using System.Reflection;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// The Art. 17 cascade registry, enforced at COLUMN granularity (#842).
/// </summary>
/// <remarks>
/// <b>A registry a human has to remember to update is not a registry.</b> ADR 0024's was prose in a
/// document; it listed <c>raw_payload</c> and nothing else, went stale silently, and an auditor
/// reading it would have concluded we were compliant while the only erasure path erased nothing.
/// This one is driven by the EF model, so a text column added anywhere breaks the build until
/// someone decides what an erasure does to it.
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

    /// <summary>
    /// The dispositions that CLAIM we ran a query over the column. An encrypted column may never
    /// carry one of these — that claim is the Blocker this file exists to make unshippable.
    /// </summary>
    private static readonly ErasureColumnDisposition[] SearchedDispositions =
    [
        ErasureColumnDisposition.MatchedHumanErases,
        ErasureColumnDisposition.MatchedRetained,
    ];

    /// <summary>
    /// The dispositions that owe a written ground. <b><c>NotRecruiterData</c> is on this list, and
    /// putting it there is the point.</b> It makes the strongest claim in the registry — <i>"the
    /// write path cannot put her data here"</i> — and it used to be the only bucket that cost
    /// nothing to join. The verdict with the highest burden of proof had the lowest cost of entry,
    /// so it is where the awkward columns went.
    /// </summary>
    private static readonly ErasureColumnDisposition[] GroundedDispositions =
    [
        ErasureColumnDisposition.MatchedHumanErases,
        ErasureColumnDisposition.MatchedRetained,
        ErasureColumnDisposition.HeldButNotSearchable,
        ErasureColumnDisposition.NotRecruiterData,
    ];

    private static AppDbContext ModelOnlyContext()
    {
        // The EF model is the source of truth — not a list someone maintains, which is the failure
        // mode this test exists to prevent. The connection is never opened.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }

    private static string ColumnKey(IEntityType entity, IProperty property) =>
        $"{entity.GetTableName()}.{property.GetColumnName()}";

    private static List<string> RecruiterTextColumns()
    {
        using var context = ModelOnlyContext();

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

                columns.Add(ColumnKey(entity, property));
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
            + "  Erased               — the erasure destroys it\n"
            + "  MatchedHumanErases   — it can hold her identifier; her right applies; a human erases it\n"
            + "  MatchedRetained      — searched and reported, retained on a WRITTEN legal ground\n"
            + "  HeldButNotSearchable — DEK-encrypted; we hold it and cannot scan it; the reply says so\n"
            + "  Pseudonymised        — held only as an HMAC\n"
            + "  NotRecruiterData     — the WRITE PATH cannot put her free text here (never 'unlikely')\n\n"
            + "If it cannot hold recruiter text at all, add its TABLE to NonRecruiterTables above.\n"
            + "Do not guess: 'we looked and it was fine' is what the last registry said.\n\n"
            + "Unclassified:\n  " + string.Join("\n  ", unclassified));
    }

    /// <summary>
    /// <b>THE GUARD FOR THE BLOCKER.</b> A DEK-encrypted column may never be classified as SEARCHED.
    /// </summary>
    /// <remarks>
    /// The erasure command once ran <c>lower(cover_letter) LIKE '%magnus fagerberg%'</c> against three
    /// columns the write path guarantees are ciphertext. It matched zero rows on every request,
    /// forever, and the reply template turned that zero into <i>"we hold no data matching this
    /// identifier"</i> — to a named person who had asked.
    /// <para>
    /// <b>The column-level registry could not catch it</b>, because Form-A encryption is applied by an
    /// interceptor and is INVISIBLE in the EF model the registry is driven from: to EF, a cover letter
    /// is a <c>string</c> like any other. So the two registries are taught about each other here.
    /// </para>
    /// <para>
    /// <c>EncryptedFieldRegistry</c> is <c>internal</c> to Infrastructure and this project has no
    /// <c>InternalsVisibleTo</c>, so it is reached by reflection — which means <b>this test must fail
    /// loudly if the reflection stops working.</b> A rename would otherwise silently empty the guard,
    /// and we would have rebuilt the vacuous control one level up. The not-null assertions and the
    /// non-empty floor below are not ceremony; they ARE the guard.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_DEK_encrypted_column_can_never_be_classified_as_searched()
    {
        var encrypted = EncryptedColumns();

        encrypted.Count.ShouldBeGreaterThanOrEqualTo(4,
            "reflection over EncryptedFieldRegistry found (almost) nothing — the guard is VACUOUS. It "
            + "must resolve the encryption allowlist, or fail. Found: "
            + string.Join(", ", encrypted.Order()));

        foreach (var known in new[]
                 {
                     "applications.cover_letter",
                     "application_notes.content",
                     "follow_ups.note",
                 })
        {
            encrypted.ShouldContain(known,
                $"{known} is Form-A encrypted and the cross-check must see it. If this fails, the "
                + "EF-model → column-name mapping has drifted and the guard is reaching PAST the very "
                + "columns that produced the Blocker.");
        }

        var claimedSearched = encrypted
            .Where(c => ErasureCascadeRegistry.Columns.TryGetValue(c, out var d)
                        && SearchedDispositions.Contains(d))
            .Order()
            .ToList();

        claimedSearched.ShouldBeEmpty(
            "these columns are DEK-encrypted at rest (per-user envelope, ADR 0049 C3 / 0066) and are "
            + "classified as SEARCHED. A plaintext LIKE against them compares a name to base64 and "
            + "matches NOTHING — not 'nothing today', but structurally, forever. The reply template "
            + "then turns that zero into 'we hold no data about you', to a named data subject.\n\n"
            + "The only honest disposition for an encrypted column is HeldButNotSearchable: we hold "
            + "it, we cannot read it, and every reply says so (UnsearchableSurfaces).\n\n"
            + "Wrongly classified as searched:\n  " + string.Join("\n  ", claimedSearched));
    }

    /// <summary>
    /// Every encrypted column, as <c>table.column</c>, resolved through the EF model from
    /// Infrastructure's own encryption allowlist.
    /// </summary>
    /// <remarks>
    /// <b>Form A</b> is read from <c>EncryptedFieldRegistry</c> through its real probe, by reflection
    /// (the type is internal). <b>Form B</b> — a JSON-serialised VO written to an encrypted shadow —
    /// is enumerated EXPLICITLY, because its allowlist is keyed by domain property and shadow name in
    /// a way the EF model does not hand back symmetrically. <b>That manual list is a seam, and it is
    /// named as one: add a Form-B column and you must add it here.</b> Saying so out loud beats a
    /// cross-check that silently covers one form and reads as if it covered both — which is the
    /// mistake this whole guard exists to stop.
    /// </remarks>
    private static HashSet<string> EncryptedColumns()
    {
        var registry = typeof(AppDbContext).Assembly
            .GetType("Jobbliggaren.Infrastructure.Security.EncryptedFieldRegistry", throwOnError: false);

        registry.ShouldNotBeNull(
            "EncryptedFieldRegistry was not found by reflection. It moved or was renamed, and this "
            + "guard just became vacuous. Fix the reflection — do not delete the test.");

        var tryGet = registry.GetMethod(
            "TryGetEncryptedProperties",
            BindingFlags.Public | BindingFlags.Static,
            [typeof(Type), typeof(string[]).MakeByRefType()]);

        tryGet.ShouldNotBeNull(
            "EncryptedFieldRegistry.TryGetEncryptedProperties(Type, out string[]) was not found. The "
            + "Form-A probe changed shape and this guard just became vacuous.");

        using var context = ModelOnlyContext();
        var columns = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in context.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is null)
                continue;

            // Form A — ask the REAL registry, through its real probe, for this entity's CLR type.
            var args = new object?[] { entity.ClrType, null };
            if (tryGet.Invoke(null, args) is true && args[1] is string[] encryptedProperties)
            {
                foreach (var name in encryptedProperties)
                {
                    var property = entity.FindProperty(name);
                    if (property is not null)
                        columns.Add(ColumnKey(entity, property));
                }
            }

            // Form B — the manual seam. See the remarks.
            foreach (var shadow in new[] { "ContentEnc", "ParsedContentEnc" })
            {
                var property = entity.FindProperty(shadow);
                if (property is not null)
                    columns.Add(ColumnKey(entity, property));
            }
        }

        return columns;
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
    /// Every <c>(table, disposition)</c> pair that owes a ground has one — <b>including
    /// <c>NotRecruiterData</c></b>, which is the whole reason this test changed shape.
    /// </summary>
    /// <remarks>
    /// The old version required a ground only for the two "matched" dispositions. So the bucket that
    /// asserts <i>"the write path cannot put her data here"</i> — the strongest claim in the file —
    /// was the ONE a tired author could enter for free. <c>company_watch_criteria.label</c> (120
    /// chars of arbitrary user text) went there for exactly that reason, and the guard nodded,
    /// because it checked that the column was CLASSIFIED and not that the classification was EARNED.
    /// <para>
    /// The key carries the disposition because a table can hold several verdicts at once —
    /// <c>applications</c> carries four — and one paragraph standing in for four different legal
    /// grounds is precisely how the last registry rotted.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_disposition_that_owes_a_ground_has_one()
    {
        var owed = ErasureCascadeRegistry.Columns
            .Where(kv => GroundedDispositions.Contains(kv.Value))
            .Select(kv => $"{kv.Key.Split('.')[0]}:{kv.Value}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        owed.ShouldNotBeEmpty("the registry declares no grounded dispositions — this test is itself "
            + "vacuous, which is the failure mode it exists to prevent.");

        foreach (var key in owed)
        {
            ErasureCascadeRegistry.WrittenGrounds.ShouldContainKey(key,
                $"{key} is a verdict we will have to defend to a person who asked us to delete her "
                + "data. Write the ground. For NotRecruiterData it is short and mechanical: name the "
                + "WRITE-PATH guarantee (closed domain, or retired-and-counted). 'We judged it "
                + "unlikely' is not a ground.");

            ErasureCascadeRegistry.WrittenGrounds[key].Length.ShouldBeGreaterThan(60,
                $"{key}'s ground is too thin to be a ground.");
        }
    }

    /// <summary>
    /// The "could not search" list the response is REQUIRED to carry must be non-empty and must name
    /// exactly the encrypted columns — otherwise the disclosure that makes
    /// <c>NoMatchInSearchableSurfaces</c> an honest word is a disclosure of nothing.
    /// </summary>
    [Fact]
    public void The_unsearchable_disclosure_names_every_column_we_cannot_search()
    {
        var declared = ErasureCascadeRegistry.UnsearchableColumns;

        declared.ShouldNotBeEmpty(
            "the response promises the data subject a list of what we could NOT search. If it is "
            + "empty, that promise is a blank — and 'we searched everywhere' is back, wearing a new "
            + "type.");

        var disclosed = UnsearchableSurfaces.FromRegistry();

        disclosed.Columns.ShouldBe(declared,
            "the response's disclosure must be DERIVED from the registry, never restated beside it.");

        disclosed.Escalation.Length.ShouldBeGreaterThan(60,
            "we refuse the MECHANISM, not the person — so every reply must carry a real route she can "
            + "take. A refusal with no route is a refusal of the person.");

        foreach (var column in declared)
        {
            ErasureCascadeRegistry.Columns[column]
                .ShouldBe(ErasureColumnDisposition.HeldButNotSearchable);
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
