namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Resultatet av <see cref="ISearchQueryParser.Parse"/> (ADR 0067 Beslut 5c,
/// Fas D2). Ren Application-DTO (CLAUDE.md §3.3 record class).
/// <para>
/// <b>Variant A+A (architect + CTO 2026-06-10):</b> kontraktet bär inga
/// dimensions-fält. ADR 0067 Beslut 5c skrevs pre-C2 och namngav
/// <c>(SsykConceptIds, RegionConceptIds, EmploymentTypeConceptIds, ResidualQ)</c>;
/// det reconcileras bort eftersom (a) dimensions-disambiguering är FE-chip-ansvar
/// (Beslut 5b/Fas E), inte "gissande backend", och (b) dimensions-fält som alltid
/// vore tomma från residual-vägen är Speculative Generality (Fowler 2018). Se ADR
/// 0067 implementerings-notat 2026-06-10 (Fas D2).
/// </para>
/// <para>
/// <see cref="ResidualQ"/> är <c>null</c> (ingen positiv fritext) eller en
/// normaliserad sträng på <c>SearchCriteria.QMinLength</c>..<c>QMaxLength</c>
/// tecken. <see cref="ExcludedTerms"/> are the minus-words (#2143, ADR 0167): each
/// one is text, never a dimension — what it hides is resolved by the search port.
/// </para>
/// </summary>
public sealed record ParsedSearchQuery(string? ResidualQ, IReadOnlyList<string> ExcludedTerms)
{
    /// <summary>
    /// Each excluded term costs one anti-join in every list, count and facet query, so the
    /// grammar keeps at most this many. The frontend mirrors it as <c>Q_MAX_EXCLUDED_TERMS</c>.
    /// </summary>
    public const int MaxExcludedTerms = 5;

    public ParsedSearchQuery(string? residualQ)
        : this(residualQ, [])
    {
    }

    /// <summary>True when the text carries a positive residual or at least one excluded term.</summary>
    public bool HasTerms => ResidualQ is not null || ExcludedTerms.Count > 0;

    public bool Equals(ParsedSearchQuery? other) =>
        other is not null
        && string.Equals(ResidualQ, other.ResidualQ, StringComparison.Ordinal)
        && ExcludedTerms.SequenceEqual(other.ExcludedTerms, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ResidualQ, StringComparer.Ordinal);
        foreach (var term in ExcludedTerms)
            hash.Add(term, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
