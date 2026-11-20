using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Application.RecentJobSearches.Common;

/// <summary>
/// #2199 PR 2b — the one predicate deciding whether a search may be stored in recent searches, and what is
/// stored. <c>RecentJobSearchCaptureBehavior</c> stores exactly what <see cref="Admit"/> returns, and
/// <c>ListJobAdsQueryHandler</c> answers its verdict on every list response, so the /jobb hero offers
/// "Spara sökningen" only for a search this gate admits. Before it, the hero's confirmation was false on
/// every skip below (senior-cto-advisor, #2199 ruling 6).
/// </summary>
/// <remarks>
/// It is total: the parser, the personnummer scanner and <see cref="SearchCriteria.Create"/> return values
/// and never throw, which matters because the list handler calls it outside the capture's catch. It knows
/// nothing of the commit flag or the user: those are the behaviour's, since they say whether this request
/// stores, not whether this search may be stored.
/// </remarks>
public sealed class RecentSearchCaptureGate(ISearchQueryParser parser, IReligiousOccupations religiousOccupations)
{
    /// <summary>The criteria to store for <paramref name="search"/>, or <see langword="null"/> when it may not be stored.</summary>
    public SearchCriteria? Admit(ICapturesRecentSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);

        // Default-browse-guard (security-auditor F6 P4a High-2 2026-05-20):
        // explicit lokal invariant — capture:a aldrig "alla annonser, ingen
        // filter". Skyddar mot data-minimerings-regression (Art. 5(1)(c)) om
        // SearchCriteria-VO:t i framtiden lättar på sin Empty-invariant
        // för en annan feature. SearchCriteria.Create kallas fortfarande
        // nedan så normalisering + validering äger rum innan persist.
        // Fas C2 (ADR 0067): guarden räknar alla dimensioner — stänger C1:s
        // live-gap där yrkesgrupp-/kommun-only-sökningar inte fångades.
        // Fas B2 (ADR 0067 Beslut 6): Klass 2 (anställningsform/omfattning)
        // ingår — en commit:ad sökning med ENBART t.ex. anställningsform ska
        // fångas (annars tyst gap analogt C1).
        var occupationGroupCount = search.OccupationGroup?.Count ?? 0;
        // #2199 PR 2: a committed search with ONLY occupation-names (/jobb's variant tick) is a real
        // filter and is captured, in lockstep with SearchCriteria.Create's empty invariant.
        var occupationNameCount = search.OccupationName?.Count ?? 0;
        var municipalityCount = search.Municipality?.Count ?? 0;
        var regionCount = search.Region?.Count ?? 0;
        var employmentTypeCount = search.EmploymentType?.Count ?? 0;
        var worktimeExtentCount = search.WorktimeExtent?.Count ?? 0;
        // #311 PR-2b C1 (ADR 0087 D6): en committad sökning med ENBART arbetsgivar-filter
        // (?employer=) ska fångas (annars tyst gap analogt C1/Klass 2). Employer räknas därför i
        // default-browse-guarden.
        var employerCount = search.Employer?.Count ?? 0;
        // #551 PR-D: en committad sökning med ENBART distans-filter (?remote=true) ska fångas —
        // remote=true är en äkta filter-intention, inte default-browse. remote=false = inget filter
        // (bool-semantik). MÅSTE hållas i lockstep med SearchCriteria.Create:s tom-invariant
        // (architect-bind — annars tyst no-capture: guarden släpper igenom men Create avvisar, ELLER
        // guarden blockerar en giltig VO).
        // #831 — the read-path validators no longer 400 a sub-minimum q; the parser nulls it
        // and the search runs on the dimensions alone. So `search.Q` is no longer necessarily
        // what the search USED, and capturing it raw broke the lockstep the comment above
        // names: the guard let `q="a"` through, then SearchCriteria.Create rejected it on its
        // own min-invariant and the whole capture was silently dropped — INCLUDING a
        // perfectly valid dimension filter. `?q=a&occupationGroup=X&commit=1` returned 200
        // with results and vanished from "Senaste sökningar" over one character the system
        // itself ignores.
        //
        // `effectiveQ` is therefore what RAN, and BOTH the guard below and Create use it, so
        // the two cannot disagree again.
        //
        // Deliberately NOT `ResidualQ` itself: it also collapses internal whitespace, while
        // SearchCriteria.NormalizeString only trims — passing it would change FilterHash for
        // searches that already captured fine, i.e. the dedupe semantics of persisted rows.
        // That is a different change-reason and belongs in its own PR.
        //
        // For any q the parser KEEPS, the captured value is byte-identical to before — that is
        // by construction, since `effectiveQ` is then `search.Q` itself. The class that does
        // change is the one the parser DROPS but Create used to accept: invisible stuffing such
        // as a letter followed by U+200B ZERO WIDTH SPACE, where `char.IsWhiteSpace` is false so
        // NormalizeString's Trim keeps it at length 2 while the parser strips the Cf rune and
        // nulls the 1-char residual. Those
        // rows now capture as q = null (with a dimension) or not at all (without one) — both
        // the honest outcome, since a nulled q is what the search ran on. That class was never
        // unreachable: raw length 2 passed the old MinimumLength too, which is the same leak
        // that justifies removing the minimum in the first place.
        //
        // #2143 — a q of only minus-words is kept: it filtered the search the user ran.
        var parsed = parser.Parse(search.Q);
        var effectiveQ = parsed.HasTerms ? search.Q : null;

        // #831 review round 2 — `Create` has THREE q-dependent invariants, not two. Beyond the
        // Empty guard and the min-length rule there is the `SearchCriteria.RelevanceRequiresQ`
        // failure: Relevance with a null q is rejected. So
        // `?q=a&sortBy=Relevance&occupationGroup=X&commit=true` reproduced the very defect
        // `effectiveQ` was added to fix — 200 with the dimension applied, then a silent
        // no-capture one character later.
        //
        // Same principle, same answer: capture the sort that actually RAN. With a nulled
        // residual `ApplyRelevanceSort` falls back to PublishedAt desc, so that is what the
        // user got. This is not a new convention — `ListJobAdsSortExtensions.ToDomainSort`
        // already maps MatchDesc to PublishedAtDesc for this exact seam, and documents it as
        // "den honesta fallbacken ... det värde som recent-search-hashen lagrar" for a sort
        // with no anonymous persistable meaning. Relevance-without-residual is that same case,
        // and so is a q of only minus-words (#2143): relevance ranks the positive residual.
        var effectiveSortBy = search.SortBy == JobAdSortBy.Relevance && parsed.ResidualQ is null
            ? JobAdSortBy.PublishedAtDesc
            : search.SortBy;

        if (string.IsNullOrWhiteSpace(effectiveQ) && occupationGroupCount == 0
            && occupationNameCount == 0 && municipalityCount == 0 && regionCount == 0
            && employmentTypeCount == 0 && worktimeExtentCount == 0
            && employerCount == 0 && !search.Remote)
        {
            return null;
        }

        // A2 (Klas-beslut 2026-08-19, security-auditor Major). ?employer= is a FORMAT gate
        // (^[0-9]{10}\z) and never a personnummer discriminator, because a 10-digit personnummer
        // is format-identical to an org.nr. Without this guard a committed search on a hand-typed
        // pnr-shaped value persists it in PLAINTEXT to recent_job_searches.employer_list: no
        // encryption (ADR 0049's envelope scope is four user-owned PII columns - cover letter,
        // application notes, follow-up notes, resume content - and never this one), no
        // time-based retention (an LRU cap of 20 per seeker), and no hit gate, so a value
        // matching zero ads is stored exactly as reliably as one that matches. For an enskild
        // firma that value IS the holder's personnummer (#841); the bearer is a THIRD PARTY, so
        // no acceptance route existed and this is fixed rather than accepted.
        //
        // SKIP rather than filter the value out of the list, and the reason is FilterHash, not
        // replayability: a criteria with employer stripped hashes IDENTICALLY to a genuine
        // employer-less search on the same other dimensions, so filtering would find that row
        // and Bump() it - silently corrupting LastSeenCount and LastViewedAt on a DIFFERENT,
        // real search (code-reviewer). Skip also stores strictly less, and the replay path reads
        // the column through the same gate (EmployerAxisGate, #1471), so a value refused here has
        // no second route to the wire (security-auditor). The SEARCH still runs: refusing it
        // would break a legitimate filter on a sole trader's ads, which are real ads.
        //
        // OrganizationNumber.IsPersonnummerShaped is the house's single-sourced discriminator,
        // and this was the one PERSISTENCE SINK that never consulted it.
        if ((search.Employer ?? []).Any(EmployerAxisGate.IsWithheld))
            return null;

        // Same sink and the same skip-reason as the employer guard above, so that block governs
        // here too - with one widening. There the bearer is NECESSARILY a third party, since the
        // value is by construction someone else's org.nr. A free-text box can carry the user's own
        // number, a third party's, or a sole trader's org.nr typed here instead of into the
        // employer facet, so the bearer is POSSIBLY a third party - which closes the same
        // acceptance route just as firmly (§9.6(3) requires the controller to be the only affected
        // data subject), and this axis additionally re-renders a captured q verbatim as the
        // "Senaste sokningar" row label (ListRecentSearchesQueryHandler.DeriveLabel).
        //
        // What differs is the detector: `q` is free text with no format gate, so it carries
        // hyphenated, 12-digit and gapped forms the ten-digit employer axis structurally cannot,
        // and the employer helper's unparseable arm - fail-safe on a format axis - would refuse
        // every ordinary search string here. This is instead the house's single-sourced flag path
        // (Resume.ValidateName, AutoPromoteGate run the identical
        // one-liner) rather than a predicate re-derived per call site (#844: a rule with two
        // normalisers is two rules). What this axis DOES choose is the gap POLICY: #1415 split it
        // per kind of text, and a hand-typed box takes SingleLineUserInput (ADR 0134 D2). The CV
        // surfaces keep the narrow one, which is not a weaker choice but a different one - a line
        // break means something in extracted text and nothing here.
        if (effectiveQ is not null && BearsPersonnummer(effectiveQ))
            return null;

        // Klas-beslut 2026-08-20 (#1419). Same sink, same skip-reason and same bearer analysis
        // as the two guards above - these six dimensions are validated on shape only and never
        // against the taxonomy, so a hand-edited URL reached the sink past both of them. The sixth,
        // OccupationName, joined the chain when #2199 PR 2 began persisting it.
        //
        // Runs SingleLineUserInput because that is what the value IS - a single line out of a
        // hand-editable URL. The choice is behaviourally inert while the conceptId grammar admits
        // no whitespace or control character, and it is the right one on the day that relaxes.
        // TaxonomyAxisProfileIsInertTests drives the production validator, so a relaxation fails
        // there rather than changing this guard's reach quietly.
        //
        // The detector is the house's single-sourced flag chain rather than the employer axis's
        // shape predicate (#844), and that is a DECLARED difference in reach, not an oversight:
        // IsPersonnummerShaped is deliberately over-inclusive with no Luhn and no date gate, so a
        // Luhn-invalid ten-digit value is skipped there and captured here. What rides on that
        // residual is not personal data - a Luhn-invalid number is no personnummer, and a legal
        // person's org.nr is not personal data, while a sole trader's IS a valid personnummer and
        // is caught.
        if (BearsPersonnummer(search.OccupationGroup) || BearsPersonnummer(search.OccupationName)
            || BearsPersonnummer(search.Municipality) || BearsPersonnummer(search.Region)
            || BearsPersonnummer(search.EmploymentType) || BearsPersonnummer(search.WorktimeExtent))
            return null;

        var criteriaResult = SearchCriteria.Create(
            occupationGroup: search.OccupationGroup ?? [],
            occupationName: search.OccupationName ?? [],
            municipality: search.Municipality ?? [],
            region: search.Region ?? [],
            employmentType: search.EmploymentType ?? [],
            worktimeExtent: search.WorktimeExtent ?? [],
            employer: search.Employer ?? [],
            remote: search.Remote,
            q: effectiveQ,
            sortBy: effectiveSortBy);

        // #831 truth-sync (rond 2). Parentesen sa tidigare "queryn bör då ha failat i
        // ValidationBehavior". Det är INTE längre sant generellt — validatorerna slutade
        // avvisa q-minimum. Alla TRE q-beroende invarianterna i Create är i stället
        // stängda UPPSTRÖMS av `effectiveQ`/`effectiveSortBy`: Empty-guarden ovan,
        // min-längden (q redan nollat när parsern nollade det) och RelevanceRequiresQ
        // (sorten redan nedgraderad när residualen är null). Grenen är därmed ren
        // defense-in-depth mot en FRAMTIDA q-beroende invariant i Create som inte hålls i
        // lockstep här — vilket är precis den defekt rond 1 och rond 2 hittade en gång var.
        // Inget test når den i dag, medvetet — men skälet är INTE att grenen vore svår att
        // träffa i test: grinden unit-testas mot en fake query som ingen validator ser, så
        // ett `OccupationGroup: ["!"]` skulle nå den direkt. Skälet är att ett sådant test
        // inte skulle representera något NÅBART produktions-tillstånd — samtliga sju
        // failure-paths i Create är onåbara från en query som passerar
        // ListJobAdsQueryValidator (uppräknade och verifierade i #831:s rond-3-granskning).
        // Ett test här hade pinnat en fiktion, vilket CLAUDE.md §7 förbjuder.
        if (criteriaResult.IsFailure)
            return null;

        // #2199 PR 2b (security-auditor's Art. 9 Major on PR #2241; Klas 2026-10-25: "(a) Capture-vakt").
        // A religious office singled out - a name filed under one of the four groups, or one of the groups
        // picked on its own rather than inside a whole occupation field - reveals a faith, which Art. 6(1)(f)
        // cannot carry. Same sink and the same skip-reason as the employer guard above: filtering the ids
        // out would hash identically to a genuine narrower search and Bump() it. The SEARCH still runs. It
        // reads the criteria Create built, so it judges exactly the ids that would be stored, after their
        // normalisation, from whichever row the user ticked them.
        var criteria = criteriaResult.Value;
        if (religiousOccupations.AreSingledOutBy(criteria.OccupationGroup, criteria.OccupationName))
            return null;

        return criteria;
    }

    // The single-line flag predicate, in ONE home. Both string-bearing guards below the employer
    // one call it, so this file no longer carries the same expression twice while citing #844
    // against exactly that.
    private static bool BearsPersonnummer(string value) =>
        PersonnummerScanner.Scan(
            PersonnummerTextNormalizer.Normalize(value, PersonnummerGapProfile.SingleLineUserInput)).Count > 0;

    private static bool BearsPersonnummer(IReadOnlyList<string>? values) =>
        values is not null && values.Any(BearsPersonnummer);
}
