using System.Text.Json;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Services;

namespace Aufgaben.Api.Endpoints;

public static class TaskEndpoints
{
    public static void MapTaskEndpoints(this RouteGroupBuilder api)
    {
        var tasks = api.MapGroup("/tasks");

        tasks.MapGet("/", (string? view, int? id, string? q, TaskService svc, CancellationToken ct) =>
            svc.ListAsync(view ?? "mein-tag", id, q, ct));

        tasks.MapGet("/{id:int}", (int id, TaskService svc, CancellationToken ct) => svc.GetAsync(id, ct));

        tasks.MapPost("/", async (CreateTaskRequest req, TaskService svc, CancellationToken ct) =>
        {
            var created = await svc.CreateAsync(req, "app", ct);
            return Results.Created($"/api/tasks/{created.Id}", created);
        });

        tasks.MapPatch("/{id:int}", (int id, JsonElement body, TaskService svc, CancellationToken ct) => svc.PatchAsync(id, body, ct));

        tasks.MapPost("/{id:int}/complete", (int id, TaskService svc, CancellationToken ct) => svc.CompleteAsync(id, ct));

        tasks.MapPost("/{id:int}/reopen", (int id, TaskService svc, CancellationToken ct) => svc.ReopenAsync(id, ct));

        tasks.MapDelete("/{id:int}", async (int id, TaskService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        tasks.MapPost("/{id:int}/checklist", (int id, ChecklistRequest req, TaskService svc, CancellationToken ct) =>
            svc.AddChecklistItemAsync(id, req, ct));

        tasks.MapPatch("/{id:int}/checklist/{itemId:int}", (int id, int itemId, ChecklistRequest req, TaskService svc, CancellationToken ct) =>
            svc.UpdateChecklistItemAsync(id, itemId, req, ct));

        tasks.MapDelete("/{id:int}/checklist/{itemId:int}", (int id, int itemId, TaskService svc, CancellationToken ct) =>
            svc.DeleteChecklistItemAsync(id, itemId, ct));

        tasks.MapPost("/{id:int}/comments", (int id, CommentRequest req, TaskService svc, CancellationToken ct) =>
            svc.CommentAsync(id, req, ct));

        // Anhänge immer als Download ausliefern, nie im Browser anzeigen lassen
        // (eine HTML- oder SVG-Datei aus einer Mail darf hier keinen Code ausführen).
        tasks.MapGet("/{id:int}/attachments/{attachmentId:int}", async (int id, int attachmentId, HttpContext ctx, TaskService svc, CancellationToken ct) =>
        {
            var (meta, data) = await svc.GetAttachmentAsync(id, attachmentId, ct);
            ctx.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
            ctx.Response.Headers.CacheControl = "private, no-store";
            return Results.File(data, "application/octet-stream", meta.FileName);
        });

        api.MapGet("/counts", (TaskService svc, CancellationToken ct) => svc.CountsAsync(ct));
    }
}
