using System.Diagnostics;
using Jobbliggaren.Application.Dev.Commands.SeedAccount;
using Jobbliggaren.Application.Dev.Commands.TakeLoginCode;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// DEV-ONLY endpoints — remove before launch (Klas), together with everything they gate
/// (<c>docs/runbooks/release-checklist.md</c>). These exist solely so onboarding flows can be
/// re-tested.
///
/// <para><b>The routes are ENVIRONMENT-gated and nothing widens them.</b> Unauthenticated, the
/// <c>accounts</c> and <c>login-code</c> seams open an account and hand out a login code for a reserved
/// address, so they can never be reachable outside Development.</para>
/// </summary>
public static class DevEndpoints
{
    private const string GroupPrefix = "/api/v1/dev";

    /// <summary>
    /// DEV-ONLY, ENVIRONMENT-gated and NOT configurable. The caller (<c>Program.cs</c>) MUST guard
    /// this with <c>app.Environment.IsDevelopment()</c> and nothing else — no flag may widen it.
    /// <c>ProductionStartupSmokeTests</c> measures that route table, because "no flag widens it" is exactly
    /// the kind of claim that stays true only while someone is measuring it.
    /// </summary>
    public static void MapDevEnvironmentOnlyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(GroupPrefix).WithTags("Dev");

        // DEV-ONLY — the login code the last challenge mail to a RESERVED address carried (#1735, ADR 0142
        // D10), taken once, so a flow can sign in without a mailbox. The code alone signs nothing in: it is
        // verified against the challenge id its requester holds. Two gates: this map, and IDevLoginCodeReader
        // registered only in Development. REMOVE BEFORE LAUNCH.
        group.MapPost("/login-code", async (
            DevLoginCodeRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body?.Email))
                return Results.BadRequest();

            var code = await mediator.Send(new DevTakeLoginCodeCommand(body.Email), ct);
            return code is null ? Results.NotFound() : Results.Ok(new { code });
        });

        // DEV-ONLY — a login-capable account for a RESERVED address, opened by the writer `complete` uses (ADR
        // 0142 part 5a), so the E2E suite seeds without spending the cap on mails to addresses without an
        // account. 204 with no body: no session, no code, no user id. The same two gates as login-code: this
        // map, and IDevSeedableAddressPolicy registered only in Development. REMOVE BEFORE LAUNCH.
        group.MapPost("/accounts", async (
            DevSeedAccountRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body?.Email))
                return Results.BadRequest();

            return await mediator.Send(new DevSeedAccountCommand(body.Email), ct) switch
            {
                DevSeedAccountOutcome.Ready => Results.NoContent(),
                DevSeedAccountOutcome.NotReserved => Results.NotFound(),
                DevSeedAccountOutcome.Unavailable => Results.Conflict(),
                _ => throw new UnreachableException("A DevSeedAccountOutcome has no response."),
            };
        });
    }

    /// <summary>DEV-ONLY request body for <c>POST /api/v1/dev/login-code</c> (#1735).</summary>
    public sealed record DevLoginCodeRequest(string? Email);

    /// <summary>DEV-ONLY request body for <c>POST /api/v1/dev/accounts</c> (ADR 0142 part 5a).</summary>
    public sealed record DevSeedAccountRequest(string? Email);
}
