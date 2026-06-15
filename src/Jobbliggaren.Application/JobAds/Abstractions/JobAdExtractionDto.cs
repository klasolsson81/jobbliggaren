namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Read-side projection of a job ad's persisted <c>ExtractedTerms</c>
/// (the explainable surface, F4-4 / CLAUDE.md §5). Returned by
/// <c>GetJobAdExtractedTermsQuery</c>. Maps the Domain value object to a transport
/// DTO at the boundary (CLAUDE.md §2.3 — queries never return domain objects);
/// <see cref="ExtractedTermDto.Kind"/>/<see cref="ExtractedTermDto.Source"/> are
/// stringified so the API contract is decoupled from the Domain enum ordinals.
/// An ad not yet extracted yields an empty <see cref="Terms"/> list.
/// </summary>
public sealed record JobAdExtractionDto(
    Guid JobAdId,
    IReadOnlyList<ExtractedTermDto> Terms);

/// <summary>
/// One extracted term with cited evidence. <see cref="Lexeme"/> is the overlap
/// token (keyword stem | skill concept-id); <see cref="MatchedOn"/> cites the span
/// that grounded it (explainable by design).
/// </summary>
public sealed record ExtractedTermDto(
    string Lexeme,
    string Display,
    string Kind,
    string Source,
    string MatchedOn,
    string? ConceptId,
    double Weight);
