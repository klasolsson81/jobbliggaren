namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// #2143 (ADR 0167) — translates a minus-word into the taxonomy concepts whose Swedish label it
/// names exactly, so <c>-deltid</c> also hides an ad whose omfattning is Deltid when its text never
/// says so. A narrow port beside <see cref="ITaxonomyReadModel"/> (ISP): its one consumer is the
/// job-ad search port, which is the single point where a name reaches the query path.
/// <para>
/// The match is exact — trimmed, whitespace-collapsed, case-insensitive label equality, never a
/// prefix or a fuzzy match (<c>deltidsjobb</c> does not name Deltid). A yrkesområde label yields
/// its yrkesgrupper, as the hero field does for a plus-word. Graceful: an unknown term contributes
/// nothing, an empty input gives <see cref="TaxonomyLabelMatchDto.Empty"/>, never null or a throw.
/// </para>
/// </summary>
public interface ITaxonomyLabelMatcher
{
    ValueTask<TaxonomyLabelMatchDto> MatchExactLabelsAsync(
        IReadOnlyList<string> terms, CancellationToken cancellationToken);
}
