namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Application-port för GDPR Art. 17 right-to-erasure av rekryterar-PII i
/// <c>job_ads.raw_payload</c>. Implementeras i Infrastructure-lagret eftersom
/// jsonb-sökning kräver provider-specifik LINQ (<c>EF.Functions.JsonContains</c>
/// är Npgsql-specifik och bryter Clean Arch om den exponeras i Application).
///
/// Per ADR 0032 §8 amendment 2026-05-13 + ADR 0035 + senior-cto-advisor-decision
/// 2026-05-13 Q2 (total null-out via ExecuteUpdateAsync).
/// </summary>
public interface IRecruiterPiiPurger
{
    /// <summary>
    /// Null:ar <c>raw_payload</c> på alla JobAds där matchande rekryterar-email
    /// finns i jsonb-strukturen <c>{ employer: { contact_email: ... } }</c>.
    /// <c>IgnoreQueryFilters</c> anropas fortfarande i impl:n men är sedan #821 en NO-OP:
    /// JobAd har inget query-filter kvar att ignorera (den döda soft-delete-axeln är retirerad).
    /// Anropet lämnas orört här — <c>RecruiterPiiPurger</c> skrivs om av #842 (Art. 17-vägen är
    /// en strukturell no-op) och den lanen äger filen.
    /// Idempotent — repeated runs ger 0 rader vid andra körning (raw_payload är
    /// då redan null).
    /// </summary>
    /// <returns>Antal rader påverkade.</returns>
    Task<int> RedactByEmailAsync(string email, CancellationToken cancellationToken);
}
