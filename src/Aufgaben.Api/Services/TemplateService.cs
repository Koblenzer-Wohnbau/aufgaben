using Aufgaben.Api.Auth;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Data;
using Aufgaben.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Aufgaben.Api.Services;

/// <summary>
/// Vorlagen für wiederkehrende Abläufe. Ein Klick erzeugt alle Schritte als Aufgaben:
/// mit Fälligkeit relativ zum Stichtag, Checkliste und zuständiger Person.
/// </summary>
public sealed class TemplateService(AppDbContext db, CurrentUser me, AppClock clock, TaskService tasks)
{
    public async Task<List<TemplateDto>> ListAsync(CancellationToken ct)
    {
        var items = await db.Templates.Include(t => t.Steps).OrderBy(t => t.Name).ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<TemplateDto> CreateAsync(TemplateRequest req, CancellationToken ct)
    {
        var template = new Template { Name = ValidName(req.Name), CreatedById = me.Id, CreatedAt = clock.UtcNow };
        ApplySteps(template, req);
        db.Templates.Add(template);
        await db.SaveChangesAsync(ct);
        return ToDto(template);
    }

    public async Task<TemplateDto> UpdateAsync(int id, TemplateRequest req, CancellationToken ct)
    {
        var template = await LoadEditableAsync(id, ct);
        template.Name = ValidName(req.Name);
        db.TemplateSteps.RemoveRange(template.Steps);
        template.Steps.Clear();
        await db.SaveChangesAsync(ct);
        ApplySteps(template, req);
        await db.SaveChangesAsync(ct);
        return ToDto(template);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var template = await LoadEditableAsync(id, ct);
        db.Templates.Remove(template);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ApplyTemplateResult> ApplyAsync(int id, ApplyTemplateRequest req, CancellationToken ct)
    {
        var template = await db.Templates.Include(t => t.Steps).FirstOrDefaultAsync(t => t.Id == id, ct)
                       ?? throw ApiException.NotFound("Vorlage");
        var start = TaskService.ParseDate(req.StartDate) ?? throw ApiException.Invalid("Bitte einen Stichtag angeben.");
        var suffix = string.IsNullOrWhiteSpace(req.TitleSuffix) ? "" : " · " + req.TitleSuffix.Trim();
        var created = new List<int>();

        // Alle Schritte oder keiner: Bricht ein Schritt ab, bleiben keine halben Abläufe zurück.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var step in template.Steps.OrderBy(s => s.Position))
        {
            int? assignee = req.Assignees is not null && req.Assignees.TryGetValue(step.Id, out var a) ? a : req.DefaultAssigneeId;
            var checklist = step.ChecklistText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var dto = await tasks.CreateAsync(new CreateTaskRequest(
                Title: TaskService.ValidTitle(step.Title + suffix),
                Notes: step.Notes,
                Priority: step.Priority,
                Kind: "aufgabe",
                AssigneeId: assignee,
                ListId: null,
                ProjectId: req.ProjectId,
                DueDate: start.AddDays(step.OffsetDays).ToString("yyyy-MM-dd"),
                DueTime: null,
                RemindAt: null,
                MyDay: null,
                Recurrence: null,
                Checklist: checklist), "vorlage", ct);
            created.Add(dto.Id);
        }
        await tx.CommitAsync(ct);
        return new ApplyTemplateResult(created.Count, created);
    }

    private async Task<Template> LoadEditableAsync(int id, CancellationToken ct)
    {
        var template = await db.Templates.Include(t => t.Steps).FirstOrDefaultAsync(t => t.Id == id, ct)
                       ?? throw ApiException.NotFound("Vorlage");
        if (template.CreatedById != me.Id && !me.IsAdmin)
        {
            throw ApiException.Forbidden("Nur die Person, die die Vorlage angelegt hat, oder die Verwaltung kann sie ändern.");
        }
        return template;
    }

    private static void ApplySteps(Template template, TemplateRequest req)
    {
        template.Description = (req.Description ?? "").Trim();
        if (req.Steps is null || req.Steps.Count == 0)
        {
            throw ApiException.Invalid("Eine Vorlage braucht mindestens einen Schritt.");
        }
        if (req.Steps.Count > 60)
        {
            throw ApiException.Invalid("Höchstens 60 Schritte pro Vorlage.");
        }
        var position = 0;
        foreach (var s in req.Steps)
        {
            template.Steps.Add(new TemplateStep
            {
                Position = position++,
                Title = TaskService.ValidTitle(s.Title),
                Notes = s.Notes ?? "",
                OffsetDays = Math.Clamp(s.OffsetDays, -3650, 3650),
                Priority = TaskService.ValidPriority(s.Priority ?? "normal"),
                ChecklistText = string.Join('\n', (s.Checklist ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Take(TaskService.MaxChecklist)),
                RoleHint = (s.RoleHint ?? "").Trim(),
            });
        }
    }

    private static string ValidName(string name)
    {
        var value = (name ?? "").Trim();
        return value.Length is 0 or > 200 ? throw ApiException.Invalid("Der Name der Vorlage braucht 1 bis 200 Zeichen.") : value;
    }

    public static TemplateDto ToDto(Template t) => new(
        t.Id,
        t.Name,
        t.Description,
        t.Steps.OrderBy(s => s.Position).Select(s => new TemplateStepDto(
            s.Id, s.Position, s.Title, s.Notes, s.OffsetDays, s.Priority,
            s.ChecklistText.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList(), s.RoleHint)).ToList());
}
