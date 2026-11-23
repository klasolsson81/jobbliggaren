using FluentValidation;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Application.RecentJobSearches.Common;

namespace Jobbliggaren.Application.JobAds.Queries.ListJobAds;

public sealed class ListJobAdsQueryValidator : AbstractValidator<ListJobAdsQuery>
{
    public ListJobAdsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        // F4-14 — Sort är read-side-ytan (ListJobAdsSort: 5 rena + MatchDesc).
        // q.SortBy/q.SortByMatch är härledda och alltid giltiga.
        RuleFor(q => q.Sort).IsInEnum();

        // #2285 — the search axes and the q bound live in one validator, shared with the command that stores a
        // search, so the two cannot drift apart.
        Include(new RecentSearchCandidateValidator());

        // ADR 0042 Beslut D — relevans-sortering kräver söktext (fail-fast).
        // Match-sorten (MatchDesc) kräver INGEN söktext — den ordnar på profil-match,
        // inte ts_rank.
        //
        // #831 — SCOPE, exakt: den här regeln mäter RÅ q och fångar bara den TOMMA
        // söktexten. Den garanterar INTE en non-null residual, vilket är vad sorten
        // faktiskt behöver. `?q=a&sort=relevans` passerar alltså här, nollas i sökporten
        // och degraderar medvetet till PublishedAt desc i `ApplyRelevanceSort` — ett
        // nåbart tillstånd sedan q-minimum flyttade till parsern, pinnat av
        // `Validate_RelevanceSort_WithSubMinimumQ_Passes_ParserDecidesTheOrder`.
        //
        // Att i stället köra parsern här vore CTO:ns avvisade Cut E (validatorn skulle
        // få ett Application-beroende). Och att
        // 400:a vore att återinföra precis den vägran #831 tar bort. Degradering är
        // rätt beteende; meddelandet nedan är därför formulerat om så det inte längre
        // påstår mer än regeln kontrollerar.
        RuleFor(q => q.Q)
            .NotEmpty()
            .When(q => q.Sort == ListJobAdsSort.Relevance)
            .WithMessage("Relevans-sortering kräver att du anger en söktext.");

        // ADR 0079 STEG 5 + #300 PR-4 (ADR 0084 §F4) — grad-filtret är Fast-bandet
        // (Grund/Relaterat/Bra/Stark). Cap = bandets storlek, inte en literal (defense-in-depth-
        // tak — Related infogad mellan Basic och Good, ADR 0084 §F2). Related ÄR Fast-beräkningsbar
        // (en kategorisk exact-vs-related-split på shadow-kolumnen, GradeRankExpression rank 2),
        // till skillnad från Topp som AVVISAS wire-side: listfiltret kan inte beräkna must-have-
        // täckning i SQL (G3-OPT-A), så en Topp-grad skulle tyst matcha noll → en label-lie. Detta
        // är den strukturella ärlighets-grinden (CTO-bind 2026-06-23). Tom/null = inget grad-filter.
        RuleFor(q => q.MatchGrades!)
            .Must(g => g.Count <= MatchGradeBands.Filterable.Count)
            .When(q => q.MatchGrades is not null)
            .WithMessage(
                $"Max {MatchGradeBands.Filterable.Count} matchningsgrader "
                + "(Grund/Relaterat/Bra/Stark) per filter.");

        RuleForEach(q => q.MatchGrades)
            .Must(MatchGradeBands.Filterable.Contains)
            .When(q => q.MatchGrades is not null)
            .WithMessage(
                "Endast Grund/Relaterat/Bra/Stark kan filtreras — Topp kräver CV-styrkt "
                + "kravtäckning och kan inte beräknas i listfiltret.");

        // #383 (CTO-bind cto-7f3a9c2e1b4d8a6f) — status-facetterna är ömsesidigt
        // uteslutande där de motsäger varandra: "visa endast ansökta" OCH "dölj ansökta"
        // samtidigt är självmotsägande (tom mängd by construction) → rent 400 wire-side i
        // stället för en tyst tom sida. SavedOnly är fritt kombinerbart med båda ("sparade
        // jag inte sökt ännu" = savedOnly + hideApplied är giltigt). Validerar på hela
        // queryn (de två bool:arna läses tillsammans).
        RuleFor(q => q)
            .Must(q => !(q.AppliedOnly && q.HideApplied))
            .WithMessage(
                "Det går inte att både visa endast ansökta och dölja ansökta annonser samtidigt.");
    }
}
