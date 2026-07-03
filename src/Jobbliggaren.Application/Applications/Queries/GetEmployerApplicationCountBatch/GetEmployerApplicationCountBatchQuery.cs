using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationCountBatch;

/// <summary>
/// #446 (#311; ADR 0087 D2 read-model; DPIA #456 / ADR 0090 D1, Art. 6(1)(b)) — the /jobb card badge
/// "Du har X tidigare ansökningar till detta företag". For a PAGE of job ads, returns how many of the
/// signed-in user's OWN submitted applications (<c>AppliedAt != null</c>) target each ad's employer
/// (same org.nr). A per-user OVERLAY keyed on JobAdId, the fourth in the /jobb overlay family
/// (parity <c>GetJobAdStatusBatchQuery</c> saved/applied + <c>GetJobAdMatchBatchQuery</c> tags): one
/// bounded round-trip per page, NEVER a count-per-card (N+1 on the hot path — ADR 0045 / CLAUDE.md
/// §2.5). Reuses #444's LOGIC (org.nr shadow column via <see cref="Jobbliggaren.Application.JobAds.Abstractions.IJobAdEmployerReader"/>
/// + the caller's <c>AppliedAt != null</c> history), NOT its endpoint.
/// <para>
/// <b>Owner-scoped (M2, IDOR).</b> The count is a scalar over the CALLER'S OWN rows only — the handler
/// resolves <c>JobSeekerId</c> from <see cref="ICurrentUser"/> (never the wire), enforced by
/// <see cref="IAuthenticatedRequest"/>. It can never include or imply another user's activity.
/// </para>
/// <para>
/// <b>GDPR (ADR 0087 D8 / ADR 0090 D1).</b> org.nr is read SERVER-SIDE only, as the GROUP key — the
/// response carries a plain <c>int</c> per JobAdId and NO org.nr (a sole-proprietorship org.nr can
/// equal a personnummer, CLAUDE.md §5), so there is no M1 surfacing and no personnummer to mask. Same
/// Art. 6(1)(b) submitted-only definition as #444.
/// </para>
/// </summary>
public sealed record GetEmployerApplicationCountBatchQuery(IReadOnlyList<Guid> JobAdIds)
    : IQuery<EmployerApplicationCountBatchDto>, IAuthenticatedRequest;
