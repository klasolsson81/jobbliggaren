namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Resultatet av <see cref="ISearchQueryParser.Parse"/> (ADR 0067 Beslut 5c,
/// Fas D2). Ren Application-DTO (CLAUDE.md §3.3 record class).
/// <para>
/// <b>Variant A+A (architect + CTO 2026-06-10):</b> kontraktet bär ENBART
/// <see cref="ResidualQ"/> — inga dimensions-fält. ADR 0067 Beslut 5c skrevs
/// pre-C2 och namngav <c>(SsykConceptIds, RegionConceptIds,
/// EmploymentTypeConceptIds, ResidualQ)</c>; det reconcileras bort eftersom
/// (a) dimensions-disambiguering är FE-chip-ansvar (Beslut 5b/Fas E), inte
/// "gissande backend", och (b) dimensions-fält som alltid vore tomma från
/// residual-vägen är Speculative Generality (Fowler 2018). Se ADR 0067
/// implementerings-notat 2026-06-10 (Fas D2).
/// </para>
/// <para>
/// <see cref="ResidualQ"/> är <c>null</c> (ingen fritextsökning) eller en
/// normaliserad sträng på <c>SearchCriteria.QMinLength</c>..<c>QMaxLength</c>
/// tecken. Matar <c>JobAdFilterCriteria.Q</c> → FTS-hybridens OR-gren.
/// </para>
/// </summary>
public sealed record ParsedSearchQuery(string? ResidualQ);
