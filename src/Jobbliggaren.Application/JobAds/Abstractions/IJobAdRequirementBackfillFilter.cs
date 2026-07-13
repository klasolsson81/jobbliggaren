using System.Linq.Expressions;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Application port (F4-4b) supplying the re-ingest predicate for the
/// requirements backfill — the rows whose <c>raw_payload</c> predates the
/// <c>must_have</c> POCO expansion and therefore must be re-fetched.
/// <para>
/// The predicate needs the Npgsql <c>jsonb ?</c> existence operator
/// (<c>EF.Functions.JsonExists</c>), which lives ONLY in the Npgsql package — it
/// must not be referenced from the Application layer (CLAUDE.md §2.1; the layer
/// arch test forbids Npgsql in Application). So, exactly as
/// <c>IDbExceptionInspector</c> does, the Npgsql-specific LINQ is encapsulated in
/// the Infrastructure implementation behind this Application-owned port, and the
/// <c>BackfillJobAdRequirementsJob</c> consumes only the abstraction. The base
/// <c>EF.Property</c> shadow-column predicates used by the ssyk/Klass2 backfills
/// are fine inline (base EF Core); a jsonb-key predicate is not, hence this port.
/// </para>
/// </summary>
public interface IJobAdRequirementBackfillFilter
{
    /// <summary>
    /// Selects imported <see cref="JobAd"/>s whose <c>raw_payload</c> lacks the
    /// <c>must_have</c> key (ingested before the F4-4b POCO expansion). Once a row
    /// is re-ingested the key is present and the predicate excludes it →
    /// restart-idempotent, precise (no full unconditional sweep).
    /// </summary>
    Expression<Func<JobAd, bool>> RowsMissingRequirements { get; }
}
