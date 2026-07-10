using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Matching.Commands.MarkMatchesSeen;

/// <summary>
/// ADR 0080 Vag 4 PR-5 (Beslut 6) — advances the authenticated user's last-seen-matches
/// watermark (the "Nya matchningar" count resets). Called when the user OPENS the matches view
/// (Klas product decision 2026-06-24 — "views the matches surface, not every page load").
/// Owner-scoped. Returns a non-generic <see cref="Result"/> (it mutates the caller's existing
/// JobSeeker; creates no id). Idempotent (the watermark is monotonic).
/// </summary>
/// <param name="SeenThrough">
/// The max <c>CreatedAt</c> of the matches the user actually viewed (#477 Low — the watermark is
/// set to this, NOT clock-now, so a match created between the fetch and this call is not silently
/// swallowed). Null (no body / an empty match list / deploy-skew from an older FE) falls back to
/// clock-now in the handler — the old behaviour, safe when there is nothing newer to preserve.
/// </param>
public sealed record MarkMatchesSeenCommand(DateTimeOffset? SeenThrough) : ICommand<Result>;
