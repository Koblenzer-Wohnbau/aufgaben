using System.Text.Json;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Infrastructure;
using Aufgaben.Api.Services;
using Microsoft.AspNetCore.Http.Features;

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
            // Aus einem bestätigten KI-Vorschlag: im Verlauf steht „mit KI-Vorschlag angelegt“.
            var created = await svc.CreateAsync(req, req.FromAi == true ? "ki" : "app", ct);
            return Results.Created($"/api/tasks/{created.Id}", created);
        });

        tasks.MapPatch("/{id:int}", (int id, JsonElement body, TaskService svc, CancellationToken ct) => svc.PatchAsync(id, body, ct));

        tasks.MapPost("/{id:int}/complete", (int id, TaskService svc, CancellationToken ct) => svc.CompleteAsync(id, ct));

        tasks.MapPost("/{id:int}/reopen", (int id, TaskService svc, CancellationToken ct) => svc.ReopenAsync(id, ct));

        // Löschen legt in den Papierkorb; endgültig gelöscht wird nur von dort.
        tasks.MapDelete("/{id:int}", async (int id, TaskService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        tasks.MapPost("/{id:int}/restore", (int id, TaskService svc, CancellationToken ct) => svc.RestoreAsync(id, ct));

        tasks.MapDelete("/{id:int}/endgueltig", async (int id, TaskService svc, CancellationToken ct) =>
        {
            await svc.PurgeAsync(id, ct);
            return Results.NoContent();
        });

        tasks.MapPost("/bulk", (BulkRequest req, TaskService svc, CancellationToken ct) => svc.BulkAsync(req, ct));

        tasks.MapPost("/erledigte-loeschen", (TaskService svc, CancellationToken ct) => svc.DeleteCompletedAsync(ct));

        api.MapPost("/papierkorb/leeren", async (TaskService svc, CancellationToken ct) =>
            new BulkResultDto(await svc.EmptyTrashAsync(ct), 0));

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

        // Vorschau nur für echte Bilder (erkannt an den ersten Bytes) – alles andere bleibt Download.
        tasks.MapGet("/{id:int}/attachments/{attachmentId:int}/vorschau", async (int id, int attachmentId, HttpContext ctx, TaskService svc, CancellationToken ct) =>
        {
            var (_, data) = await svc.GetAttachmentAsync(id, attachmentId, ct);
            var type = AttachmentRules.ImageType(data) ?? throw ApiException.NotFound("Vorschau");
            ctx.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
            ctx.Response.Headers.CacheControl = "private, no-store";
            return Results.File(data, type);
        });

        // Anhang hochladen: Dateiinhalt roh im Body, Name als ?name=…. Die größere Grenze gilt nur hier
        // (sonst 1 MB pro Anfrage); Nginx braucht dafür eine eigene location (deploy/nginx).
        tasks.MapPost("/{id:int}/attachments", async (int id, string? name, HttpContext ctx, TaskService svc, CancellationToken ct) =>
        {
            var sizeFeature = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false })
            {
                sizeFeature.MaxRequestBodySize = AttachmentRules.MaxFileBytes + 1;
            }
            if (ctx.Request.ContentLength > AttachmentRules.MaxFileBytes)
            {
                throw AttachmentRules.TooLarge();
            }
            var data = await AttachmentRules.ReadLimitedAsync(ctx.Request.Body, AttachmentRules.MaxFileBytes, ct);
            return await svc.AddAttachmentAsync(id, name, ctx.Request.ContentType, data, ct);
        });

        tasks.MapDelete("/{id:int}/attachments/{attachmentId:int}", (int id, int attachmentId, TaskService svc, CancellationToken ct) =>
            svc.DeleteAttachmentAsync(id, attachmentId, ct));

        api.MapGet("/counts", (TaskService svc, CancellationToken ct) => svc.CountsAsync(ct));
        api.MapGet("/suggestions", (TaskService svc, CancellationToken ct) => svc.SuggestionsAsync(ct));
    }
}
