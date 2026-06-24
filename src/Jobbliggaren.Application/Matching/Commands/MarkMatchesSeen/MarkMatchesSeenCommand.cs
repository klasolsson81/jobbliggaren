using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Matching.Commands.MarkMatchesSeen;

/// <summary>
/// ADR 0080 Vag 4 PR-5 (Beslut 6) — advances the authenticated user's last-seen-matches
/// watermark to now (the "Nya matchningar" count resets). Called when the user OPENS the
/// matches view (Klas product decision 2026-06-24 — "views the matches surface, not every page
/// load"). Parameterless — owner-scoped. Returns a non-generic <see cref="Result"/> (it mutates
/// the caller's existing JobSeeker; creates no id). Idempotent (the watermark is monotonic).
/// </summary>
public sealed record MarkMatchesSeenCommand : ICommand<Result>;
