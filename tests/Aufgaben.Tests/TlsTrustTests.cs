using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Aufgaben.Api.Services;

namespace Aufgaben.Tests;

public class TlsTrustTests
{
    private static X509Certificate2 SelfSigned(string name)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
    }

    private static bool Check(RemoteCertificateValidationCallback callback, X509Certificate2 cert, SslPolicyErrors errors) =>
        callback(new object(), cert, null, errors);

    [Fact]
    public void Without_settings_the_normal_check_applies() =>
        Assert.Null(TlsTrust.Callback(null, ""));

    [Fact]
    public void A_pinned_fingerprint_accepts_exactly_that_certificate_even_self_signed_and_under_another_name()
    {
        using var exchange = SelfSigned("kwbexse.kowobau.local");
        using var other = SelfSigned("kwbexse.kowobau.local");
        // Schreibweise wie von openssl: mit Doppelpunkten, gemischte Groß- und Kleinschreibung
        var sha256 = string.Join(":", Convert.ToHexString(exchange.GetCertHash(HashAlgorithmName.SHA256)).Chunk(2).Select(c => new string(c))).ToLowerInvariant();

        var callback = TlsTrust.Callback(null, sha256)!;
        const SslPolicyErrors selfSignedAndWrongName = SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch;
        Assert.True(Check(callback, exchange, selfSignedAndWrongName));
        Assert.False(Check(callback, other, selfSignedAndWrongName)); // gleicher Name, anderes Zertifikat
        Assert.False(Check(callback, other, SslPolicyErrors.None)); // auch ein „gültiges“ anderes nicht
    }

    [Fact]
    public void Sha1_fingerprints_work_too()
    {
        using var exchange = SelfSigned("kwbexse.kowobau.local");
        var callback = TlsTrust.Callback(null, exchange.Thumbprint)!; // SHA-1, wie Windows es anzeigt
        Assert.True(Check(callback, exchange, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Smtp_login_uses_username_and_password_when_offered()
    {
        var exchange = new HashSet<string> { "GSSAPI", "NTLM", "LOGIN" };
        SmtpLogin.PreferPassword(exchange);
        Assert.Equal(["LOGIN"], exchange);

        var onlyNtlm = new HashSet<string> { "NTLM" };
        SmtpLogin.PreferPassword(onlyNtlm);
        Assert.Equal(["NTLM"], onlyNtlm); // nichts anderes da – dann bleibt es dabei
    }

    [Theory]
    [InlineData("12:34")]
    [InlineData("zz11223344556677889900aabbccddeeff00112233")]
    public void Broken_fingerprints_are_reported(string value) =>
        Assert.Throws<InvalidOperationException>(() => TlsTrust.NormalizeThumbprint(value));
}
