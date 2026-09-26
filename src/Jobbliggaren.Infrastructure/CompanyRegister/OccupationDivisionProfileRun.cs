namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #1682 — the single state row beside <see cref="OccupationDivisionProfileRow"/>: when the profile
/// was last built and what the build counted. Without it "no rows for this group" is one symbol for
/// three facts — never built, built and under the floor, built and genuinely zero — the exact
/// collapse <c>CompanyWatchCriterionMaterialisation</c> exists to prevent, and the dishonest nought
/// this repo has shipped once already (#1656). The read side gates on <see cref="ProfiledAt"/>
/// before it touches the profile table at all.
///
/// <para>
/// A <c>text</c> primary key on a single-row table makes "one row" a key rather than a convention:
/// EF-mappable without a CHECK, and greppable. The counters are audited here on purpose — a bucket
/// whose firings are not stored cannot be told from a bucket that never filled.
/// </para>
/// </summary>
internal sealed class OccupationDivisionProfileRun
{
    /// <summary>The one key the one row carries.</summary>
    public const string CurrentKey = "current";

    public required string ProfileKey { get; init; }

    /// <summary>Stamped where the corpus is read, so an earlier stamp only ever ages the run out sooner.</summary>
    public required DateTimeOffset ProfiledAt { get; init; }

    public required int OccupationGroupsProfiled { get; init; }

    public required int RowsWritten { get; init; }

    /// <summary>Every ad in the window, all buckets included — the denominator of every share.</summary>
    public required int AdsCounted { get; init; }

    public required int AdsNotInRegister { get; init; }

    public required int AdsInRegisterWithoutSni { get; init; }
}
