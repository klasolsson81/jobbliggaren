using System.Text.Json;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;

/// <summary>
/// GDPR Art. 17 — erase every ad we hold that mentions <paramref name="Identifier"/>
/// (ADR 0106 Tier B, #842). Admin-only. Destructive and irreversible for every user of those ads.
/// </summary>
/// <param name="RequestId">
/// Minted by the endpoint. It is the audited aggregate id — the audited thing is <b>the request</b>,
/// not the ads, because a request that matches nothing must still leave a record and there is no ad
/// id to hang it on.
/// </param>
/// <param name="Identifier">
/// Free text: an email, a phone number, or a NAME. One channel, no discriminator — TD-75's premise
/// was not outdated, it was falsified (the email is never a structured key in storage), so every
/// identifier is matched over free text either way. TD-75 is closed as void. See ADR 0106 D8.
/// </param>
/// <param name="DryRun">True ⇒ report what would be erased and write nothing.</param>
/// <param name="ConfirmedJobAdIds">
/// <b>The ads the operator actually reviewed</b> — required when <paramref name="DryRun"/> is false.
/// Not a count: a count cannot be reviewed. A recruiter named <i>Anna</i> substring-matches
/// <i>Johanna</i> and <i>Marianna</i> across thousands of ads, and an operator who reads "4127" and
/// retypes "4127" has reviewed nothing while destroying 4 127 ads. He sends back the ids he read.
/// Anything he did not confirm is not erased — and the response reports the gap.
/// </param>
public sealed record EraseRecruiterAdsCommand(
    Guid RequestId,
    string Identifier,
    bool DryRun,
    IReadOnlyList<Guid>? ConfirmedJobAdIds)
    : ICommand<Result<EraseRecruiterAdsResponse>>,
      IAdminRequest,
      IAuditableCommand<Result<EraseRecruiterAdsResponse>>,
      IAuditPayloadCommand<Result<EraseRecruiterAdsResponse>>
{
    public string EventType => "JobAd.RecruiterErasureRequested";

    public string AggregateType => "RecruiterErasureRequest";

    /// <summary>
    /// Record handler-rejected requests too (e.g. the 409 when the reviewed set has moved).
    /// </summary>
    /// <remarks>
    /// <b>Scope, stated precisely, because an over-claim here would be the very thing this issue is
    /// about.</b> <c>AuditBehavior</c> is the INNERMOST pipeline behavior, and
    /// <c>ValidationBehavior</c> / <c>AdminAuthorizationBehavior</c> both <i>throw</i> — outside it.
    /// So this opt-in records failures the HANDLER returns. It does <b>not</b> record a 400 (bad
    /// input) or a 403 (non-admin). Those are operator-side errors on an internal admin route, not
    /// refusals of a data subject's request: her request reaches us as an email to a human, and the
    /// Art. 12(3) record of a refusal is the controller's case file and the runbook — not this
    /// route's exception paths. The 403 is nevertheless worth having, and
    /// <c>AdminAuthorizationBehavior</c> records it separately.
    /// </remarks>
    public bool AuditFailures => true;

    /// <summary>
    /// The request's own id — deliberately NOT derived from the response, because
    /// <see cref="AuditFailures"/> means this is also called on a FAILED result, where there is no
    /// value to read.
    /// </summary>
    public Guid ExtractAggregateId(Result<EraseRecruiterAdsResponse> response) => RequestId;

    /// <summary>
    /// The accountability record (Art. 5(2)/30) — written to <c>audit_log.payload</c>, a jsonb
    /// column that has existed since ADR 0022 and that no command has ever written.
    /// </summary>
    /// <remarks>
    /// <b>The identifier is HMAC'd, never stored.</b> Recording the recruiter's email in the audit
    /// row for her own erasure request would make that request the last place her address survives.
    /// The pseudonymiser is handed in (never reached for), so there is exactly one route from an
    /// identifier into <c>audit_log</c> and it goes through HMAC-SHA256(server pepper). md5 is
    /// rejected: an unkeyed digest of an email is dictionary-reversible in milliseconds.
    /// </remarks>
    public string? BuildAuditPayload(
        Result<EraseRecruiterAdsResponse> response, IIdentifierPseudonymizer pseudonymizer)
    {
        ArgumentNullException.ThrowIfNull(pseudonymizer);

        var payload = new Dictionary<string, object?>
        {
            ["identifierHmac"] = pseudonymizer.Pseudonymize(Identifier),
            ["dryRun"] = DryRun,
            ["succeeded"] = response.IsSuccess,
        };

        if (response.IsSuccess)
        {
            var value = response.Value;
            payload["outcome"] = value.Outcome.ToString();
            payload["matched"] = SurfaceCounts(value.Matched);
            payload["erased"] = SurfaceCounts(value.Erased);
            payload["erasedExternalIds"] = value.ErasedExternalIds;
        }
        else
        {
            // The code only. The message may echo operator input, and this row is the one place we
            // have promised not to keep her identifier (CLAUDE.md §5).
            payload["errorCode"] = response.Error.Code;
        }

        return JsonSerializer.Serialize(payload);
    }

    private static Dictionary<string, int> SurfaceCounts(ErasureSurfaceCounts counts) => new()
    {
        ["jobAds"] = counts.JobAds,
        ["recentJobSearches"] = counts.RecentJobSearches,
        ["savedSearches"] = counts.SavedSearches,
        ["applicationSnapshots"] = counts.ApplicationSnapshots,
        ["userAuthoredText"] = counts.UserAuthoredText,
    };
}
