using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Admin.Accounts;
using Jobbliggaren.Application.Admin.Accounts.Commands.CancelAccountEmailChange;
using Jobbliggaren.Application.Admin.Accounts.Commands.RequestAccountEmailChange;
using Jobbliggaren.Application.Admin.Accounts.Queries.CountAccountsByStatus;
using Jobbliggaren.Application.Admin.Accounts.Queries.GetAccountDetails;
using Jobbliggaren.Application.Admin.Accounts.Queries.GetPendingAccountEmailChange;
using Jobbliggaren.Application.Admin.Accounts.Queries.SearchAccounts;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// The admin account directory (#1974, ADR 0151): a search over every account, with its status counts in
/// the same response, and one account's details. The search term travels only in the POST body. Every
/// response is private and never stored, and the header is set before the query runs, so a failure carries
/// it too.
/// </summary>
public static class AdminAccountsEndpoints
{
    /// <summary>A blank address means no address filter; a missing status means every status.</summary>
    public sealed record AccountSearchRequest(
        string? Address = null,
        AccountStatus? Status = null,
        AccountSort Sort = AccountSort.RegisteredNewest,
        int Page = 1,
        int PageSize = 25)
    {
        /// <summary>The address names a person, so a record's generated text never prints it.</summary>
        public override string ToString() =>
            $"AccountSearchRequest(address {(string.IsNullOrWhiteSpace(Address) ? "none" : "redacted")}, "
            + $"status {Status?.ToString() ?? "any"}, {Sort}, page {Page}/{PageSize})";
    }

    public sealed record AccountSearchResponse(PagedResult<AccountListItemDto> Accounts, AccountStatusCountsDto Counts);

    /// <summary>
    /// #1975 — the new address and the administrator's own re-authentication grant. The address names a person and the
    /// grant is a credential, so a record's generated text prints neither.
    /// </summary>
    public sealed record AccountEmailChangeRequest(string? NewEmail = null, string? ReauthGrant = null)
    {
        public override string ToString() => "AccountEmailChangeRequest(address and grant redacted)";
    }

    public static void MapAdminAccountsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/accounts")
            .WithTags("Admin")
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .RequireRateLimiting(RateLimitingExtensions.AdminReadPolicy);

        // Two sends, one response and one token: the counts follow the term but not the status filter.
        group.MapPost("/search", async (
            AccountSearchRequest body, IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var accounts = await mediator.Send(
                new SearchAccountsQuery(body.Address, body.Status, body.Sort, body.Page, body.PageSize), ct);
            var counts = await mediator.Send(new CountAccountsByStatusQuery(body.Address), ct);
            return Results.Ok(new AccountSearchResponse(accounts, counts));
        });

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var account = await mediator.Send(new GetAccountDetailsQuery(id), ct);
            return account is null ? Results.NotFound() : Results.Ok(account);
        });

        // #1975 (ADR 0153) — an address change an administrator starts for an account, its cancel and its pending
        // read. A group of its own, because the directory's group puts AdminRead on every route and the two writes
        // take AdminWrite.
        var account = app.MapGroup("/api/v1/admin/accounts/{id:guid}")
            .WithTags("Admin")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        account.MapPost("/email-change", async (
            Guid id, AccountEmailChangeRequest body, IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(new RequestAccountEmailChangeCommand(id, body.NewEmail, body.ReauthGrant), ct);
            return result.IsFailure
                ? ErrorResult(result.Error)
                : Results.Accepted(uri: (string?)null, value: result.Value);
        }).RequireRateLimiting(RateLimitingExtensions.AdminWritePolicy);

        account.MapDelete("/email-change", async (Guid id, IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(new CancelAccountEmailChangeCommand(id), ct);
            return result.IsFailure ? ErrorResult(result.Error) : Results.NoContent();
        }).RequireRateLimiting(RateLimitingExtensions.AdminWritePolicy);

        // Nothing pending is no content, never a 404: the account may well exist.
        account.MapGet("/email-change", async (Guid id, IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var pending = await mediator.Send(new GetPendingAccountEmailChangeQuery(id), ct);
            return pending is null ? Results.NoContent() : Results.Ok(pending);
        }).RequireRateLimiting(RateLimitingExtensions.AdminReadPolicy);
    }

    private static IResult ErrorResult(DomainError error) =>
        error.Code == AuthErrorCodes.EmailDeliveryUnavailable
            ? AuthProblem.EmailDeliveryUnavailable()
            : error.ToProblemResult();
}
