using LifeOS.Api.Models;

namespace LifeOS.Api.Services;

public static partial class FinanceEndpoints
{
    private static void MapUpcomingEventEndpoints(RouteGroupBuilder finance)
    {
        finance.MapGet("/upcoming-events", async (
            HttpContext http,
            UpcomingEventService eventService) =>
            Results.Ok(new { items = await eventService.GetAsync(GetScopeId(http)) }))
            .WithName("GetUpcomingEvents");

        finance.MapPost("/upcoming-events", async (
            HttpContext http,
            CreateUpcomingEventCommand? body,
            UpcomingEventService eventService) =>
        {
            if (body is null) return Error("Invalid request body.", 400);
            body.ScopeId = GetScopeId(http);

            var created = await eventService.CreateAsync(body);
            return Results.Ok(await eventService.GetByIdAsync(created.Id, GetScopeId(http)));
        }).WithName("CreateUpcomingEvent");

        finance.MapGet("/upcoming-events/{id:guid}", async (
            HttpContext http,
            Guid id,
            UpcomingEventService eventService) =>
        {
            var agenda = await eventService.GetByIdAsync(id, GetScopeId(http));
            return agenda is null ? Error("Agenda not found.", 404) : Results.Ok(agenda);
        }).WithName("GetUpcomingEventById");

        finance.MapPatch("/upcoming-events/{id:guid}", async (
            HttpContext http,
            Guid id,
            UpdateUpcomingEventCommand? body,
            UpcomingEventService eventService) =>
        {
            if (body is null) return Error("Invalid request body.", 400);
            body.ScopeId = GetScopeId(http);

            await eventService.UpdateAsync(id, body);
            return Results.Ok(await eventService.GetByIdAsync(id, GetScopeId(http)));
        }).WithName("UpdateUpcomingEvent");

        finance.MapPost("/upcoming-events/{id:guid}/postpone", async (
            HttpContext http,
            Guid id,
            PostponeUpcomingEventCommand? body,
            UpcomingEventService eventService) =>
        {
            body ??= new PostponeUpcomingEventCommand();
            body.ScopeId = GetScopeId(http);

            await eventService.PostponeAsync(id, body);
            return Results.Ok(await eventService.GetByIdAsync(id, GetScopeId(http)));
        }).WithName("PostponeUpcomingEvent");

        finance.MapPost("/upcoming-events/{id:guid}/skip", async (
            HttpContext http,
            Guid id,
            SettleUpcomingEventCommand? body,
            UpcomingEventService eventService) =>
        {
            body ??= new SettleUpcomingEventCommand();
            body.ScopeId = GetScopeId(http);

            await eventService.SkipAsync(id, body);
            return Results.Ok(await eventService.GetByIdAsync(id, GetScopeId(http)));
        }).WithName("SkipUpcomingEvent");

        finance.MapPost("/upcoming-events/{id:guid}/cancel", async (
            HttpContext http,
            Guid id,
            SettleUpcomingEventCommand? body,
            UpcomingEventService eventService) =>
        {
            body ??= new SettleUpcomingEventCommand();
            body.ScopeId = GetScopeId(http);

            await eventService.CancelAsync(id, body);
            return Results.Ok(await eventService.GetByIdAsync(id, GetScopeId(http)));
        }).WithName("CancelUpcomingEvent");

        finance.MapPost("/upcoming-events/{id:guid}/realize", async (
            HttpContext http,
            Guid id,
            RealizeUpcomingEventCommand? body,
            UpcomingEventService eventService) =>
        {
            body ??= new RealizeUpcomingEventCommand();
            body.ScopeId = GetScopeId(http);

            return Results.Ok(await eventService.RealizeAsync(id, body));
        }).WithName("RealizeUpcomingEvent");

        finance.MapDelete("/upcoming-events/{id:guid}", async (
            HttpContext http,
            Guid id,
            UpcomingEventService eventService) =>
        {
            await eventService.DeleteAsync(id, GetScopeId(http));
            return Results.NoContent();
        }).WithName("DeleteUpcomingEvent");
    }
}
