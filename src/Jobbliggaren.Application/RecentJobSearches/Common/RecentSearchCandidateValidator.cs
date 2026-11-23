using FluentValidation;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Application.RecentJobSearches.Common;

/// <summary>
/// #2285 — the axis and q rules for a search, shared by <c>ListJobAdsQueryValidator</c> and
/// <c>CaptureRecentSearchCommandValidator</c> through <c>Include</c>. Each concrete validator keeps its own sort rules,
/// since the sort it binds (<c>ListJobAdsSort</c>) is not part of this shape. Internal, so assembly scanning does not
/// register it on its own.
/// </summary>
internal sealed class RecentSearchCandidateValidator : AbstractValidator<IRecentSearchCandidate>
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
    // bindestrecks-normalisering). Strikt [0-9]{10}\z (ASCII, ej \d=\p{Nd} — #865; \z, ej $, mot newline-injektion,
    // paritet ConceptIdPattern). Default-deny defense-in-depth (Saltzer/Schroeder).
    private const string OrganizationNumberPattern = @"^[0-9]{10}\z";

    public RecentSearchCandidateValidator()
    {
        // Maxantal-cap (invariant 2) — IN(...)-blowup/jsonb-stuffing-DoS-tak.
        // Refererar Domain-konstanten (single source).
        //
        // ADR 0067 Beslut 1 — dimensioner OccupationGroup (ssyk-level-4/
        // yrkesgrupp, primärt yrke-filter) + Municipality (kommun) + Region.
        // Fas C2 (CTO-dom (e)): Ssyk-paramen (occupation-name) borttagen —
        // ?ssyk= är numera en obunden query-param som ignoreras av endpointen
        // tills Fas E byter FE-picker.
        RuleFor(s => s.OccupationGroup!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(s => s.OccupationGroup is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} yrkesgrupper per sökning.");

        RuleForEach(s => s.OccupationGroup)
            .Matches(ConceptIdPattern)
            .When(s => s.OccupationGroup is not null)
            .WithMessage("Yrkesgrupp måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        // #2199 — yrkesbenämningar, the same concept-id grammar and cap as the group.
        RuleFor(s => s.OccupationName!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(s => s.OccupationName is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} yrkesbenämningar per sökning.");

        RuleForEach(s => s.OccupationName)
            .Matches(ConceptIdPattern)
            .When(s => s.OccupationName is not null)
            .WithMessage("Yrkesbenämning måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(s => s.Municipality!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(s => s.Municipality is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} kommuner per sökning.");

        RuleForEach(s => s.Municipality)
            .Matches(ConceptIdPattern)
            .When(s => s.Municipality is not null)
            .WithMessage("Kommun måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(s => s.Region!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(s => s.Region is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} regioner per sökning.");

        RuleForEach(s => s.Region)
            .Matches(ConceptIdPattern)
            .When(s => s.Region is not null)
            .WithMessage("Region måste vara en giltig JobTech location-concept-id (1-32 tecken, alfanumeriskt + _-).");

        // ADR 0067 Beslut 6 (Fas B2) — Klass 2 anställningsform + omfattning.
        // Samma defense-in-depth-yta (cap + per-element-regex) som dimensionerna
        // ovan; Domain SearchCriteria.Create är sanningskälla.
        RuleFor(s => s.EmploymentType!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(s => s.EmploymentType is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} anställningsformer per sökning.");

        RuleForEach(s => s.EmploymentType)
            .Matches(ConceptIdPattern)
            .When(s => s.EmploymentType is not null)
            .WithMessage("Anställningsform måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(s => s.WorktimeExtent!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(s => s.WorktimeExtent is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} omfattningar per sökning.");

        RuleForEach(s => s.WorktimeExtent)
            .Matches(ConceptIdPattern)
            .When(s => s.WorktimeExtent is not null)
            .WithMessage("Omfattning måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        // #311 D6 (ADR 0087) — arbetsgivar-facet (org.nr). Samma cap-yta som övriga
        // dimensioner (IN(...)-blowup-tak); per-element-formatet är dock org.nr
        // (10 siffror), INTE concept-id. Defense-in-depth pre-handler-yta.
        RuleFor(s => s.Employer!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(s => s.Employer is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} arbetsgivare per sökning.");

        RuleForEach(s => s.Employer)
            .Matches(OrganizationNumberPattern)
            .When(s => s.Employer is not null)
            .WithMessage("Organisationsnummer måste vara 10 siffror.");

        // #831 — the q MinLength rule is GONE. The parser, not the validator, owns the
        // minimum: `SearchQueryParser` nulls a residual below QMinLength and runs on the
        // dimensions instead, and the search port feeds it every
        // q (#2143). Two rules for one input meant a bookmarked
        // `/jobb?q=a` 400'd on one path and degraded on the other; the parser wins because a
        // GET list query should degrade, not refuse.
        //
        // The DoS justification the removed rule carried ("matchar närapå hela tabellen →
        // DoS-yta", CTO-rond 2026-05-13 Q7c) does not survive contact with what nulling
        // does: a nulled q runs the DEFAULT browse query — indexed, paginated, PageSize ≤
        // 100 — not a near-full-table scan. The guard was also leaky, which is the stronger
        // reason: it measured RAW q while the parser normalises first (strips Cc/Cf,
        // collapses whitespace) and only then compares, so `?q=a<ZWSP>` passed the validator
        // and got nulled anyway. A bound that one invisible character defeats protects nothing.
        //
        // MaxLength STAYS and is the load-bearing half: SearchQueryParser's
        // `new StringBuilder(raw.Length)` allocation enumerates every rune BEFORE truncating,
        // so the max is the only pre-work resource bound. Same shape SearchSkillsQueryValidator
        // already chose (max only, never a 400 mid-typing). Refererar Domain-konstanten
        // (single source) — speglar MaxConceptIds-mönstret ovan.
        // .When() borttagen med minimum-regeln (#831, architect Minor 3): den vaktade
        // MinimumLength mot ""/"   ". FluentValidations längdvalidator returnerar true för
        // null, och varje icke-null sträng <= max passerar ändå, så villkoret kan inte längre
        // ändra utfallet — en död guard är en andra regel på samma input, vilket är precis
        // det den här PR:en tar bort.
        RuleFor(s => s.Q)
            .MaximumLength(SearchCriteria.QMaxLength)
            .WithMessage($"Söktext får vara högst {SearchCriteria.QMaxLength} tecken.");
    }
}
