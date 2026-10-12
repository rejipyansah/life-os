using LifeOS.Api.Models;

namespace LifeOS.Api.Services;

public static partial class FinanceEndpoints
{
    private static void MapSetAsideEndpoints(RouteGroupBuilder finance)
    {
        finance.MapGet("/set-asides", async (
            HttpContext http,
            SetAsideService setAsideService) =>
            Results.Ok(new { items = await setAsideService.GetSetAsidesAsync(GetScopeId(http)) }))
            .WithName("GetSetAsides");

        finance.MapPost("/set-asides", async (
            HttpContext http,
            CreateSetAsideCommand? body,
            SetAsideService setAsideService) =>
        {
            if (body is null) return Error("Invalid request body.", 400);
            body.ScopeId = GetScopeId(http);

            await setAsideService.CreateSetAsideAsync(body);
            var created = (await setAsideService.GetSetAsidesAsync(GetScopeId(http))).First();
            return Results.Ok(created);
        }).WithName("CreateSetAside");

        finance.MapGet("/set-asides/{id:guid}", async (
            HttpContext http,
            Guid id,
            SetAsideService setAsideService) =>
        {
            var setAside = await setAsideService.GetSetAsideAsync(id, GetScopeId(http));
            return setAside is null ? Error("Set-aside not found.", 404) : Results.Ok(setAside);
        }).WithName("GetSetAsideById");

        finance.MapGet("/set-asides/{id:guid}/history", async (
            HttpContext http,
            Guid id,
            string? cursor,
            int? pageSize,
            SetAsideService setAsideService) =>
        {
            try
            {
                return Results.Ok(await setAsideService.GetHistoryAsync(
                    id,
                    GetScopeId(http),
                    cursor,
                    pageSize ?? 30));
            }
            catch (ValidationException ex) { return Error(ex.Message, 404); }
        }).WithName("GetSetAsideHistory");

        finance.MapPatch("/set-asides/{id:guid}", async (
            HttpContext http,
            Guid id,
            UpdateSetAsideCommand? body,
            SetAsideService setAsideService) =>
        {
            if (body is null) return Error("Invalid request body.", 400);
            body.ScopeId = GetScopeId(http);

            await setAsideService.UpdateSetAsideAsync(id, body);
            return Results.Ok(await setAsideService.GetSetAsideAsync(id, GetScopeId(http)));
        }).WithName("UpdateSetAside");

        finance.MapPost("/set-asides/{id:guid}/add", async (
            HttpContext http,
            Guid id,
            AddToSetAsideCommand? body,
            SetAsideService setAsideService) =>
        {
            if (body is null) return Error("Invalid request body.", 400);
            body.ScopeId = GetScopeId(http);

            return Results.Ok(await setAsideService.AddAsync(id, body));
        }).WithName("AddToSetAside");

        finance.MapPost("/set-asides/{id:guid}/withdraw", async (
            HttpContext http,
            Guid id,
            WithdrawFromSetAsideCommand? body,
            SetAsideService setAsideService) =>
        {
            if (body is null) return Error("Invalid request body.", 400);
            body.ScopeId = GetScopeId(http);

            return Results.Ok(await setAsideService.WithdrawAsync(id, body));
        }).WithName("WithdrawFromSetAside");

        finance.MapPost("/set-asides/{id:guid}/spend", async (
            HttpContext http,
            Guid id,
            SpendFromSetAsideCommand? body,
            SetAsideService setAsideService) =>
        {
            if (body is null) return Error("Invalid request body.", 400);
            body.ScopeId = GetScopeId(http);

            return Results.Ok(await setAsideService.SpendAsync(id, body));
        }).WithName("SpendFromSetAside");

        finance.MapPost("/set-asides/{id:guid}/close", async (
            HttpContext http,
            Guid id,
            CloseSetAsideCommand? body,
            SetAsideService setAsideService) =>
        {
            body ??= new CloseSetAsideCommand();
            body.ScopeId = GetScopeId(http);

            return Results.Ok(await setAsideService.CloseAsync(id, body));
        }).WithName("CloseSetAside");
    }
}
