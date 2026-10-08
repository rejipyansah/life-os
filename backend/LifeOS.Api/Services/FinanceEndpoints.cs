using System.Security.Claims;
using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

/// <summary>
/// API Finance berdasarkan domain final:
///   ACCOUNT        — list, create, update/archive
///   TRANSACTION    — list, detail, create, reverse/correct
///   SET-ASIDE      — list, create, update, add, withdraw, spend, close, history
///   UPCOMING EVENT — list, create, update, postpone, skip, cancel, delete, realize
///   FINANCE STATE  — satu read model untuk halaman Finance
///
/// Scope selalu diselesaikan di server. Client tidak pernah menentukan ScopeId.
/// </summary>
public static class FinanceEndpoints
{
    public static void MapFinanceApi(this WebApplication app)
    {
        // ───────────────────────── Finance state ─────────────────────────

        app.MapGet("/api/finance/state", async (
            HttpContext http,
            FinanceStateService stateService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;

            try
            {
                return Results.Ok(await stateService.GetStateAsync(scope.ScopeId!.Value));
            }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
        }).WithName("GetFinanceState");

        // ───────────────────────── Accounts ─────────────────────────

        app.MapGet("/api/finance/accounts", async (
            HttpContext http,
            bool? includeArchived,
            AccountService accountService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;

            try
            {
                return Results.Ok(await accountService.GetAccountsAsync(
                    scope.ScopeId!.Value, includeArchived ?? false));
            }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
        }).WithName("GetAccounts");

        app.MapPost("/api/finance/accounts", async (
            HttpContext http,
            CreateAccountCommand? body,
            AccountService accountService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;
            if (body is null) return Error("Invalid request body.", 400);

            body.ScopeId = scope.ScopeId!.Value;

            try
            {
                var account = await accountService.CreateAccountAsync(body);
                return Results.Ok(new
                {
                    accountId = account.Id,
                    name = account.Name,
                    type = account.Type,
                    isArchived = account.IsArchived,
                    createdAt = account.CreatedAt
                });
            }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
        }).WithName("CreateAccount");

        app.MapGet("/api/finance/accounts/{id:guid}", async (
            HttpContext http,
            Guid id,
            AccountService accountService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;

            try
            {
                return Results.Ok(await accountService.GetAccountByIdAsync(id, scope.ScopeId!.Value));
            }
            catch (ValidationException ex) { return Error(ex.Message, 404); }
        }).WithName("GetAccountById");

        app.MapPatch("/api/finance/accounts/{id:guid}", async (
            HttpContext http,
            Guid id,
            UpdateAccountCommand? body,
            AccountService accountService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;
            if (body is null) return Error("Invalid request body.", 400);

            body.ScopeId = scope.ScopeId!.Value;

            try
            {
                var account = await accountService.UpdateAccountAsync(id, body);
                return Results.Ok(new
                {
                    accountId = account.Id,
                    name = account.Name,
                    type = account.Type,
                    isArchived = account.IsArchived,
                    createdAt = account.CreatedAt
                });
            }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
        }).WithName("UpdateAccount");

        // ───────────────────────── Transactions ─────────────────────────

        app.MapGet("/api/finance/transactions", async (
            HttpContext http,
            TransactionService transactionService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;

            try
            {
                var items = await transactionService.GetTransactionsAsync(scope.ScopeId!.Value);
                return Results.Ok(new { items });
            }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
        }).WithName("GetTransactions");

        app.MapGet("/api/finance/transactions/{id:guid}", async (
            HttpContext http,
            Guid id,
            TransactionService transactionService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;

            var transaction = await transactionService.GetTransactionByIdAsync(id, scope.ScopeId!.Value);
            return transaction is null ? Error("Transaction not found.", 404) : Results.Ok(transaction);
        }).WithName("GetTransactionById");

        app.MapPost("/api/finance/transactions", async (
            HttpContext http,
            CreateTransactionCommand? body,
            TransactionService transactionService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;
            if (body is null) return Error("Invalid request body.", 400);

            body.ScopeId = scope.ScopeId!.Value;

            try
            {
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
            }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
            catch (SerializationConflictException) { return Conflict(); }
        }).WithName("CreateTransaction");

        app.MapPost("/api/finance/transactions/{id:guid}/reverse", async (
            HttpContext http,
            Guid id,
            ReverseTransactionCommand? body,
            TransactionService transactionService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;

            body ??= new ReverseTransactionCommand();
            body.ScopeId = scope.ScopeId!.Value;
            body.TransactionId = id;

            try
            {
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
            }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
            catch (SerializationConflictException) { return Conflict(); }
        }).WithName("ReverseTransaction");

        // ───────────────────────── Set-asides ─────────────────────────

        app.MapGet("/api/finance/set-asides", async (
            HttpContext http,
            SetAsideService setAsideService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;

            try
            {
                return Results.Ok(new { items = await setAsideService.GetSetAsidesAsync(scope.ScopeId!.Value) });
            }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
        }).WithName("GetSetAsides");

        app.MapPost("/api/finance/set-asides", async (
            HttpContext http,
            CreateSetAsideCommand? body,
            SetAsideService setAsideService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;
            if (body is null) return Error("Invalid request body.", 400);

            body.ScopeId = scope.ScopeId!.Value;

            try
            {
                await setAsideService.CreateSetAsideAsync(body);
                var created = (await setAsideService.GetSetAsidesAsync(scope.ScopeId!.Value)).First();
                return Results.Ok(created);
            }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
            catch (SerializationConflictException) { return Conflict(); }
        }).WithName("CreateSetAside");

        app.MapGet("/api/finance/set-asides/{id:guid}", async (
            HttpContext http,
            Guid id,
            SetAsideService setAsideService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;

            var setAside = await setAsideService.GetSetAsideAsync(id, scope.ScopeId!.Value);
            return setAside is null ? Error("Set-aside not found.", 404) : Results.Ok(setAside);
        }).WithName("GetSetAsideById");

        app.MapGet("/api/finance/set-asides/{id:guid}/history", async (
            HttpContext http,
            Guid id,
            string? cursor,
            int? pageSize,
            SetAsideService setAsideService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;

            try
            {
                return Results.Ok(await setAsideService.GetHistoryAsync(id, scope.ScopeId!.Value,
                    cursor, pageSize ?? 30));
            }
            catch (ValidationException ex) { return Error(ex.Message, 404); }
        }).WithName("GetSetAsideHistory");

        app.MapPatch("/api/finance/set-asides/{id:guid}", async (
            HttpContext http,
            Guid id,
            UpdateSetAsideCommand? body,
            SetAsideService setAsideService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;
            if (body is null) return Error("Invalid request body.", 400);

            body.ScopeId = scope.ScopeId!.Value;

            try
            {
                await setAsideService.UpdateSetAsideAsync(id, body);
                return Results.Ok(await setAsideService.GetSetAsideAsync(id, scope.ScopeId!.Value));
            }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
            catch (SerializationConflictException) { return Conflict(); }
        }).WithName("UpdateSetAside");

        app.MapPost("/api/finance/set-asides/{id:guid}/add", async (
            HttpContext http,
            Guid id,
            AddToSetAsideCommand? body,
            SetAsideService setAsideService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;
            if (body is null) return Error("Invalid request body.", 400);

            body.ScopeId = scope.ScopeId!.Value;

            try { return Results.Ok(await setAsideService.AddAsync(id, body)); }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
            catch (SerializationConflictException) { return Conflict(); }
        }).WithName("AddToSetAside");

        app.MapPost("/api/finance/set-asides/{id:guid}/withdraw", async (
            HttpContext http,
            Guid id,
            WithdrawFromSetAsideCommand? body,
            SetAsideService setAsideService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;
            if (body is null) return Error("Invalid request body.", 400);

            body.ScopeId = scope.ScopeId!.Value;

            try { return Results.Ok(await setAsideService.WithdrawAsync(id, body)); }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
            catch (SerializationConflictException) { return Conflict(); }
        }).WithName("WithdrawFromSetAside");

        app.MapPost("/api/finance/set-asides/{id:guid}/spend", async (
            HttpContext http,
            Guid id,
            SpendFromSetAsideCommand? body,
            SetAsideService setAsideService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;
            if (body is null) return Error("Invalid request body.", 400);

            body.ScopeId = scope.ScopeId!.Value;

            try { return Results.Ok(await setAsideService.SpendAsync(id, body)); }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
            catch (SerializationConflictException) { return Conflict(); }
        }).WithName("SpendFromSetAside");

        app.MapPost("/api/finance/set-asides/{id:guid}/close", async (
            HttpContext http,
            Guid id,
            CloseSetAsideCommand? body,
            SetAsideService setAsideService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;

            body ??= new CloseSetAsideCommand();
            body.ScopeId = scope.ScopeId!.Value;

            try { return Results.Ok(await setAsideService.CloseAsync(id, body)); }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
            catch (SerializationConflictException) { return Conflict(); }
        }).WithName("CloseSetAside");

        // ───────────────────────── Upcoming events ─────────────────────────

        app.MapGet("/api/finance/upcoming-events", async (
            HttpContext http,
            UpcomingEventService eventService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;

            return Results.Ok(new { items = await eventService.GetAsync(scope.ScopeId!.Value) });
        }).WithName("GetUpcomingEvents");

        app.MapPost("/api/finance/upcoming-events", async (
            HttpContext http,
            CreateUpcomingEventCommand? body,
            UpcomingEventService eventService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;
            if (body is null) return Error("Invalid request body.", 400);

            body.ScopeId = scope.ScopeId!.Value;

            try
            {
                var created = await eventService.CreateAsync(body);
                return Results.Ok(await eventService.GetByIdAsync(created.Id, scope.ScopeId!.Value));
            }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
            catch (SerializationConflictException) { return Conflict(); }
        }).WithName("CreateUpcomingEvent");

        app.MapGet("/api/finance/upcoming-events/{id:guid}", async (
            HttpContext http,
            Guid id,
            UpcomingEventService eventService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;

            var agenda = await eventService.GetByIdAsync(id, scope.ScopeId!.Value);
            return agenda is null ? Error("Agenda not found.", 404) : Results.Ok(agenda);
        }).WithName("GetUpcomingEventById");

        app.MapPatch("/api/finance/upcoming-events/{id:guid}", async (
            HttpContext http,
            Guid id,
            UpdateUpcomingEventCommand? body,
            UpcomingEventService eventService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;
            if (body is null) return Error("Invalid request body.", 400);

            body.ScopeId = scope.ScopeId!.Value;

            try
            {
                await eventService.UpdateAsync(id, body);
                return Results.Ok(await eventService.GetByIdAsync(id, scope.ScopeId!.Value));
            }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
            catch (SerializationConflictException) { return Conflict(); }
        }).WithName("UpdateUpcomingEvent");

        app.MapPost("/api/finance/upcoming-events/{id:guid}/postpone", async (
            HttpContext http,
            Guid id,
            PostponeUpcomingEventCommand? body,
            UpcomingEventService eventService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;

            body ??= new PostponeUpcomingEventCommand();
            body.ScopeId = scope.ScopeId!.Value;

            try
            {
                await eventService.PostponeAsync(id, body);
                return Results.Ok(await eventService.GetByIdAsync(id, scope.ScopeId!.Value));
            }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
            catch (SerializationConflictException) { return Conflict(); }
        }).WithName("PostponeUpcomingEvent");

        app.MapPost("/api/finance/upcoming-events/{id:guid}/skip", async (
            HttpContext http,
            Guid id,
            SettleUpcomingEventCommand? body,
            UpcomingEventService eventService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;

            body ??= new SettleUpcomingEventCommand();
            body.ScopeId = scope.ScopeId!.Value;

            try
            {
                await eventService.SkipAsync(id, body);
                return Results.Ok(await eventService.GetByIdAsync(id, scope.ScopeId!.Value));
            }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
            catch (SerializationConflictException) { return Conflict(); }
        }).WithName("SkipUpcomingEvent");

        app.MapPost("/api/finance/upcoming-events/{id:guid}/cancel", async (
            HttpContext http,
            Guid id,
            SettleUpcomingEventCommand? body,
            UpcomingEventService eventService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;

            body ??= new SettleUpcomingEventCommand();
            body.ScopeId = scope.ScopeId!.Value;

            try
            {
                await eventService.CancelAsync(id, body);
                return Results.Ok(await eventService.GetByIdAsync(id, scope.ScopeId!.Value));
            }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
            catch (SerializationConflictException) { return Conflict(); }
        }).WithName("CancelUpcomingEvent");

        app.MapPost("/api/finance/upcoming-events/{id:guid}/realize", async (
            HttpContext http,
            Guid id,
            RealizeUpcomingEventCommand? body,
            UpcomingEventService eventService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;

            body ??= new RealizeUpcomingEventCommand();
            body.ScopeId = scope.ScopeId!.Value;

            try { return Results.Ok(await eventService.RealizeAsync(id, body)); }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
            catch (SerializationConflictException) { return Conflict(); }
        }).WithName("RealizeUpcomingEvent");

        app.MapDelete("/api/finance/upcoming-events/{id:guid}", async (
            HttpContext http,
            Guid id,
            UpcomingEventService eventService,
            GuestTokenService guestTokenService,
            ApplicationDbContext db) =>
        {
            var scope = await ResolveScopeAsync(http, guestTokenService, db);
            if (scope.Error is not null) return scope.Error;

            try
            {
                await eventService.DeleteAsync(id, scope.ScopeId!.Value);
                return Results.NoContent();
            }
            catch (ValidationException ex) { return Error(ex.Message, 422); }
            catch (SerializationConflictException) { return Conflict(); }
        }).WithName("DeleteUpcomingEvent");
    }

    // ───────────────────────── Helpers ─────────────────────────

    /// <summary>
    /// Scope diselesaikan di server dari cookie auth/guest.
    /// Client tidak pernah menjadi otoritas atas ScopeId.
    /// </summary>
    private static async Task<(Guid? ScopeId, IResult? Error)> ResolveScopeAsync(
        HttpContext http,
        GuestTokenService guestTokenService,
        ApplicationDbContext db)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!string.IsNullOrEmpty(userId))
        {
            var scope = await db.Scopes.FirstOrDefaultAsync(
                s => s.Type == ScopeType.Owner && s.OwnerUserId == userId);

            return scope is null
                ? (null, Error("Owner Scope not found.", 400))
                : (scope.Id, null);
        }

        var session = await guestTokenService.ResolveAsync(http);
        return session is null
            ? (null, Error("Guest session not found.", 401))
            : (session.ScopeId, null);
    }

    private static IResult Error(string message, int statusCode)
        => Results.Json(new { error = message }, statusCode: statusCode);

    private static IResult Conflict()
        => Results.Json(
            new { error = "The request conflicted with a concurrent operation. Please try again." },
            statusCode: 409);
}
