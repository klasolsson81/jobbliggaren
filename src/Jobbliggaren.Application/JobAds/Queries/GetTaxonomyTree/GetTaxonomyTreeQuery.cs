using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;

/// <summary>
/// ADR 0043 — picker-träd för sök-ytan (Län + Yrkesområde→Yrke). Inga
/// parametrar: trädet är statiskt och bounded, hämtas i sin helhet bakom
/// <c>ITaxonomyReadModel</c> (cache + ETag). Speglar
/// <c>SuggestJobAdTermsQuery</c>-mönstret (tunn query : IQuery&lt;T&gt;).
/// </summary>
public sealed record GetTaxonomyTreeQuery : IQuery<TaxonomyTreeDto>;
