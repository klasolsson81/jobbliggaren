using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Commands.RedactRecruiterPii;

/// <summary>
/// GDPR Art. 17 right-to-erasure-handler för rekryterar-PII i
/// <c>job_ads.raw_payload</c>. Per ADR 0032 §8 amendment 2026-05-13 +
/// ADR 0035 + senior-cto-advisor-decision 2026-05-13.
///
/// <para>
/// <b>Mekanik (CTO Q2 = total null-out):</b> delegerar till
/// <see cref="IRecruiterPiiPurger"/>-port (Infrastructure-impl) som söker
/// matchande JobAds via <c>EF.Functions.JsonContains</c> och null:ar
/// <c>raw_payload</c> via <c>ExecuteUpdateAsync</c>. Porten existerar för att
/// hålla Application Npgsql-fri (Clean Arch §2.1) — samma mönster som
/// <c>IAuditTrailEraser</c> (ADR 0024 D3).
/// </para>
///
/// <para>
/// <b>Audit-granularitet (CTO Q3 = aggregerad rad per request):</b> EN audit-rad
/// per command-anrop via standard <c>IAuditableCommand</c>-pipeline. AggregateId
/// = command.RequestId (per-request-Guid). Speglar ADR 0024 D4-precedens.
/// </para>
///
/// <para>
/// <b>Name-branch:</b> defererad till TD-75. Returnerar
/// <see cref="DomainError.Validation"/> med dokumenterad trigger.
/// </para>
/// </summary>
public sealed class RedactRecruiterPiiCommandHandler(IRecruiterPiiPurger purger)
    : ICommandHandler<RedactRecruiterPiiCommand, Result<int>>
{
    public async ValueTask<Result<int>> Handle(
        RedactRecruiterPiiCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Type == RecruiterIdentifierType.Name)
        {
            return Result.Failure<int>(DomainError.Validation(
                "RedactRecruiterPii.NameNotSupportedYet",
                "Name-baserad radering är defererad till TD-75. " +
                "Använd Email-typ tills vidare; manuell DB-procedur " +
                "dokumenterad i docs/runbooks/recruiter-pii-erasure.md."));
        }

        var rowsAffected = await purger.RedactByEmailAsync(command.Identifier, cancellationToken);

        return Result.Success(rowsAffected);
    }
}
