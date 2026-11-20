using Aufgaben.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aufgaben.Tests;

public sealed class GraphCredentialTests
{
    private static IConfigurationSection AzureAd(string certPath) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = "11111111-2222-3333-4444-555555555555",
            ["AzureAd:ClientId"] = "66666666-7777-8888-9999-000000000000",
            ["AzureAd:ClientCredentials:0:SourceType"] = "Path",
            ["AzureAd:ClientCredentials:0:CertificateDiskPath"] = certPath,
            ["AzureAd:ClientCredentials:0:CertificatePassword"] = "falsch",
        })
        .Build()
        .GetSection("AzureAd");

    [Fact]
    public void Unreadable_certificate_disables_graph_instead_of_crashing()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "kein Zertifikat");
            Assert.Null(GraphClient.BuildCredential(AzureAd(path), NullLogger.Instance));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_certificate_disables_graph()
    {
        Assert.Null(GraphClient.BuildCredential(AzureAd(Path.Combine(Path.GetTempPath(), "fehlt-" + Guid.NewGuid() + ".pfx")), NullLogger.Instance));
    }
}
