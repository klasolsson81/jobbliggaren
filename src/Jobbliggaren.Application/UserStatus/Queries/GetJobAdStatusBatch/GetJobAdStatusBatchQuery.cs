using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.UserStatus.Queries.GetJobAdStatusBatch;

/// <summary>
/// ADR 0063 — batch-resolver för "vilka av dessa JobAds har inloggad
/// JobSeeker sparat / ansökt på". Hot-path för `/jobb`-listan (20 kort/page);
/// max 100 IDs per anrop (validator). Anonyma users → tomma listor (no
/// 401-friktion på publik söksida — FE kan kalla utan auth-precheck och
/// branchar på tom respons).
/// </summary>
public sealed record GetJobAdStatusBatchQuery(IReadOnlyList<Guid> JobAdIds)
    : IQuery<JobAdStatusBatchDto>;
