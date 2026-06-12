using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.RecentJobSearches.Abstractions;
using JobbPilot.Application.RecentJobSearches.Common;
using JobbPilot.Domain.SavedSearches;
using Mediator;
using Microsoft.Extensions.Logging;

namespace JobbPilot.Application.RecentJobSearches.Behaviors;

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
        // Fas C2 (ADR 0067): guarden räknar alla fyra dimensioner — stänger
        // C1:s live-gap där yrkesgrupp-/kommun-only-sökningar inte fångades.
        var occupationGroupCount = capt.OccupationGroup?.Count ?? 0;
        var municipalityCount = capt.Municipality?.Count ?? 0;
        var regionCount = capt.Region?.Count ?? 0;
        if (string.IsNullOrWhiteSpace(capt.Q) && occupationGroupCount == 0
            && municipalityCount == 0 && regionCount == 0)
        {
            return response;
        }

        try
        {
            var criteriaResult = SearchCriteria.Create(
                occupationGroup: capt.OccupationGroup ?? [],
                municipality: capt.Municipality ?? [],
                region: capt.Region ?? [],
                q: capt.Q,
                sortBy: capt.SortBy);

            // Andra valideringsfel speglar query-validator-brott och bör inte
            // rendera capture (queryn bör då ha failat i ValidationBehavior).
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
