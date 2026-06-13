using System.Text.Json;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.JobSources;

/// <summary>
/// Postgres-implementation av <see cref="IRecruiterPiiPurger"/>. Använder
/// Npgsql-specifika <c>EF.Functions.JsonContains</c> för jsonb @>-sökning och
/// <c>ExecuteUpdateAsync</c> för in-place null-out av <c>raw_payload</c>.
///
/// Per ADR 0032 §8 amendment 2026-05-13 + ADR 0035 + CTO Q2 (total null-out).
///
/// <para>
/// <b>Probe-shape:</b> <c>{ "employer": { "contact_email": "&lt;email&gt;" } }</c>
/// matchar JobTech-payload-strukturen. Postgres @>-operator returnerar true om
/// probe-strukturen finns som sub-tree i target (oavsett andra fält). Email
/// normaliseras till lower-case (RFC 5321 local-part är formellt
/// case-sensitive men i praktiken case-insensitivt).
/// </para>
///
/// <para>
/// <b>EF Core 10 #3745-defensive:</b> <c>EF.Functions.JsonContains</c> är säker;
/// regressionen påverkar <c>.Contains()</c> på jsonb-mapped strings — vi använder
/// inte det.
/// </para>
/// </summary>
public sealed class RecruiterPiiPurger(IAppDbContext db) : IRecruiterPiiPurger
{
    public async Task<int> RedactByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var probe = BuildEmailProbe(email);

        // IgnoreQueryFilters: PII gäller även soft-deletade rader.
        return await db.JobAds
            .IgnoreQueryFilters()
            .Where(j => j.RawPayload != null
                        && EF.Functions.JsonContains(j.RawPayload, probe))
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.RawPayload, _ => (string?)null),
                cancellationToken);
    }

    private static string BuildEmailProbe(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return JsonSerializer.Serialize(new
        {
            employer = new { contact_email = normalized },
        });
    }
}
