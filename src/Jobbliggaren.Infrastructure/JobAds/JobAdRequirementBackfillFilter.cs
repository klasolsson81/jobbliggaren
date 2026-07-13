using System.Linq.Expressions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// Postgres-implementation av <see cref="IJobAdRequirementBackfillFilter"/> (F4-4b).
/// Kapslar in den Npgsql-specifika <c>jsonb ?</c>-existens-operatorn
/// (<c>EF.Functions.JsonExists</c>) i Infrastructure så Application-lagret förblir
/// Npgsql-fritt (CLAUDE.md §2.1; layer-arch-testet förbjuder Npgsql i Application).
/// Exakt samma inkapslings-mönster som <c>IDbExceptionInspector</c>. (Tidigare
/// citerades även <c>RecruiterPiiPurger</c> här som förebild; den är borttagen i
/// #842 — den var en Art. 17-raderingsväg som strukturellt inte kunde radera något.
/// Inkapslings-mönstret överlever, förebilden gör det inte.)
///
/// <para>
/// <b>EF Core 10 #3745-defensive:</b> <c>EF.Functions.JsonExists</c> är säker;
/// regressionen påverkar <c>.Contains()</c> på jsonb-mapped strings — vi använder
/// inte det. Predikatet plockar imported rader vars <c>raw_payload</c> saknar
/// <c>must_have</c>-nyckeln (importerade före F4-4b:s POCO-expansion); en re-ingestad
/// rad bär nyckeln och exkluderas → restart-idempotent, precist (ingen full svep).
/// </para>
/// </summary>
public sealed class JobAdRequirementBackfillFilter : IJobAdRequirementBackfillFilter
{
    public Expression<Func<JobAd, bool>> RowsMissingRequirements =>
        j => j.RawPayload != null && !EF.Functions.JsonExists(j.RawPayload, "must_have");
}
