namespace Aufgaben.Api.Infrastructure;

/// <summary>
/// Setzt Schema und Host jeder Anfrage auf die öffentliche Adresse aus <c>Aufgaben__BaseUrl</c>.
///
/// Die App ist nur über den Reverse Proxy erreichbar, ihre Adresse steht fest. Damit hängen die
/// Rückleitung nach der Anmeldung (redirect_uri), sichere Cookies und erzeugte Links nicht mehr davon
/// ab, dass der Proxy <c>Host</c> und <c>X-Forwarded-Proto</c> richtig weiterreicht. Fehlte das, schickte
/// die App Entra z. B. <c>http://127.0.0.1:8090/signin-oidc</c> – Entra lehnt das ab (AADSTS50011).
/// Die Adresse der Clients (X-Forwarded-For) wertet weiterhin die Forwarded-Headers-Middleware aus.
/// </summary>
public static class CanonicalUrl
{
    /// <summary>Öffentliche Adresse lesen; nur absolute http- oder https-Adressen gelten.</summary>
    public static bool TryParse(string baseUrl, out Uri publicUri)
    {
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            publicUri = uri;
            return true;
        }
        publicUri = null!;
        return false;
    }

    public static void Apply(HttpRequest request, Uri publicUri)
    {
        request.Scheme = publicUri.Scheme;
        // Authority enthält den Port nur, wenn er vom Standard abweicht (z. B. "app.example.org:8443").
        request.Host = new HostString(publicUri.Authority);
    }
}
