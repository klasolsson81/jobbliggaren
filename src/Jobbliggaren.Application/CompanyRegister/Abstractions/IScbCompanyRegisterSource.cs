namespace Jobbliggaren.Application.CompanyRegister.Abstractions;

/// <summary>
/// #560 (ADR 0091) — the SCB POPULATION channel port: streams every Swedish legal entity from SCB's
/// company register in cap-sized batches, so the orchestrator can bulk-upsert them into the local
/// <c>company_register</c> read-model. This is a DIFFERENT channel from the per-org.nr lookup port
/// <c>ICompanyRegistry</c> (ADR 0088): that answers one org.nr → name on demand (Redis-cached);
/// this replicates the whole register locally for browse/smart-watch. The two never share an
/// implementation (ADR 0091 forward-note: the legal-entities-only register must never back
/// <c>ICompanyRegistry.Lookup</c> — that would drop ADR 0090 D4's masked "Enskild Firma" hit).
///
/// <para>
/// <b>Port surface is BCL + Application ACL types only</b> (parity <c>IJobSource</c>): the
/// implementation lives in Infrastructure, loads the SCB client certificate from the Windows
/// cert-store by thumbprint, drives the adaptive count-then-slice partitioning (SCB caps a fetch at
/// 2000 rows and has no pagination), throttles to the process-wide 10-calls/10-s budget, and
/// translates the <c>hamtaforetag</c> wire rows into <see cref="ScbCompanyRecord"/>. Application
/// never sees the SCB wire format.
/// </para>
///
/// <para>
/// <b>Legal-entities-only at the source (ADR 0091, GDPR):</b> the implementation filters the SCB
/// query on Juridisk form ≠ 10 (excludes fysiska personer / enskild firma), so personnummer-shaped
/// rows are never even fetched. The orchestrator applies a second, independent
/// <c>IsPersonnummerShaped</c> exclusion before persisting.
/// </para>
/// </summary>
public interface IScbCompanyRegisterSource
{
    /// <summary>
    /// Streams all legal entities as cap-sized batches (each ≤ the SCB fetch cap). Writes partition
    /// counts and the truncated/errored verdict into <paramref name="outcome"/> as it goes — the
    /// orchestrator reads that verdict afterwards to decide whether the deregister sweep may run.
    /// A batch may be empty for a counted-but-empty partition; callers filter downstream.
    /// </summary>
    IAsyncEnumerable<IReadOnlyList<ScbCompanyRecord>> StreamLegalEntitiesAsync(
        ScbSyncOutcome outcome, CancellationToken cancellationToken);
}
