using Aufgaben.Api.Infrastructure;

namespace Aufgaben.Api.Services;

/// <summary>
/// Regeln für Anhänge, die Personen selbst hochladen (Strg+V, Ziehen und Ablegen, Dateiauswahl).
/// Anhänge werden immer als Download ausgeliefert; nur echte Bilder (erkannt an den ersten Bytes,
/// nicht an Dateiname oder angegebenem Typ) dürfen zusätzlich als Vorschau angezeigt werden.
/// </summary>
public static class AttachmentRules
{
    /// <summary>Höchstgröße einer Datei. Nginx lässt für die Upload-Adresse etwas mehr durch.</summary>
    public const int MaxFileBytes = 10 * 1024 * 1024;

    /// <summary>Höchstsumme aller Anhänge einer Aufgabe, die Anhänge liegen in der Datenbank.</summary>
    public const long MaxTotalBytes = 50L * 1024 * 1024;

    public const int MaxPerTask = 30;

    /// <summary>Programme, Skripte und Verknüpfungen: lassen sich nicht anhängen.</summary>
    private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".msi", ".msp", ".msix", ".appx", ".scr", ".pif", ".cpl", ".dll", ".sys",
        ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".ps1", ".psm1", ".psd1", ".hta", ".lnk", ".url", ".jar",
        ".reg", ".iso", ".img", ".vhd", ".vhdx", ".application", ".gadget", ".sh",
    };

    public static ApiException TooLarge() =>
        new(413, $"Eine Datei darf höchstens {MaxFileBytes / 1024 / 1024} MB groß sein.");

    /// <summary>Prüft Inhalt und Namen; gibt den bereinigten Dateinamen zurück.</summary>
    public static string CheckFile(string? fileName, byte[] data)
    {
        if (data.Length == 0)
        {
            throw ApiException.Invalid("Die Datei ist leer.");
        }
        if (data.Length > MaxFileBytes)
        {
            throw TooLarge();
        }
        // Punkte und Leerzeichen am Ende entfernt Windows beim Speichern, „datei.exe.“ wäre sonst „datei.exe“.
        var name = MailInboxProcessor.SafeFileName(string.IsNullOrWhiteSpace(fileName) ? "anhang" : fileName.Trim()).TrimEnd('.', ' ');
        if (name.Length == 0)
        {
            name = "anhang";
        }
        var extension = Path.GetExtension(name);
        if (Blocked.Contains(extension))
        {
            throw ApiException.Invalid($"Dateien vom Typ „{extension}“ lassen sich nicht anhängen, Programme und Skripte sind aus Sicherheitsgründen gesperrt.");
        }
        return name;
    }

    /// <summary>Bildtyp anhand der ersten Bytes (PNG, JPEG, GIF, WebP), sonst null. SVG gilt bewusst nicht als Bild.</summary>
    public static string? ImageType(byte[] d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47 && d[4] == 0x0D && d[5] == 0x0A && d[6] == 0x1A && d[7] == 0x0A)
        {
            return "image/png";
        }
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF)
        {
            return "image/jpeg";
        }
        if (d.Length >= 6 && d[0] == 'G' && d[1] == 'I' && d[2] == 'F' && d[3] == '8' && (d[4] == '7' || d[4] == '9') && d[5] == 'a')
        {
            return "image/gif";
        }
        if (d.Length >= 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P')
        {
            return "image/webp";
        }
        return null;
    }

    /// <summary>Gespeicherter Typ: erkannter Bildtyp, sonst der angegebene (bereinigt), sonst octet-stream.</summary>
    public static string ContentType(string? declared, byte[] data)
    {
        var image = ImageType(data);
        if (image is not null)
        {
            return image;
        }
        var type = (declared ?? "").Split(';')[0].Trim().ToLowerInvariant();
        // Ein angegebener Bildtyp ohne passende erste Bytes ist kein Bild (z. B. HTML als „bild.png“):
        // nicht übernehmen, sonst hielte die Oberfläche die Datei für ein Bild.
        if (type.StartsWith("image/", StringComparison.Ordinal))
        {
            return "application/octet-stream";
        }
        var valid = type.Length is > 2 and <= 127 && type.Count(c => c == '/') == 1
                    && type.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '.' or '+' or '-');
        return valid ? type : "application/octet-stream";
    }

    /// <summary>Liest den Body bis zur Grenze; darüber 413, ohne den Rest in den Speicher zu holen.</summary>
    public static async Task<byte[]> ReadLimitedAsync(Stream body, int limit, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        try
        {
            int read;
            while ((read = await body.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > limit)
                {
                    throw TooLarge();
                }
                buffer.Write(chunk, 0, read);
            }
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            throw TooLarge();
        }
        return buffer.ToArray();
    }
}
