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
/// Generated per request by the endpoint. It is the audited aggregate id — the audited thing is
/// <b>the request</b>, not the ads, because a request that matches nothing must still leave a
/// record (Art. 12(3)) and there is no ad id to hang it on.
/// </param>
/// <param name="Identifier">
/// Free text: an email, a phone number, or a NAME. One channel, no discriminator — TD-75's
/// premise ("email är primär rekryterar-identifier i JobTech-payloads") was not outdated, it was
/// <b>falsified</b>: the sanitizer and the wire POCO guarantee the email is never a structured key
/// in storage, so every identifier is matched over free text either way. A discriminator that
/// changes the query would be a distinction without a difference, and a place for the next bug to
/// hide. <b>TD-75 is closed as void.</b>
/// </param>
/// <param name="DryRun">
/// True ⇒ report what would be erased and write nothing.
/// </param>
/// <param name="ConfirmedJobAdCount">
/// <b>This is what makes the dry run MANDATORY, in code rather than in a runbook sentence.</b> A
/// destructive call must state how many ads the operator saw in the dry run. If it does not match
/// the live count, the command refuses with a Conflict. So: you cannot erase without having looked
/// (the field is required when <c>DryRun</c> is false), and you cannot erase a set that changed
/// under you between looking and confirming — the nightly sync ingests continuously, so that race
/// is real, not theoretical. Optimistic concurrency on the one operation that destroys content for
/// every user. Required when <c>DryRun</c> is false; ignored otherwise.
/// </param>
public sealed record EraseRecruiterAdsCommand(
    Guid RequestId,
    string Identifier,
    bool DryRun,
    int? ConfirmedJobAdCount)
    : ICommand<Result<EraseRecruiterAdsResponse>>,
      IAdminRequest,
      IAuditableCommand<Result<EraseRecruiterAdsResponse>>,
      IAuditPayloadCommand<Result<EraseRecruiterAdsResponse>>
{
    public string EventType => "JobAd.RecruiterErasureRequested";

    public string AggregateType => "RecruiterErasureRequest";

    /// <summary>
    /// Record REJECTED requests too. A rights request that is refused and leaves no trace is an
    /// Art. 12(3) exposure — we owe the data subject the reasons we did not act and her right to
    /// complain, and we cannot produce either from a row we never wrote. Today
    /// <c>AuditBehavior</c> skips audit on failure; this opt-in is why it no longer does here.
    /// </summary>
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
    /// row for her erasure request would make that request the last place her address survives.
    /// The pseudonymiser is handed in (never reached for), so there is exactly one route from an
    /// identifier into <c>audit_log</c> and it goes through HMAC-SHA256(server pepper). md5 is
    /// rejected: an unkeyed digest of an email is dictionary-reversible in milliseconds — a fig
    /// leaf, not a pseudonym.
    /// <para>
    /// <c>erasedExternalIds</c> are Arbetsförmedlingen's public ad identifiers, not personal data,
    /// and they are what lets an auditor verify the erasure actually happened. Failures record the
    /// error code — never the identifier, never the message (which could echo input).
    /// </para>
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
            payload["matched"] = new Dictionary<string, int>
            {
                ["jobAds"] = value.Matched.JobAds,
                ["recentJobSearches"] = value.Matched.RecentJobSearches,
                ["savedSearches"] = value.Matched.SavedSearches,
            };
            payload["erased"] = new Dictionary<string, int>
            {
                ["jobAds"] = value.Erased.JobAds,
                ["recentJobSearches"] = value.Erased.RecentJobSearches,
                ["savedSearches"] = value.Erased.SavedSearches,
            };
            payload["erasedExternalIds"] = value.ErasedExternalIds;
        }
        else
        {
            // The code only. The message may echo operator input, and this row is the one place
            // we have promised not to keep her identifier (CLAUDE.md §5).
            payload["errorCode"] = response.Error.Code;
        }

        return JsonSerializer.Serialize(payload);
    }
}
