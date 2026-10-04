using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Admin.Accounts;
using Jobbliggaren.Application.Admin.Accounts.Queries.CountAccountsByStatus;
using Jobbliggaren.Application.Admin.Accounts.Queries.GetAccountDetails;
using Jobbliggaren.Application.Admin.Accounts.Queries.SearchAccounts;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Authorization;
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
    }
}
