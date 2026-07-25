using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Common;
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
        // That is a different change-reason and belongs in its own PR. This form is strictly
        // additive: for any q the parser keeps, the captured value is byte-identical to
        // before; only the previously-unreachable sub-minimum case changes, and it changes
        // from "silent no-capture" to "capture what actually ran".
        var effectiveQ = parser.Parse(capt.Q).ResidualQ is null ? null : capt.Q;

        if (string.IsNullOrWhiteSpace(effectiveQ) && occupationGroupCount == 0
            && municipalityCount == 0 && regionCount == 0
            && employmentTypeCount == 0 && worktimeExtentCount == 0
            && employerCount == 0 && !capt.Remote)
        {
            return response;
        }

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
                sortBy: capt.SortBy);

            // Andra valideringsfel speglar query-validator-brott och bör inte
            // rendera capture. #831 truth-sync: parentesen sa tidigare "queryn bör då ha
            // failat i ValidationBehavior" — det är INTE längre sant för q-minimum, som
            // validatorerna slutade avvisa. Den vägen är i stället stängd uppströms: `q`
            // som når Create är `effectiveQ`, alltså redan nollad när parsern nollade den,
            // så Create kan inte längre falla på sin min-invariant för en fråga som kördes.
            // Kvarvarande failures speglar äkta validator-brott (fortsatt: capture:a inte).
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
}
