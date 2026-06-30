using FluentValidation;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Application.JobAds.Queries.ListJobAds;

public sealed class ListJobAdsQueryValidator : AbstractValidator<ListJobAdsQuery>
{
    // JobTech v2 concept-id-format: kort sträng, alfanumerisk + `_-`, observerade
    // exempel ~12 tecken (`MVqp_eS8_kDZ`). Sätter 1-32 som defense-in-depth-yta
    // (Saltzer/Schroeder 1975 default-deny). CTO-rond 2026-05-13 Q7a/Q7b.
    // ADR 0042 Beslut B — multi: per-element-regex + maxantal-cap speglar
    // SearchCriteria.Create (Domain = sanningskälla; detta = defense-in-depth
    // pre-handler-yta, samma mönster som single-värde-validatorn hade).
    private const string ConceptIdPattern = @"^[A-Za-z0-9_-]{1,32}\z";

    // #311 D6 (ADR 0087) — org.nr är INTE en JobTech concept-id: ett svenskt
    // organisationsnummer är exakt 10 siffror (live-verifierad form i JobStream,
    // t.ex. 5592804784; lagras verbatim i organization_number-kolumnen, ingen
    // bindestrecks-normalisering). Strikt \d{10}\z (\z, ej $, mot newline-injektion,
    // paritet ConceptIdPattern). Default-deny defense-in-depth (Saltzer/Schroeder).
    private const string OrganizationNumberPattern = @"^\d{10}\z";

    public ListJobAdsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        // F4-14 — Sort är read-side-ytan (ListJobAdsSort: 5 rena + MatchDesc).
        // q.SortBy/q.SortByMatch är härledda och alltid giltiga.
        RuleFor(q => q.Sort).IsInEnum();

        // Maxantal-cap (invariant 2) — IN(...)-blowup/jsonb-stuffing-DoS-tak.
        // Refererar Domain-konstanten (single source).
        //
        // ADR 0067 Beslut 1 — dimensioner OccupationGroup (ssyk-level-4/
        // yrkesgrupp, primärt yrke-filter) + Municipality (kommun) + Region.
        // Fas C2 (CTO-dom (e)): Ssyk-paramen (occupation-name) borttagen —
        // ?ssyk= är numera en obunden query-param som ignoreras av endpointen
        // tills Fas E byter FE-picker.
        RuleFor(q => q.OccupationGroup!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(q => q.OccupationGroup is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} yrkesgrupper per sökning.");

        RuleForEach(q => q.OccupationGroup)
            .Matches(ConceptIdPattern)
            .When(q => q.OccupationGroup is not null)
            .WithMessage("Yrkesgrupp måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(q => q.Municipality!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(q => q.Municipality is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} kommuner per sökning.");

        RuleForEach(q => q.Municipality)
            .Matches(ConceptIdPattern)
            .When(q => q.Municipality is not null)
            .WithMessage("Kommun måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(q => q.Region!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(q => q.Region is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} regioner per sökning.");

        RuleForEach(q => q.Region)
            .Matches(ConceptIdPattern)
            .When(q => q.Region is not null)
            .WithMessage("Region måste vara en giltig JobTech location-concept-id (1-32 tecken, alfanumeriskt + _-).");

        // ADR 0067 Beslut 6 (Fas B2) — Klass 2 anställningsform + omfattning.
        // Samma defense-in-depth-yta (cap + per-element-regex) som dimensionerna
        // ovan; Domain SearchCriteria.Create är sanningskälla.
        RuleFor(q => q.EmploymentType!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(q => q.EmploymentType is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} anställningsformer per sökning.");

        RuleForEach(q => q.EmploymentType)
            .Matches(ConceptIdPattern)
            .When(q => q.EmploymentType is not null)
            .WithMessage("Anställningsform måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(q => q.WorktimeExtent!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(q => q.WorktimeExtent is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} omfattningar per sökning.");

        RuleForEach(q => q.WorktimeExtent)
            .Matches(ConceptIdPattern)
            .When(q => q.WorktimeExtent is not null)
            .WithMessage("Omfattning måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        // #311 D6 (ADR 0087) — arbetsgivar-facet (org.nr). Samma cap-yta som övriga
        // dimensioner (IN(...)-blowup-tak); per-element-formatet är dock org.nr
        // (10 siffror), INTE concept-id. Defense-in-depth pre-handler-yta.
        RuleFor(q => q.Employer!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(q => q.Employer is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} arbetsgivare per sökning.");

        RuleForEach(q => q.Employer)
            .Matches(OrganizationNumberPattern)
            .When(q => q.Employer is not null)
            .WithMessage("Organisationsnummer måste vara 10 siffror.");

        // q MinLength(2) hindrar `?q=a` (matchar närapå hela tabellen → DoS-yta).
        // MaxLength(100) räcker för normal söksträng + safety margin mot injection-
        // stuffing. CTO-rond 2026-05-13 Q7c. Refererar Domain-konstanterna
        // (single source) — speglar MaxConceptIds-mönstret ovan; samma gräns
        // läses av ISearchQueryParser (ADR 0067 Fas D2).
        RuleFor(q => q.Q)
            .MinimumLength(SearchCriteria.QMinLength)
            .MaximumLength(SearchCriteria.QMaxLength)
            .When(q => !string.IsNullOrWhiteSpace(q.Q))
            .WithMessage("Söktext måste vara 2-100 tecken.");

        // ADR 0042 Beslut D — relevans-sortering kräver söktext (fail-fast,
        // speglar SearchCriteria.Create-invarianten). Match-sorten (MatchDesc)
        // kräver INGEN söktext — den ordnar på profil-match, inte ts_rank.
        RuleFor(q => q.Q)
            .NotEmpty()
            .When(q => q.Sort == ListJobAdsSort.Relevance)
            .WithMessage("Relevans-sortering kräver en söktext.");

        // ADR 0079 STEG 5 + #300 PR-4 (ADR 0084 §F4) — grad-filtret är Fast-bandet
        // (Grund/Relaterat/Bra/Stark). Cap = 4 (de fyra filtrerbara graderna; defense-in-depth-
        // tak — Related infogad mellan Basic och Good, ADR 0084 §F2). Related ÄR Fast-beräkningsbar
        // (en kategorisk exact-vs-related-split på shadow-kolumnen, GradeRankExpression rank 2),
        // till skillnad från Topp som AVVISAS wire-side: listfiltret kan inte beräkna must-have-
        // täckning i SQL (G3-OPT-A), så en Topp-grad skulle tyst matcha noll → en label-lie. Detta
        // är den strukturella ärlighets-grinden (CTO-bind 2026-06-23). Tom/null = inget grad-filter.
        RuleFor(q => q.MatchGrades!)
            .Must(g => g.Count <= 4)
            .When(q => q.MatchGrades is not null)
            .WithMessage("Max 4 matchningsgrader (Grund/Relaterat/Bra/Stark) per filter.");

        RuleForEach(q => q.MatchGrades)
            .Must(g => g is MatchGrade.Basic or MatchGrade.Related or MatchGrade.Good or MatchGrade.Strong)
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
