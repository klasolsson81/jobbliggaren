using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Common;
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
/// användare, (4) <see cref="RecentSearchCaptureGate"/> vägrar sökningen (tom/invalid filter,
/// personnummer, religiöst yrke — #2199 PR 2b).</para>
/// </summary>
public sealed partial class RecentJobSearchCaptureBehavior<TMessage, TResponse>(
    ICurrentUser currentUser,
    IRecentJobSearchCapturer capturer,
    RecentSearchCaptureGate gate,
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
        // Additiv till default-browse-guarden i RecentSearchCaptureGate, ersätter den inte:
        // en commit på tom sökning capture:as fortfarande aldrig.
        if (!capt.Commit)
            return response;

        if (response is not IRecentSearchCaptureResponse capResp)
            return response;

        if (currentUser.UserId is not { } userId)
            return response;

        // #2199 PR 2b — every rule about WHAT may be stored lives in RecentSearchCaptureGate, the one
        // predicate the list response also answers, so the hero offers "Spara sökningen" exactly when
        // this would store. What stays here is whether THIS request stores: the commit flag, the
        // response marker and the user, above.
        if (gate.Admit(capt) is not { } criteria)
            return response;

        try
        {
            await capturer
                .CaptureAsync(userId, criteria, capResp.TotalCount, cancellationToken)
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
