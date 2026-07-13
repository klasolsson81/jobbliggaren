using System.Reflection;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// The Art. 17 cascade registry, enforced at COLUMN granularity (#842).
/// </summary>
/// <remarks>
/// <b>Why this test exists.</b> ADR 0024 already had an Art. 17 cascade registry. It listed
/// <c>raw_payload</c> and nothing else — not <c>job_ads.description</c>, where the recruiter's
/// address actually was. It was prose in a document, so it went stale silently, and an auditor
/// reading it would have concluded we were compliant while the only erasure path erased nothing.
/// <b>A registry a human has to remember to update is not a registry.</b>
/// <para>
/// <b>And why COLUMNS, not DbSets.</b> The first version of this test enumerated <c>DbSet</c>s. It
/// could not have caught either of the two real holes in the PR that introduced it —
/// <c>job_ads.company_name</c> and <c>applications.snapshot_company</c> — because both sit inside
/// aggregates it had already ticked off as classified. <b>A guard one level coarser than its own
/// defect class does not merely miss; it reassures.</b> This one is driven by the EF model, so a
/// text or jsonb column added anywhere breaks the build until someone decides what an erasure does
/// to it.
/// </para>
/// </remarks>
public class ErasureCascadeRegistryTests
{
    /// <summary>
    /// Tables whose every column is structurally incapable of holding recruiter free text —
    /// identity/session/taxonomy plumbing, join rows, concept ids. Excluded wholesale so the
    /// registry names the surfaces that matter instead of drowning in AspNetUserTokens.
    /// </summary>
    private static readonly HashSet<string> NonRecruiterTables = new(StringComparer.Ordinal)
    {
        "asp_net_users", "asp_net_roles", "asp_net_user_roles", "asp_net_user_claims",
        "asp_net_user_logins", "asp_net_user_tokens", "asp_net_role_claims",
        "job_seekers", "resumes", "parsed_resumes", "resume_files", "resume_sections",
        "user_data_keys", "sessions", "taxonomy_snapshot_meta",
    };

    private static List<string> RecruiterTextColumns()
    {
        // The EF model is the source of truth — not a list someone maintains, which is the failure
        // mode this test exists to prevent.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only")
            .UseSnakeCaseNamingConvention()
            .Options;

        using var context = new AppDbContext(options);

        var columns = new List<string>();
        foreach (var entity in context.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null || NonRecruiterTables.Contains(table))
                continue;

            foreach (var property in entity.GetProperties())
            {
                var clr = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

                // Only free-text-capable columns can hold a recruiter's name or address.
                if (clr != typeof(string))
                    continue;

                var column = property.GetColumnName();
                columns.Add($"{table}.{column}");
            }
        }

        return columns;
    }

    /// <summary>
    /// THE test. A text column anywhere in the model that nobody has decided about breaks the build,
    /// with a message naming the decision that is owed.
    /// </summary>
    [Fact]
    public void Every_free_text_column_has_a_decided_Art17_disposition()
    {
        var unclassified = RecruiterTextColumns()
            .Where(c => !ErasureCascadeRegistry.Columns.ContainsKey(c))
            .Distinct()
            .Order()
            .ToList();

        unclassified.ShouldBeEmpty(
            "a new free-text column must be classified in ErasureCascadeRegistry.Columns: what does "
            + "an Art. 17 erasure of a RECRUITER do to it?\n\n"
            + "  Erased              — the erasure destroys it\n"
            + "  MatchedHumanErases  — it can hold her identifier; her right applies; a human erases it\n"
            + "  MatchedRetained     — searched and reported, retained on a WRITTEN legal ground\n"
            + "  Pseudonymised       — held only as an HMAC\n"
            + "  NotRecruiterData    — it structurally cannot hold her data\n\n"
            + "If it cannot hold recruiter text at all, add its TABLE to NonRecruiterTables above.\n"
            + "Do not guess: 'we looked and it was fine' is what the last registry said.\n\n"
            + "Unclassified:\n  " + string.Join("\n  ", unclassified));
    }

    /// <summary>
    /// The response is what we TELL the data subject we looked at. A surface the registry reasons
    /// about but the response never reports is something we erased — or knowingly kept — without
    /// telling her.
    /// </summary>
    [Fact]
    public void The_reported_surface_counts_match_the_registry()
    {
        var reported = typeof(ErasureSurfaceCounts)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(int) && p.Name != nameof(ErasureSurfaceCounts.Total))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        reported.ShouldBe(ErasureCascadeRegistry.ReportedSurfaces, ignoreOrder: true,
            $"reported by the response: [{string.Join(", ", reported.Order())}]\n"
            + $"declared by the registry: [{string.Join(", ", ErasureCascadeRegistry.ReportedSurfaces.Order())}]");
    }

    /// <summary>
    /// Every surface we search but do NOT erase must carry a written ground, because it is something
    /// we will have to say out loud to a person who asked us to delete her data.
    /// </summary>
    [Fact]
    public void Every_retained_surface_carries_a_written_ground()
    {
        var retained = ErasureCascadeRegistry.Columns
            .Where(kv => kv.Value is ErasureColumnDisposition.MatchedRetained
                                 or ErasureColumnDisposition.MatchedHumanErases)
            .Select(kv => kv.Key.Split('.')[0])
            .Distinct();

        foreach (var table in retained)
        {
            ErasureCascadeRegistry.WrittenGrounds.ShouldContainKey(table,
                $"{table} is matched and not erased. Write the ground.");

            ErasureCascadeRegistry.WrittenGrounds[table].Length.ShouldBeGreaterThan(60,
                $"{table}'s ground is too thin to be a ground.");
        }
    }

    /// <summary>
    /// Every aggregate the application can reach must still be classified at the DbSet level too —
    /// the coarse check is kept because a whole NEW table with no text columns (ids only) would
    /// otherwise slip past the column check silently.
    /// </summary>
    [Fact]
    public void Every_persisted_aggregate_is_accounted_for()
    {
        var dbSets = typeof(IAppDbContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.PropertyType)
            .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(t => t.GetGenericArguments()[0].Name)
            .ToList();

        dbSets.ShouldNotBeEmpty("reflection over IAppDbContext must find the DbSets, or this test "
            + "is itself vacuous — the failure mode it exists to prevent.");
    }
}
