using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Commands.SetLastSeenFollowedAds;

/// <summary>
/// Bevakning F2 (#801, RF-6=6B) — advances the authenticated user's company-follow last-seen
/// watermark (the Översikt "nya annonser från bevakade företag"-count resets). Called when the user
/// visits the follows surface (/foretag) — the sibling of <c>MarkMatchesSeenCommand</c> for the
/// match rail (Klas surface decision 2026-07-12: advance on visiting the follows hub, not on every
/// /oversikt load). Owner-scoped. Returns a non-generic <see cref="Result"/> (it mutates the
/// caller's existing JobSeeker; creates no id). Idempotent (the watermark is monotonic).
///
/// <para>
/// <b>Deliberately NOT <c>MarkFollowedCompanyAdSeenCommand</c></b> (#453) — that is a per-HIT
/// <c>SeenAt</c>-stamp for cross-channel EMAIL suppression ("aldrig mejla något jag sett i appen").
/// This is the coarse per-USER read watermark for the IN-APP rail count. Two orthogonal concerns
/// (RF-6=6B: the watermark drives the count; per-hit <c>SeenAt</c> remains the email-suppression
/// authority) — kept as distinct commands so the one-char plural difference can never silently
/// conflate them.
/// </para>
/// </summary>
/// <param name="SeenThrough">
/// The seen window the user acknowledged. Null (no body / deploy-skew from an older FE / the
/// follows hub renders no individual hits to preserve) falls back to clock-now in the handler — the
/// documented safe path when there is nothing newer to preserve (parity
/// <c>MarkMatchesSeenCommand.SeenThrough</c>). A future-dated value is clamped to now by the
/// aggregate.
/// </param>
public sealed record SetLastSeenFollowedAdsCommand(DateTimeOffset? SeenThrough) : ICommand<Result>;
