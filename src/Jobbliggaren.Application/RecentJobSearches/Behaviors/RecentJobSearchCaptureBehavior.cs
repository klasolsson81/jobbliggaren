using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.SavedSearches;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.RecentJobSearches.Behaviors;

/// <summary>
/// ADR 0060 — auto-capture-pipeline-behavior. CTO 2026-05-20 Variant A:
/// post-handler-side-effect som fångar varje lyckad <c>ICapturesRecentSearch</c>-
/// query för authenticated user. Pipeline-ordning: efter UnitOfWork (capture
/// sker bara om huvud-query lyckats), före Audit (queries audit:as inte).
///
/// <para>Capture är best-effort: <see cref="IRecentJobSearchCapturer"/>-anropet
/// wrappas i try/catch + log. Capture-fel bryter ALDRIG queryn (defensive —
/// fall här skulle ge 500 på söksidan, oacceptabelt).</para>
///
/// <para>No-op när: (1) meddelandet inte bär <see cref="ICapturesRecentSearch"/>,
/// (2) respons inte bär <see cref="IRecentSearchCaptureResponse"/>, (3) anonym
/// användare, (4) <see cref="SearchCriteria.Create"/> failar (tom/invalid filter
/// — default-browse capture:as ej).</para>
/// </summary>
public sealed partial class RecentJobSearchCaptureBehavior<TMessage, TResponse>(
    ICurrentUser currentUser,
    IRecentJobSearchCapturer capturer,
    ISearchQueryParser parser,
    ILogger<RecentJobSearchCaptureBehavior<TMessage, TResponse>> logger)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next(message, cancellationToken).ConfigureAwait(false);

        if (message is not ICapturesRecentSearch capt)
            return response;

        // Commit-intent-guard (Fas E2j, ADR 0060 amendment 2026-06-12):
        // capture ENDAST vid avsiktlig commit. Live-förhandsvisning per ord
        // (router.replace, commit=false) får aldrig fångas — annars återinförs
        // E2i:s mellanstegsspam + data-minimerings-regression (Art. 5(1)(c)).
        // Additiv till default-browse-guarden nedan, ersätter den inte:
        // en commit på tom sökning capture:as fortfarande aldrig.
        if (!capt.Commit)
            return response;

        if (response is not IRecentSearchCaptureResponse capResp)
            return response;

        if (currentUser.UserId is not { } userId)
            return response;

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
        var occupationGroupCount = capt.OccupationGroup?.Count ?? 0;
        var municipalityCount = capt.Municipality?.Count ?? 0;
        var regionCount = capt.Region?.Count ?? 0;
        var employmentTypeCount = capt.EmploymentType?.Count ?? 0;
        var worktimeExtentCount = capt.WorktimeExtent?.Count ?? 0;
        // #311 PR-2b C1 (ADR 0087 D6): en committad sökning med ENBART arbetsgivar-filter
        // (?employer=) ska fångas (annars tyst gap analogt C1/Klass 2). Employer räknas därför i
        // default-browse-guarden.
        var employerCount = capt.Employer?.Count ?? 0;
        // #551 PR-D: en committad sökning med ENBART distans-filter (?remote=true) ska fångas —
        // remote=true är en äkta filter-intention, inte default-browse. remote=false = inget filter
        // (bool-semantik). MÅSTE hållas i lockstep med SearchCriteria.Create:s tom-invariant
        // (architect-bind — annars tyst no-capture: guarden släpper igenom men Create avvisar, ELLER
        // guarden blockerar en giltig VO).
        // #831 — the read-path validators no longer 400 a sub-minimum q; the parser nulls it
        // and the search runs on the dimensions alone. So `capt.Q` is no longer necessarily
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
        // by construction, since `effectiveQ` is then `capt.Q` itself. The class that does
        // change is the one the parser DROPS but Create used to accept: invisible stuffing such
        // as a letter followed by U+200B ZERO WIDTH SPACE, where `char.IsWhiteSpace` is false so
        // NormalizeString's Trim keeps it at length 2 while the parser strips the Cf rune and
        // nulls the 1-char residual. Those
        // rows now capture as q = null (with a dimension) or not at all (without one) — both
        // the honest outcome, since a nulled q is what the search ran on. That class was never
        // unreachable: raw length 2 passed the old MinimumLength too, which is the same leak
        // that justifies removing the minimum in the first place.
        var effectiveQ = parser.Parse(capt.Q).ResidualQ is null ? null : capt.Q;

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
        // with no anonymous persistable meaning. Relevance-without-residual is that same case.
        var effectiveSortBy = capt.SortBy == JobAdSortBy.Relevance && effectiveQ is null
            ? JobAdSortBy.PublishedAtDesc
            : capt.SortBy;

        if (string.IsNullOrWhiteSpace(effectiveQ) && occupationGroupCount == 0
            && municipalityCount == 0 && regionCount == 0
            && employmentTypeCount == 0 && worktimeExtentCount == 0
            && employerCount == 0 && !capt.Remote)
        {
            return response;
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
        if ((capt.Employer ?? []).Any(EmployerAxisGate.IsWithheld))
            return response;

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
        // (JobSeeker.ValidateDisplayName, Resume.ValidateName, AutoPromoteGate run the identical
        // one-liner) rather than a predicate re-derived per call site (#844: a rule with two
        // normalisers is two rules). What this axis DOES choose is the gap POLICY: #1415 split it
        // per kind of text, and a hand-typed box takes SingleLineUserInput (ADR 0134 D2). The CV
        // surfaces keep the narrow one, which is not a weaker choice but a different one - a line
        // break means something in extracted text and nothing here.
        if (effectiveQ is not null && BearsPersonnummer(effectiveQ))
            return response;

        // Klas-beslut 2026-08-20 (#1419). Same sink, same skip-reason and same bearer analysis
        // as the two guards above - these five dimensions are validated on shape only and never
        // against the taxonomy, so a hand-edited URL reached the sink past both of them.
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
        if (BearsPersonnummer(capt.OccupationGroup) || BearsPersonnummer(capt.Municipality)
            || BearsPersonnummer(capt.Region) || BearsPersonnummer(capt.EmploymentType)
            || BearsPersonnummer(capt.WorktimeExtent))
            return response;

        try
        {
            var criteriaResult = SearchCriteria.Create(
                occupationGroup: capt.OccupationGroup ?? [],
                municipality: capt.Municipality ?? [],
                region: capt.Region ?? [],
                employmentType: capt.EmploymentType ?? [],
                worktimeExtent: capt.WorktimeExtent ?? [],
                employer: capt.Employer ?? [],
                remote: capt.Remote,
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
            // träffa i test: behaviorn unit-testas mot en fake query som ingen validator ser, så
            // ett `OccupationGroup: ["!"]` skulle nå den direkt. Skälet är att ett sådant test
            // inte skulle representera något NÅBART produktions-tillstånd — samtliga sju
            // failure-paths i Create är onåbara från en query som passerar
            // ListJobAdsQueryValidator (uppräknade och verifierade i #831:s rond-3-granskning).
            // Ett test här hade pinnat en fiktion, vilket CLAUDE.md §7 förbjuder.
            if (criteriaResult.IsFailure)
                return response;

            await capturer
                .CaptureAsync(userId, criteriaResult.Value, capResp.TotalCount, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // PII-hygien (security-auditor F6 P4a High-1 2026-05-20):
            // Logga endast exception-typ + meddelande-typ, INTE hela Exception-
            // objektet — Npgsql kan i vissa konfigurationer (Include Error Detail)
            // läcka SQL-parameter-värden (q-fritext upp till 100 tecken kan vara
            // person-/företagsnamn). Stacken är inte värdefull för en best-effort
            // no-op-väg.
            LogCaptureFailed(logger, ex.GetType().FullName ?? "Unknown", typeof(TMessage).Name);
        }

        return response;
    }

    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Warning,
        Message = "RecentJobSearch auto-capture misslyckades för {MessageType} (best-effort, query orörd). ExceptionType={ExceptionType}")]
    private static partial void LogCaptureFailed(ILogger logger, string exceptionType, string messageType);

    // The single-line flag predicate, in ONE home. Both string-bearing guards below the employer
    // one call it, so this file no longer carries the same expression twice while citing #844
    // against exactly that.
    private static bool BearsPersonnummer(string value) =>
        PersonnummerScanner.Scan(
            PersonnummerTextNormalizer.Normalize(value, PersonnummerGapProfile.SingleLineUserInput)).Count > 0;

    private static bool BearsPersonnummer(IReadOnlyList<string>? values) =>
        values is not null && values.Any(BearsPersonnummer);
}
