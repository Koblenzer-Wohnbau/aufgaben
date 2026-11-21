using Aufgaben.Api.Contracts;
using Aufgaben.Api.Services;

namespace Aufgaben.Api.Endpoints;

/// <summary>Urlaubsübergabe – jede Person verwaltet nur ihren eigenen Urlaub.</summary>
public static class AbsenceEndpoints
{
    public static void MapAbsenceEndpoints(this RouteGroupBuilder api)
    {
        var absences = api.MapGroup("/absences");

        absences.MapGet("/status", (AbsenceService svc, CancellationToken ct) => svc.StatusAsync(ct));

        absences.MapPost("/", async (AbsenceRequest req, AbsenceService svc, CancellationToken ct) =>
        {
            var created = await svc.CreateAsync(req, ct);
            return Results.Created($"/api/absences/{created.Id}", created);
        });

        absences.MapDelete("/{id:int}", async (int id, AbsenceService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        absences.MapPost("/{id:int}/end", (int id, AbsenceService svc, CancellationToken ct) => svc.EndAsync(id, ct));

        absences.MapGet("/{id:int}/plan", (int id, AbsenceService svc, CancellationToken ct) => svc.PlanAsync(id, ct));

        absences.MapPost("/{id:int}/tasks/{taskId:int}", (int id, int taskId, AbsenceDecisionRequest req, AbsenceService svc, CancellationToken ct) =>
            svc.DecideAsync(id, taskId, req, ct));

        absences.MapPost("/{id:int}/handover-all", (int id, AbsenceService svc, CancellationToken ct) => svc.HandOverAllAsync(id, ct));

        absences.MapPost("/{id:int}/take-back", (int id, AbsenceTaskIdsRequest req, AbsenceService svc, CancellationToken ct) =>
            svc.TakeBackAsync(id, req, ct));

        absences.MapGet("/{id:int}/summary", (int id, AbsenceService svc, CancellationToken ct) => svc.SummaryAsync(id, ct));

        absences.MapPost("/{id:int}/summary/close", async (int id, AbsenceService svc, CancellationToken ct) =>
        {
            await svc.CloseSummaryAsync(id, ct);
            return Results.NoContent();
        });
    }
}
