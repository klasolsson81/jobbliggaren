using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCompanyWatchStatusByOrgNrBatch;

/// <summary>
/// #560 company-search wave PR-C (CTO F3, ADR 0087 D8(c)) — the ORG.NR-KEYED sibling of
/// <see cref="GetCompanyWatchStatusBatch.GetCompanyWatchStatusBatchQuery"/>. Where that one resolves each
/// requested JobAdId's employer org.nr server-side (via <c>IJobAdEmployerReader</c>), the
/// <c>/foretag/sok</c> register-search caller ALREADY holds each row's unmasked org.nr, so the org.nrs
/// arrive directly in the body — no job-ad hop. Answers "which of these companies does the current user
/// already follow" so the search results can render an honest "Bevakar" vs "Bevaka" per row.
///
/// <para>
/// <b>Composed at the RSC edge, never a server-side join (DPIA C-D4/M-C5 firewall).</b> This query reads
/// ONLY <c>company_watches</c> (the private follow graph); the register search reads ONLY
/// <c>company_register</c> (firewalled off <c>IAppDbContext</c>). The FE runs both and merges — enriching
/// the register row server-side was the rejected Approach (a).
/// </para>
///
/// <para>
/// <b>The response is POSITIONAL, 1:1 with <see cref="OrganizationNumbers"/> (no dedup, input order
/// preserved).</b> The caller keys by org.nr, but the response must NOT echo an org.nr back — a sole-prop
/// org.nr can be a personnummer, and a raw org.nr on a Mediator-response-reachable DTO would trip
/// <c>OrganizationNumberSurfacingGuardTests</c>. Positional alignment lets the FE zip by index while the
/// response stays org.nr-free (see <see cref="CompanyWatchStatusByOrgNrBatchDto"/>).
/// </para>
///
/// <para>
/// <b>Auth-gated + owner-scoped:</b> follow-state is per-user-private, so this is an
/// <see cref="IAuthenticatedRequest"/> (anon → 401; the handler also returns empty defensively). The
/// <c>OrganizationNumbers</c> is CLIENT-SUPPLIED input (classified <c>InboundOrgNrRequests</c>); the
/// redacting <see cref="ToString"/> keeps it out of logs (#883, <c>OrgNrRecordLoggingGuardTests</c>).
/// Max 100 org.nrs per call (validator; a search page is 20).
/// </para>
/// </summary>
public sealed record GetCompanyWatchStatusByOrgNrBatchQuery(IReadOnlyList<string> OrganizationNumbers)
    : IQuery<CompanyWatchStatusByOrgNrBatchDto>, IAuthenticatedRequest
{
    // #883 (ADR 0087 D8(c)) — an org.nr can be a sole-prop personnummer; MEL renders a record through
    // ToString(), so redact the list and keep only the non-PII count.
    public override string ToString() => $"GetCompanyWatchStatusByOrgNrBatchQuery(Count={OrganizationNumbers.Count})";
}
