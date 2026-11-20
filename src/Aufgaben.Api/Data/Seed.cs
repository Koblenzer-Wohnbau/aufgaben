using Aufgaben.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Aufgaben.Api.Data;

public static class Seed
{
    /// <summary>Beispielvorlagen für typische Abläufe einer Wohnungsbaugesellschaft. Nur wenn noch keine Vorlage existiert.</summary>
    public static async Task ExampleTemplatesAsync(AppDbContext db, AppClock clock, CancellationToken ct)
    {
        if (await db.Templates.AnyAsync(ct))
        {
            return;
        }
        var now = clock.UtcNow;

        db.Templates.Add(new Template
        {
            Name = "Mieterwechsel",
            Description = "Stichtag = Auszugstermin. Von der Kündigung bis zur Übergabe an die neue Mietpartei.",
            CreatedAt = now,
            Steps =
            [
                Step(0, "Kündigung bestätigen und Vorabnahme terminieren", -30, "hoch", "Kündigungseingang prüfen\nBestätigung versenden\nTermin Vorabnahme vereinbaren", "Vermietung"),
                Step(1, "Vorabnahme durchführen", -21, "normal", "Zustand dokumentieren (Fotos)\nSchönheitsreparaturen festhalten\nZählerstände notieren", "Technik"),
                Step(2, "Wohnung inserieren und Besichtigungen planen", -20, "normal", "Exposé erstellen\nInserat veröffentlichen\nBesichtigungstermine koordinieren", "Vermietung"),
                Step(3, "Endabnahme und Schlüsselrückgabe", 0, "hoch", "Abnahmeprotokoll unterschreiben lassen\nSchlüssel zählen\nZählerstände ablesen", "Technik"),
                Step(4, "Instandsetzung beauftragen", 2, "normal", "Mängelliste an Handwerker\nAuftrag freigeben", "Technik"),
                Step(5, "Übergabe an neue Mietpartei", 14, "hoch", "Übergabeprotokoll\nSchlüsselübergabe dokumentieren\nZählerstände", "Vermietung"),
                Step(6, "Kaution abrechnen", 30, "normal", "Nebenkosten berücksichtigen\nAbrechnung versenden", "Buchhaltung"),
            ],
        });

        db.Templates.Add(new Template
        {
            Name = "Wohnungsabnahme nach Sanierung",
            Description = "Stichtag = geplanter Abnahmetermin.",
            CreatedAt = now,
            Steps =
            [
                Step(0, "Abnahmetermin mit Gewerken abstimmen", -7, "normal", "Elektro\nSanitär\nMaler\nBodenleger", "Bauleitung"),
                Step(1, "Begehung und Mängelliste", 0, "hoch", "Fotos je Raum\nMängel je Gewerk erfassen", "Bauleitung"),
                Step(2, "Mängelbeseitigung nachhalten", 10, "normal", "", "Bauleitung"),
                Step(3, "Gewährleistungsfristen eintragen", 14, "normal", "Fristen je Gewerk als Wiedervorlage anlegen", "Bauleitung"),
            ],
        });

        db.Templates.Add(new Template
        {
            Name = "Mängelmeldung Mieter",
            Description = "Stichtag = Eingang der Meldung.",
            CreatedAt = now,
            Steps =
            [
                Step(0, "Meldung aufnehmen und Eingang bestätigen", 0, "hoch", "Kontaktdaten prüfen\nFotos anfordern", "Kundenservice"),
                Step(1, "Vor-Ort-Prüfung", 2, "normal", "", "Hausmeister"),
                Step(2, "Handwerker beauftragen", 3, "normal", "", "Technik"),
                Step(3, "Rückmeldung an Mietpartei", 7, "normal", "", "Kundenservice"),
            ],
        });

        await db.SaveChangesAsync(ct);
    }

    private static TemplateStep Step(int position, string title, int offset, string priority, string checklist, string role) => new()
    {
        Position = position,
        Title = title,
        OffsetDays = offset,
        Priority = priority,
        ChecklistText = checklist,
        RoleHint = role,
    };

    /// <summary>Testpersonen, Projekte und Aufgaben für die lokale Entwicklung (nur im Modus "Dev").</summary>
    public static async Task DevDataAsync(AppDbContext db, AppClock clock, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
        {
            return;
        }
        var now = clock.UtcNow;
        var today = clock.Today;

        User U(string oid, string name, string email, string job, bool admin = false, bool teams = true) => new()
        {
            EntraOid = oid,
            DisplayName = name,
            Email = email,
            Upn = email,
            JobTitle = job,
            IsAdmin = admin,
            TeamsSeenAt = teams ? now : null,
            CreatedAt = now,
        };

        var anna = U("dev-anna", "Anna Schneider", "anna.schneider@example.test", "Projektleitung Neubau", admin: true);
        var thomas = U("dev-thomas", "Thomas Becker", "thomas.becker@example.test", "Bauleitung");
        var maria = U("dev-maria", "Maria Klein", "maria.klein@example.test", "Vermietung");
        var jonas = U("dev-jonas", "Jonas Wagner", "jonas.wagner@example.test", "Hausmeister", teams: false);
        db.Users.AddRange(anna, thomas, maria, jonas);

        Project P(string number, string name, string team) => new() { Number = number, Name = name, TeamName = team, CreatedAt = now };
        // Erfundene Beispielprojekte – Kanalnamen beginnen mit einer Projektnummer.
        var lindenpark = P("1001", "Lindenpark", "Neubau");
        var sonnenhang = P("1002", "Am Sonnenhang 5-9", "Neubau");
        var gartenstrasse = P("2001", "Gartenstraße 12-14", "Sanierung");
        var bergweg = P("2002", "Bergweg 3-7", "Sanierung");
        db.Projects.AddRange(lindenpark, sonnenhang, gartenstrasse, bergweg);
        await db.SaveChangesAsync(ct);

        var list = new TaskList { OwnerId = anna.Id, Name = "Einkauf und Angebote", Color = "orange", CreatedAt = now };
        db.TaskLists.Add(list);
        await db.SaveChangesAsync(ct);

        TaskItem T(string title, User creator, User assignee, Project? project = null, int? dueIn = null, string priority = "normal",
            string kind = "aufgabe", bool myDay = false, TimeOnly? time = null, string[]? checklist = null, bool done = false, string notes = "")
        {
            var t = new TaskItem
            {
                Title = title,
                Notes = notes,
                CreatedById = creator.Id,
                AssigneeId = assignee.Id,
                ProjectId = project?.Id,
                DueDate = dueIn is int d ? today.AddDays(d) : null,
                DueTime = time,
                Priority = priority,
                Kind = kind,
                MyDayOn = myDay ? today : null,
                CreatedAt = now.AddMinutes(-Random.Shared.Next(60, 6000)),
                UpdatedAt = now,
            };
            if (done)
            {
                t.Status = Values.Done;
                t.CompletedAt = now.AddMinutes(-20);
                t.CompletedById = assignee.Id;
            }
            var i = 0;
            foreach (var c in checklist ?? [])
            {
                t.Checklist.Add(new ChecklistItem { Title = c, Position = i, Done = i++ < 2 });
            }
            return t;
        }

        db.Tasks.AddRange(
            T("Mängelliste Whg. 3.2 an Elektriker schicken", anna, anna, lindenpark, -1, "hoch"),
            T("Abnahmeprotokoll Treppenhaus prüfen", anna, anna, bergweg, 0, time: new TimeOnly(10, 0),
                checklist: ["Fotos sichten", "Mängel je Etage", "Protokoll gegenzeichnen", "An Hausverwaltung senden", "Termin Nachbegehung", "Ablage"]),
            T("Rückruf Mieterin wegen Heizung (Whg. 1.4)", anna, anna, gartenstrasse, 0, kind: "wiedervorlage",
                notes: "Heizkörper Wohnzimmer wird nicht warm. Monteur war am Montag da."),
            T("Angebote Gerüstbau vergleichen", anna, anna, sonnenhang, 2, myDay: true),
            T("Schlussrechnung Fenster freigeben", anna, maria, lindenpark, 4),
            T("Baustellenfotos hochladen", anna, thomas, lindenpark, 0, done: true),
            T("Zählerstände Haus 2 ablesen", anna, jonas, bergweg, 1),
            T("Gewährleistung Dach prüfen", anna, anna, null, 21, kind: "wiedervorlage"),
            T("Budgetübersicht für GF-Runde vorbereiten", thomas, anna, null, 1, "hoch"),
            T("Muster für Bodenbeläge anfordern", anna, anna, null, 3));
        await db.SaveChangesAsync(ct);

        var angebote = await db.Tasks.FirstAsync(t => t.Title.StartsWith("Muster"), ct);
        angebote.ListId = list.Id;

        // Beispiel für "Mail an Aufgabe" (inkl. Anhang)
        var mailTask = T("Angebot Fensterbau prüfen", anna, anna, lindenpark, 3, notes:
            "Per E-Mail von Anna Schneider am " + clock.LocalNow.ToString("dd.MM.yyyy") + " 08:12\n\n" +
            "---------- Weitergeleitete Nachricht ----------\nVon: Fensterbau Beispiel GmbH\n\n" +
            "Sehr geehrte Damen und Herren,\nanbei unser Angebot für die Fenster in Haus 2.\nMit freundlichen Grüßen");
        mailTask.Source = "mail";
        db.Tasks.Add(mailTask);
        await db.SaveChangesAsync(ct);

        var content = System.Text.Encoding.UTF8.GetBytes("Beispielangebot – nur Testdaten.\nPosition 1: Fenster 120x140, 12 Stück\n");
        var attachment = new TaskAttachment
        {
            TaskId = mailTask.Id,
            FileName = "Angebot_Fensterbau.txt",
            ContentType = "text/plain",
            Size = content.Length,
            CreatedAt = now,
        };
        db.TaskAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        db.AttachmentBlobs.Add(new AttachmentBlob { AttachmentId = attachment.Id, Data = content });
        await db.SaveChangesAsync(ct);
    }
}
