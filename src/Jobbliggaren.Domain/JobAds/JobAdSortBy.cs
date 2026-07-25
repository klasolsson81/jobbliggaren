namespace Jobbliggaren.Domain.JobAds;

// Whitelistad sort-enum per CTO-beslut F2-P7 (CLAUDE.md §5.1 "Magic strings förbjudet",
// OCP via enum-extension). Direction-suffix bakad i value:t för att hålla query-record
// minimal (en enum istället för fält + bool).
//
// Flyttad till Domain 2026-05-16 (ADR 0039): SearchCriteria-VO:t (Domain) speglar
// ListJobAds-sortyta och får per Clean Arch §2.1 inte bero på Application. Enumen är
// ren domän-vokabulär (sorterings-avsikt), inte ett Application-koncept.
public enum JobAdSortBy
{
    PublishedAtDesc = 0,
    PublishedAtAsc = 1,
    ExpiresAtDesc = 2,
    ExpiresAtAsc = 3,

    // ADR 0042 Beslut D — relevans-sort (D2 ILIKE-heuristik). Relevans-ordning utan
    // söktext är odefinierad.
    // #831 truth-sync: "invariant i SearchCriteria.Create + ListJobAdsQueryValidator"
    // stämmer inte längre för validatorn. Den upprätthåller `NotEmpty` på RÅ q, medan
    // sorten behöver en non-null RESIDUAL — och sedan q-minimum flyttade till parsern
    // är `?q=a&sort=relevans` ett nåbart tillstånd där råvärdet finns men residualen
    // nollas. Det degraderar medvetet till PublishedAt desc (ApplyRelevanceSort), det
    // 400:ar inte. Create-invarianten står kvar och gäller SPARADE sökningar.
    Relevance = 4,
}
