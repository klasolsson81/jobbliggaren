using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Matching.Abstractions;

/// <summary>
/// Why a membership dimension landed where it did, when the verdict plus the cited
/// evidence do not say it on their own (senior-cto-advisor bind 2026-08-31).
/// <para>
/// The three membership dimensions — SSYK, ort and employment type — have arms that
/// return EMPTY evidence, and two of them fold two different reasons into one verdict.
/// <c>ScoreSsykMembership</c>'s single <c>NotAssessed</c> guard is a disjunction ("the user
/// stated no occupation group" OR "the ad has none"), and <c>ScoreOrtUnion</c> returns
/// <c>NotAssessed</c> from two arms and empty-evidence <c>Match</c>/<c>NoMatch</c> from two
/// more. The client cannot recover the reason from <c>(verdict, emptiness, dimension)</c>:
/// the mapping is not injective, and where it happens to be, deriving it is the
/// anti-pattern #1598 was opened to retire. So the reason rides the wire as a bounded code
/// and the catalogue owns its word.
/// </para>
/// <para>
/// <b>A bounded discriminator, not a message</b> — the same shape as
/// <c>DomainError.Kind</c> (AGENTS.md §3): per-case members and ONE exhaustive switch at
/// the consumer, never a string the reader matches on. No Swedish prose crosses the port
/// (that was #1540's defect), and no concept id crosses it either (ADR 0043's ACL).
/// </para>
/// <para>
/// <b>Nullable at every carrier, with no <c>None</c> member</b>: absence means "the evidence
/// explains itself" — an ordinary hit or miss with concepts to cite. That is the doctrine
/// <see cref="Queries.GetJobAdMatchDetail.MatchRegisterConceptDto.Label"/> and
/// <c>TaxonomyLabelDto.Label</c> already carry; a <c>None</c> member would be a member
/// meaning "ignore me".
/// </para>
/// Serialized by NAME (<c>JsonStringEnumConverter</c>), parity
/// <see cref="MatchDimensionVerdict"/> and <see cref="Grading.MatchGrade"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MatchDimensionCause
{
    /// <summary>
    /// The USER constrained nothing on this dimension, so there was nothing to assess
    /// against — the vacuous-gate doctrine. Produced by all three membership scorers.
    /// </summary>
    PreferenceUnstated,

    /// <summary>
    /// The AD carries no value on this dimension. For ort and employment type this is the
    /// #552 grade gate (a stated preference against a silent ad is a contradiction —
    /// <c>NoMatch</c> with nothing to cite); for SSYK it is <c>NotAssessed</c>, since the
    /// SSYK gate rather than the RB1 floor owns that case.
    /// </summary>
    AdSilent,

    /// <summary>
    /// The ad offers remote work, which overrides the ort gate for everyone with a stated
    /// ort preference regardless of which ort they stated (#551, ADR 0076 amendment). The
    /// verdict is <c>Match</c> with empty evidence: there is no region concept id to cite,
    /// and a "distans" sentinel in Matched would be a magic string the taxonomy resolver
    /// would then have to special-case (§5).
    /// </summary>
    RemoteOverride,

    /// <summary>
    /// The ad names only a län, and that län contains a kommun the user asked for (#477
    /// Low 1). Neither a confirmed hit nor a contradiction, so the verdict is
    /// <c>NotAssessed</c> and the grade is neither floored nor lifted.
    /// </summary>
    RegionContainsPreferredMunicipality,
}

/// <summary>
/// The cause of each membership dimension's outcome for one scored ad, carried BESIDE the
/// score on <see cref="FullScoredMatch"/> — never inside
/// <see cref="MatchDimension"/>.
/// <para>
/// <b>Why not a field on <see cref="MatchDimension"/>:</b> seven dimensions share that type
/// and four of them can never bear a cause (title compares stemmed lexemes; the three CV
/// coverage dimensions always cite Display labels or are <c>Vacuous</c>). Giving them all a
/// property that is meaningless for four is the interface-segregation complaint, and it
/// would put a second change-reason on a type the grade ladder reads. Carrying a
/// scorer-computed signal beside the score is the shape this carrier already exists for
/// (<see cref="FullScoredMatch.SsykIsRelated"/>, CTO carrier-bind 2026-06-28).
/// </para>
/// <para>
/// <b>Why not three flat fields on the carrier:</b> the three names here match the three
/// <see cref="Queries.GetJobAdMatchDetail.JobAdMatchDetailDto"/> row properties exactly, so
/// the handler's mapping is positional-free and an eighth membership dimension is one
/// compile error in one place.
/// </para>
/// </summary>
/// <param name="SsykOverlap">Cause for the occupation-group dimension, or <c>null</c> when its
/// evidence explains itself.</param>
/// <param name="RegionFit">Cause for the ort dimension, or <c>null</c>.</param>
/// <param name="EmploymentFit">Cause for the employment-type dimension, or <c>null</c>.</param>
public sealed record MatchDimensionCauses(
    MatchDimensionCause? SsykOverlap,
    MatchDimensionCause? RegionFit,
    MatchDimensionCause? EmploymentFit)
{
    /// <summary>
    /// No dimension carries a cause — what the scorer returns when every membership dimension
    /// cites ordinary evidence. An INSTANCE, not a <see cref="MatchDimensionCause"/> member: the
    /// enum deliberately has no <c>None</c>, because a member meaning "ignore me" is not a
    /// reason. This one names a real state and keeps an arity change to one edit.
    /// </summary>
    public static readonly MatchDimensionCauses None = new(null, null, null);
}
