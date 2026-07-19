using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.SavedSearches.Commands.MarkResultsSeen;

/// <summary>
/// #312 (ADR 0115) — advances a specific saved search's per-search USER-read watermark
/// (<c>SavedSearch.ResultsSeenAt</c>): that search's "N nya träffar"-count resets. Called when the
/// user views the saved search's results (in-app-only v1, GDPR Art. 6(1)(b) — the saved search + its
/// notification toggle IS the requested service; no consent flag). Owner-scoped. Non-generic
/// <see cref="Result"/> (mutates the caller's own SavedSearch; creates no id). Idempotent — the
/// watermark is monotonic (the aggregate guards it).
/// </summary>
/// <param name="Id">The saved search whose results the user viewed (route id).</param>
/// <param name="SeenThrough">
/// The max <c>JobAd.CreatedAt</c> of the results the user actually saw (#477/#759 — the watermark is
/// set to this, NOT clock-now, so an ad ingested between the fetch and this call is not silently
/// swallowed). Null (no body / empty result set / older FE) falls back to clock-now in the handler;
/// the aggregate clamps a future value to now.
/// </param>
public sealed record MarkSavedSearchResultsSeenCommand(Guid Id, DateTimeOffset? SeenThrough)
    : ICommand<Result>;
