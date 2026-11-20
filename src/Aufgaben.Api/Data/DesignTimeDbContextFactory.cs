using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aufgaben.Api.Data;

/// <summary>
/// Nur für "dotnet ef migrations add": erzeugt den DbContext ohne die App zu starten
/// (und damit ohne Entra-Konfiguration). Es wird keine Verbindung aufgebaut.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=aufgaben_design;Username=design;Password=design")
            .Options;
        return new AppDbContext(options);
    }
}
