using Aufgaben.Api.Data;
using Aufgaben.Api.Options;
using Microsoft.EntityFrameworkCore;

namespace Aufgaben.Api.Services;

/// <summary>Liest den aktuellen Funktionsstand einmal pro Anfrage oder Hintergrundvorgang.</summary>
public sealed class FeatureSettingsStore(AppDbContext db)
{
    private Task<FeatureSettings>? _settings;

    public Task<FeatureSettings> GetAsync(CancellationToken ct) =>
        _settings ??= db.FeatureSettings.AsNoTracking().SingleAsync(s => s.Id == FeatureSettings.SingletonId, ct);

    public static Task SeedAsync(AppDbContext db, AufgabenOptions initial, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO feature_settings ("Id", "AbsencesEnabled", "ImportedInboxUsers")
            VALUES ({FeatureSettings.SingletonId}, {initial.AbsencesEnabled}, {initial.InboxViewUsers ?? ""})
            ON CONFLICT ("Id") DO NOTHING
            """,
            ct);
}
