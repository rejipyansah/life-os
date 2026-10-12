namespace LifeOS.Api.Services;

public static partial class FinanceEndpoints
{
    private static void MapFinanceStateEndpoint(RouteGroupBuilder finance)
    {
        finance.MapGet("/state", async (
            HttpContext http,
            FinanceStateService stateService) =>
            Results.Ok(await stateService.GetStateAsync(GetScopeId(http))))
            .WithName("GetFinanceState");
    }
}
