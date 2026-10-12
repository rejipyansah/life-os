using LifeOS.Api.Models;

namespace LifeOS.Api.Services;

public static partial class FinanceEndpoints
{
    private static void MapTransactionEndpoints(RouteGroupBuilder finance)
    {
        finance.MapGet("/transactions", async (
            HttpContext http,
            TransactionService transactionService) =>
        {
            var items = await transactionService.GetTransactionsAsync(GetScopeId(http));
            return Results.Ok(new { items });
        }).WithName("GetTransactions");

        finance.MapGet("/transactions/{id:guid}", async (
            HttpContext http,
            Guid id,
            TransactionService transactionService) =>
        {
            var transaction = await transactionService.GetTransactionByIdAsync(id, GetScopeId(http));
            return transaction is null ? Error("Transaction not found.", 404) : Results.Ok(transaction);
        }).WithName("GetTransactionById");

        finance.MapPost("/transactions", async (
            HttpContext http,
            CreateTransactionCommand? body,
            TransactionService transactionService) =>
        {
            if (body is null) return Error("Invalid request body.", 400);
            body.ScopeId = GetScopeId(http);

            var (transaction, entries) = await transactionService.CreateTransactionAsync(body);
            return Results.Ok(new
            {
                transactionId = transaction.Id,
                type = transaction.Type,
                amount = transaction.Amount,
                occurredOn = transaction.OccurredOn,
                createdAt = transaction.CreatedAt,
                entryCount = entries.Count
            });
        }).WithName("CreateTransaction");

        finance.MapPost("/transactions/{id:guid}/reverse", async (
            HttpContext http,
            Guid id,
            ReverseTransactionCommand? body,
            TransactionService transactionService) =>
        {
            body ??= new ReverseTransactionCommand();
            body.ScopeId = GetScopeId(http);
            body.TransactionId = id;

            var reversal = await transactionService.ReverseTransactionAsync(body);
            return Results.Ok(new
            {
                transactionId = reversal.Id,
                type = reversal.Type,
                amount = reversal.Amount,
                relatedTransactionId = reversal.RelatedTransactionId,
                occurredOn = reversal.OccurredOn,
                createdAt = reversal.CreatedAt
            });
        }).WithName("ReverseTransaction");
    }
}
