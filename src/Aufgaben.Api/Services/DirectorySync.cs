using System.Text.Json;
using Aufgaben.Api.Data;
using Aufgaben.Api.Infrastructure;
using Aufgaben.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aufgaben.Api.Services;

/// <summary>
/// Übernimmt Personen aus Entra, damit man auch Kolleginnen Aufgaben zuweisen kann,
/// die sich noch nie angemeldet haben (und auch solche ohne Teams-Lizenz).
/// Benötigt die Anwendungsberechtigung User.Read.All (bei Gruppenfilter zusätzlich GroupMember.Read.All).
/// </summary>
public sealed class DirectorySync(AppDbContext db, GraphClient graph, AppClock clock, IOptions<AufgabenOptions> options, ILogger<DirectorySync> log)
{
    public static DateTimeOffset? LastRun { get; private set; }
    public static string? LastResult { get; private set; }

    private const string Select = "$select=id,displayName,mail,userPrincipalName,jobTitle,accountEnabled,userType";

    public async Task<(int Added, int Updated, int Deactivated)> RunAsync(CancellationToken ct)
    {
        if (!graph.IsConfigured)
        {
            throw new InvalidOperationException("Microsoft Graph ist nicht konfiguriert.");
        }
        var groupId = options.Value.Directory.GroupId;
        var url = string.IsNullOrWhiteSpace(groupId)
            ? $"users?{Select}&$filter=accountEnabled eq true and userType eq 'Member'&$count=true&$top=999"
            : $"groups/{Uri.EscapeDataString(groupId)}/transitiveMembers/microsoft.graph.user?{Select}&$top=999";

        var seen = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        await foreach (var item in graph.ListAsync(url, ct))
        {
            var oid = item.GetProperty("id").GetString();
            var enabled = !item.TryGetProperty("accountEnabled", out var en) || en.ValueKind != JsonValueKind.False;
            var member = !item.TryGetProperty("userType", out var ut) || ut.GetString() is null or "Member";
            if (!string.IsNullOrEmpty(oid) && enabled && member)
            {
                seen[oid] = item;
            }
        }

        if (seen.Count == 0)
        {
            // Sicherheitsnetz: eine leere Antwort deaktiviert nicht alle Personen.
            LastRun = clock.UtcNow;
            LastResult = "Keine Personen geliefert – nichts geändert.";
            return (0, 0, 0);
        }

        var existing = await db.Users.ToDictionaryAsync(u => u.EntraOid, StringComparer.OrdinalIgnoreCase, ct);
        int added = 0, updated = 0, deactivated = 0;
        var now = clock.UtcNow;

        foreach (var (oid, item) in seen)
        {
            var name = Str(item, "displayName") ?? Str(item, "userPrincipalName") ?? "Unbekannt";
            var mail = Str(item, "mail");
            var upn = Str(item, "userPrincipalName");
            var job = Str(item, "jobTitle");
            if (existing.TryGetValue(oid, out var user))
            {
                if (user.DisplayName != name || user.Email != (mail ?? user.Email) || user.Upn != upn || user.JobTitle != job || !user.IsActive)
                {
                    user.DisplayName = name;
                    user.Email = mail ?? user.Email;
                    user.Upn = upn;
                    user.JobTitle = job;
                    user.IsActive = true;
                    updated++;
                }
            }
            else
            {
                db.Users.Add(new User
                {
                    EntraOid = oid,
                    DisplayName = name,
                    Email = mail,
                    Upn = upn,
                    JobTitle = job,
                    CreatedAt = now,
                });
                added++;
            }
        }

        foreach (var user in existing.Values.Where(u => u.IsActive && !seen.ContainsKey(u.EntraOid)))
        {
            user.IsActive = false;
            deactivated++;
        }

        await db.SaveChangesAsync(ct);
        LastRun = now;
        LastResult = $"{seen.Count} Personen: {added} neu, {updated} aktualisiert, {deactivated} deaktiviert";
        log.LogInformation("Verzeichnisabgleich: {Result}", LastResult);
        return (added, updated, deactivated);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
