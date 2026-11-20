using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobSeekers;

/// <summary>
/// ADR 0188 (#2265) — a job seeker has at most <see cref="MaxTotal"/> quick links in the /jobb hero, the junior link
/// included (Klas, 2026-11-20: "Ja, 3 totalt"). The set spans two aggregates, the junior stamp on
/// <see cref="Preferences"/> and the marked <c>RecentJobSearch</c> rows, so the rule is a stateless domain service that
/// each "add" handler asks before it writes. The junior link counts while it is shown, also when its count is 0 and the
/// hero hides it, so a link added at 0 cannot push the set past the cap when the count returns.
/// <para>
/// A handler asks only when the add would change something: a repeated add is a no-op that succeeds. The store bounds
/// the marked searches on its own: each takes a slot from 1 to <see cref="MaxTotal"/>, and a unique index per job
/// seeker admits each slot once, so no race marks a fourth search. "Three in all" across the junior stamp and the slots
/// is checked per request, so a junior toggle racing a mark can show four links; no constraint can span a jsonb key
/// and table rows, and the reads cope with four.
/// </para>
/// </summary>
public static class QuickLinkCap
{
    public const int MaxTotal = 3;

    /// <summary>The lowest slot from 1 to <see cref="MaxTotal"/> that no marked search holds, or <c>null</c>.</summary>
    public static int? FreeSlot(IReadOnlyCollection<int> usedSlots)
    {
        ArgumentNullException.ThrowIfNull(usedSlots);
        for (var slot = 1; slot <= MaxTotal; slot++)
        {
            if (!usedSlots.Contains(slot))
                return slot;
        }

        return null;
    }

    public static Result EnsureRoomForOne(bool juniorLinkOn, int markedSearches)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(markedSearches);

        var current = markedSearches + (juniorLinkOn ? 1 : 0);
        return current < MaxTotal
            ? Result.Success()
            : Result.Failure(DomainError.Conflict(
                "QuickLink.MaxTotal",
                $"Du kan ha högst {MaxTotal} snabblänkar. Ta bort en snabblänk för att lägga till en ny."));
    }
}
