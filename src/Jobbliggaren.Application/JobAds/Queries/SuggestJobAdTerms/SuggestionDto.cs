using Jobbliggaren.Application.JobAds.Abstractions;

namespace Jobbliggaren.Application.JobAds.Queries.SuggestJobAdTerms;

/// <summary>
/// Ett typeahead-förslag (ADR 0067 Beslut 5a — utökad suggest-union). Union av
/// taxonomi-snapshot-labels (Län/Kommun/Yrkesområde/Yrkesgrupp) och job_ads-
/// titel-prefix. Ren Application-DTO (CLAUDE.md §3.3 record class).
/// <para>
/// <see cref="ConceptId"/> är <c>null</c> för <see cref="SuggestionKind.Title"/>
/// (fri titel-text har ingen concept-id); satt för alla taxonomi-träffar. FE
/// (Fas E) bär ett strukturerat chip vidare som <c>{kind, conceptId}</c> mot
/// rätt filter-dimension — eller, för titel-träffen, som ren q-fritext.
/// </para>
/// </summary>
public sealed record SuggestionDto(SuggestionKind Kind, string? ConceptId, string Label);
