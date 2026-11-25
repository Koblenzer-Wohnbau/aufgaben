using System.Net;
using System.Net.Http.Json;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Data;
using Aufgaben.Api.Options;
using Aufgaben.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Aufgaben.Tests;

[Collection("api")]
public sealed class FeatureTests(AppFixture app)
{
    [Fact]
    public async Task Configured_object_id_grants_admin_access_without_persisting_an_app_role()
    {
        await app.WithDbAsync(async db =>
        {
            db.Users.Add(new User
            {
                EntraOid = AppFixture.ConfiguredAdminOid,
                DisplayName = "Verwaltung aus der Umgebung",
                Email = "verwaltung@example.test",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            return await db.SaveChangesAsync();
        });

        using var admin = await app.LoginAsync(AppFixture.ConfiguredAdminOid);
        Assert.True((await admin.GetFromJsonAsync<MeDto>("/api/me"))!.IsAdmin);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/features")).StatusCode);
        Assert.False(await app.WithDbAsync(db => db.Users.Where(u => u.EntraOid == AppFixture.ConfiguredAdminOid)
            .Select(u => u.IsAdmin).SingleAsync()));

        using var employee = await app.LoginAsync("dev-thomas");
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/admin/features")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PatchAsJsonAsync("/api/admin/features/absences", new { enabled = true })).StatusCode);
    }

    [Fact]
    public async Task Admin_can_manage_inbox_access_and_an_explicit_revocation_overrides_imported_settings()
    {
        using var admin = await app.LoginAsync("dev-anna");
        using var employee = await app.LoginAsync("dev-thomas");
        var me = (await employee.GetFromJsonAsync<MeDto>("/api/me"))!;
        Assert.True(me.ShowInbox);
        var uri = $"/api/admin/features/users/{me.Id}/inbox";

        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PatchAsJsonAsync(uri, new { enabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync(uri, new { })).StatusCode);
        try
        {
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync(uri, new { enabled = false })).StatusCode);
            Assert.False((await employee.GetFromJsonAsync<MeDto>("/api/me"))!.ShowInbox);
            Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync("/api/tasks?view=aufgaben")).StatusCode);
            var users = (await admin.GetFromJsonAsync<AdminFeaturesDto>("/api/admin/features"))!.Users;
            Assert.False(Assert.Single(users, u => u.Person.Id == me.Id).InboxEnabled);

            // Ein weiterer Start übernimmt alte Dateieninstellungen kein zweites Mal.
            await app.WithDbAsync(async db =>
            {
                await FeatureSettingsStore.SeedAsync(db, new AufgabenOptions
                {
                    InboxViewUsers = "thomas.becker@example.test",
                    AbsencesEnabled = true,
                }, CancellationToken.None);
                return true;
            });
            Assert.False((await employee.GetFromJsonAsync<MeDto>("/api/me"))!.ShowInbox);
            Assert.False((await employee.GetFromJsonAsync<MeDto>("/api/me"))!.AbsencesEnabled);
        }
        finally
        {
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync(uri, new { enabled = true })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await employee.GetAsync("/api/tasks?view=aufgaben")).StatusCode);
    }

    [Fact]
    public async Task Global_absence_switch_updates_the_api_with_no_app_restart()
    {
        using var admin = await app.LoginAsync("dev-anna");
        using var employee = await app.LoginAsync("dev-thomas");
        Assert.False((await employee.GetFromJsonAsync<MeDto>("/api/me"))!.AbsencesEnabled);
        Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync("/api/absences/status")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await employee.PostAsJsonAsync("/api/absences", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync("/api/tasks?view=vertretung&id=1")).StatusCode);

        const string uri = "/api/admin/features/absences";
        try
        {
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync(uri, new { enabled = true })).StatusCode);
            Assert.True((await employee.GetFromJsonAsync<MeDto>("/api/me"))!.AbsencesEnabled);
            Assert.Equal(HttpStatusCode.OK, (await employee.GetAsync("/api/absences/status")).StatusCode);
        }
        finally
        {
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync(uri, new { enabled = false })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync("/api/absences/status")).StatusCode);
    }
}
