using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Companies.Queries.LookupCompany;

/// <summary>
/// #454 (ADR 0088) — look up ONE company by org.nr against the national registry
/// (<see cref="Abstractions.ICompanyRegistry"/>) and enrich the hit with our own corpus/user context
/// (active-ad count, matching-ad count, follow state — ADR 0088 D5, the F3(B) enriched single
/// endpoint: the fragments are consumed atomically by the /foretag lookup card). The org.nr arrives
/// from the request BODY, never a URL (ADR 0087 D8(c) — a sole-prop org.nr can equal a personnummer
/// and must never reach an access log). Auth-gated (<see cref="IAuthenticatedRequest"/> + endpoint
/// RequireAuthorization); reads PUBLIC registry/corpus data plus the CURRENT user's own follow state
/// — no cross-user surface. Deterministic, no AI/LLM (ADR 0071).
///
/// <para>
/// <b>Refuse-posture (ADR 0088 D4; Klas 2026-07-02 "Refuse i v1 + #456 avgör"):</b> a
/// personnummer-shaped org.nr is refused in the HANDLER (policy, not format — the validator only
/// checks shape) with a Validation-class error, BEFORE any registry transmission, caching or
/// surfacing. Revisiting that posture (enskild-firma searchability — Klas product directive) is the
/// headline question of DPIA #456, not a build-time decision here.
/// </para>
/// </summary>
public sealed record LookupCompanyQuery(string OrganizationNumber)
    : IQuery<Result<CompanyLookupDto>>, IAuthenticatedRequest
{
    /// <summary>
    /// REDACTED (#883). The org.nr is client-supplied input and IS the entire payload, so a record's
    /// compiler-generated <c>ToString()</c> would write it into a log for a plain <c>{X}</c> MEL
    /// placeholder — and it can be a sole-prop personnummer (ADR 0087 D8(c); CLAUDE.md §5;
    /// <c>LoggingBehavior</c> logs the message TYPE name only, but this closes the direct route too).
    /// Nothing but the type name is safe to keep; pinned by <c>OrgNrRecordLoggingGuardTests</c>.
    /// </summary>
    public override string ToString() => "LookupCompanyQuery(org.nr redacted)";
}
