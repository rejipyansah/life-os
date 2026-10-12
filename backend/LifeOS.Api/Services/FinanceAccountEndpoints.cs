using LifeOS.Api.Models;

namespace LifeOS.Api.Services;

public static partial class FinanceEndpoints
{
    private static void MapAccountEndpoints(RouteGroupBuilder finance)
    {
        finance.MapGet("/accounts", async (
            HttpContext http,
            bool? includeArchived,
            AccountService accountService) =>
            Results.Ok(await accountService.GetAccountsAsync(
                GetScopeId(http), includeArchived ?? false)))
            .WithName("GetAccounts");

        finance.MapPost("/accounts", async (
            HttpContext http,
            CreateAccountCommand? body,
            AccountService accountService) =>
        {
            if (body is null) return Error("Invalid request body.", 400);
            body.ScopeId = GetScopeId(http);

            var account = await accountService.CreateAccountAsync(body);
            return Results.Ok(new
            {
                accountId = account.Id,
                name = account.Name,
                type = account.Type,
                isArchived = account.IsArchived,
                createdAt = account.CreatedAt
            });
        }).WithName("CreateAccount");

        finance.MapGet("/accounts/{id:guid}", async (
            HttpContext http,
            Guid id,
            AccountService accountService) =>
        {
            try
            {
                return Results.Ok(await accountService.GetAccountByIdAsync(id, GetScopeId(http)));
            }
            catch (ValidationException ex) { return Error(ex.Message, 404); }
        }).WithName("GetAccountById");

        finance.MapPatch("/accounts/{id:guid}", async (
            HttpContext http,
            Guid id,
            UpdateAccountCommand? body,
            AccountService accountService) =>
        {
            if (body is null) return Error("Invalid request body.", 400);
            body.ScopeId = GetScopeId(http);

            var account = await accountService.UpdateAccountAsync(id, body);
            return Results.Ok(new
            {
                accountId = account.Id,
                name = account.Name,
                type = account.Type,
                isArchived = account.IsArchived,
                createdAt = account.CreatedAt
            });
        }).WithName("UpdateAccount");
    }
}
