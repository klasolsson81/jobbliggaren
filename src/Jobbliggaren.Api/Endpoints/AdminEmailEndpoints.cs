using System.Globalization;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.Admin.Email.Dmarc.Queries.GetDmarcReports;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailAllowance;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailDelivery;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailRecentFailures;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailSendCounts;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// The e-post page's reads (#1981): this month's mail against the free allowance and the computed cost beyond it
/// (ADR 0178), the delivery totals and recent failures for one period (PR 2), and our own counts per mail kind for one
/// period (PR 3, ADR 0181), and the stored DMARC aggregate reports over a fixed seven-day window (PR 4a, ADR 0184). The
/// page and the overview read these endpoints, so they share one definition. Every answer, refusals included, is private
/// and uncacheable.
/// </summary>
public static class AdminEmailEndpoints
{
    public const string AllowancePath = "/email/allowance";
    public const string DeliveryPath = "/email/delivery";
    public const string FailuresPath = "/email/failures";
    public const string KindsPath = "/email/kinds";
    public const string DmarcPath = "/email/dmarc";

    /// <summary>
    /// The query-string name of the period. Read as a string and mapped by <see cref="EmailDeliveryPeriods.FromToken"/>,
    /// never bound as the enum, which would admit a number or a member name; an unknown token is a 400 from the validator.
    /// </summary>
    public const string PeriodParameter = "period";

    public static void MapAdminEmailEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin")
            .WithTags("Admin")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapGet(AllowancePath, async (
            IMediator mediator, IDateTimeProvider clock, HttpResponse response, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetEmailAllowanceQuery(), ct);
            MarkPrivateAndSampled(response, clock);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.AdminReadPolicy);

        group.MapGet(DeliveryPath, async (
            [FromQuery(Name = PeriodParameter)] string? period,
            IMediator mediator, IDateTimeProvider clock, HttpResponse response, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetEmailDeliveryQuery(EmailDeliveryPeriods.FromToken(period)), ct);
            MarkPrivateAndSampled(response, clock);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.AdminReadPolicy);

        group.MapGet(FailuresPath, async (
            [FromQuery(Name = PeriodParameter)] string? period,
            IMediator mediator, IDateTimeProvider clock, HttpResponse response, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetEmailRecentFailuresQuery(EmailDeliveryPeriods.FromToken(period)), ct);
            MarkPrivateAndSampled(response, clock);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.AdminReadPolicy);

        group.MapGet(KindsPath, async (
            [FromQuery(Name = PeriodParameter)] string? period,
            IMediator mediator, IDateTimeProvider clock, HttpResponse response, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetEmailSendCountsQuery(EmailDeliveryPeriods.FromToken(period)), ct);
            MarkPrivateAndSampled(response, clock);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.AdminReadPolicy);

        // No period: the reports cover whole UTC days and arrive a day late, so the window is fixed (ADR 0184 D9).
        group.MapGet(DmarcPath, async (
            IMediator mediator, IDateTimeProvider clock, HttpResponse response, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetDmarcReportsQuery(), ct);
            MarkPrivateAndSampled(response, clock);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.AdminReadPolicy);
    }

    /// <summary>
    /// The API read instant, like every overview source; when the provider answered is the body's own observedAt.
    /// Cache-Control is also set on every answer under this path, a 400 included, by the path middleware in Program.
    /// </summary>
    private static void MarkPrivateAndSampled(HttpResponse response, IDateTimeProvider clock)
    {
        response.Headers.CacheControl = "private, no-store";
        response.Headers[AdminEndpoints.SampledAtHeader] =
            clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
