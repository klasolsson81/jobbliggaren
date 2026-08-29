namespace Jobbliggaren.Application.Matching.Grading;

/// <summary>
/// The named bands of <see cref="MatchGrade"/> that production filters on. One home for a piece of
/// knowledge that had four: the ">= Good" threshold was hand-enumerated in
/// <c>ListCompanyWatchesQueryHandler</c>, <c>LookupCompanyQueryHandler</c> and
/// <c>PerUserJobAdSearchQuery</c>, the filterable band in <c>ListJobAdsQueryHandler</c> and again in
/// <c>ListJobAdsQueryValidator</c>, and the five were held in step by a prose comment. The mutation
/// that costs is not hypothetical: ADR 0084 §F2 inserted <see cref="MatchGrade.Related"/> BETWEEN
/// Basic and Good and forced a renumbering; the next insertion would have taken every copy with it.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>NotifiableMatchGrade</c> is deliberately absent, and it is not an oversight.</b> That
/// band ({Good, Strong, Top}) is a Domain enum with its own writer
/// (<c>BackgroundMatchingJob.ToNotifiable</c>), and it is NOT a subset of <see cref="Filterable"/> —
/// it contains <c>Top</c>, which <see cref="Filterable"/> excludes by definition. Folding it in here
/// would be a false generalisation over two bands that only look alike.
/// </para>
/// <para>
/// <b>What nothing here pins:</b> that <see cref="Filterable"/> is exactly the set
/// <c>GradeRankExpression</c> can emit a positive rank for. That identity needs Postgres to observe,
/// so it is held behaviourally by <c>MatchSortGradeFilterOracleTests</c> and <c>MatchCountOracleTests</c>
/// against Testcontainers — not by a static assertion here, and deliberately not by a weaker one.
/// </para>
/// </remarks>
public static class MatchGradeBands
{
    /// <summary>
    /// The Fast band: the grades the list filter can compute AND the wire accepts. Hand-written, and
    /// it stays hand-written.
    /// <para>
    /// It is NOT "the enum minus <see cref="MatchGrade.Top"/>" — those two coincide today by
    /// accident. <c>Top</c> is excluded because the list path cannot compute must-have coverage in
    /// SQL (ADR 0076 §4 G3-OPT-A), so a Top filter would silently match zero: an honesty gate, not a
    /// set complement. Deriving this from <see cref="System.Enum.GetValues{TEnum}()"/> would make
    /// "filterable" the DEFAULT for a sixth grade — fail-OPEN on precisely the property that gate
    /// bought. Whether a new grade is Fast-computable is a human's call, and adding it here IS that
    /// call.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<MatchGrade> Filterable =
        [MatchGrade.Basic, MatchGrade.Related, MatchGrade.Good, MatchGrade.Strong];

    /// <summary>
    /// "Matchande annonser" — the >= Good threshold the followed-company and company-lookup counts
    /// are computed at (#452, ADR 0087 D5-tillägg), derived from <see cref="Filterable"/> rather
    /// than transcribed. <c>Top</c> stays out without being named: it is not in the Fast band, and
    /// skills only elevate WITHIN the notifiable band — they never lift a Basic across the Good
    /// threshold, so the Fast-band >= Good set equals the Full-band one (Fast==Full oracle).
    /// <para>
    /// ⚠ <b>The derivation turns the enum's DECLARATION ORDER into a semantic claim</b> — that the
    /// rungs are ordinal. They are today, on purpose (<see cref="MatchGrade.Related"/> is placed
    /// between Basic and Good). But that alignment is what <c>MatchGradeBandPinTests</c> exists to
    /// hold: insert a sixth grade between Good and Strong and this set grows silently, moving a
    /// user-visible count on /foretag without a single test failing. The pin is what makes the
    /// derivation safe, not decoration on top of it.
    /// </para>
    /// </summary>
    // ⚠ Declaration order is load-bearing, and the compiler will not tell you: a static field
    // initialiser reading `Filterable` must be declared AFTER it, or `Filterable` is still null here
    // and the type throws TypeInitializationException on first use — at runtime, not at build.
    public static readonly IReadOnlyList<MatchGrade> GoodOrBetter =
        [.. Filterable.Where(g => g >= MatchGrade.Good)];
}
