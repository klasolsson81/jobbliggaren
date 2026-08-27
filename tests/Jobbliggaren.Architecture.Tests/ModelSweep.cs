using System.Reflection;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// The ONE model sweep, shared by the two column-granularity registries: the Art. 17 erasure
/// cascade (<see cref="ErasureCascadeRegistryTests"/>) and the mapped plaintext exposure
/// (<see cref="MappedPlaintextExposureRegistryTests"/>).
/// </summary>
/// <remarks>
/// <b>Why this is one implementation and not two.</b> "What counts as a text-bearing column in this
/// model" is a single piece of knowledge. The sweep it feeds had four holes over five rounds, every
/// one of them a CLR shape nobody thought of while the column stayed text, and each was closed
/// HERE — in <see cref="IsTextBearingStoreType"/> and the <c>.ToJson()</c> branch. A second copy is
/// a place for those closures to drift apart, and the file this was extracted from says so in its
/// own words: <i>"a second copy of the .ToJson() branch below is a place for those answers to drift
/// apart."</i>
/// <para>
/// <b>The two entry points are NOT interchangeable, and the difference is a different data
/// subject.</b> <see cref="AppModelTextColumnsByTable"/> sweeps <c>AppDbContext</c> alone — the
/// reach the Art. 17 cascade has always had. <see cref="AllModelsTextColumnsByTable"/> unions
/// <c>AppIdentityDbContext</c> into it, because a <c>pg_dump</c> does not stop at a DbContext
/// boundary: it carries every schema in the database, and <c>AspNetUsers</c> holds the email and
/// the name that two of the oldest delivered enumeration's four entries name.
/// </para>
/// </remarks>
internal static class ModelSweep
{
    /// <summary>
    /// The app model alone — the Art. 17 cascade's reach, unchanged by the extraction.
    /// </summary>
    internal static Dictionary<string, List<string>> AppModelTextColumnsByTable() =>
        TextColumnsByTable(AppModelEntities());

    /// <summary>
    /// Both models — what a <c>pg_dump</c> actually carries.
    /// </summary>
    /// <remarks>
    /// <b>The key form stays <c>table.column</c> and does NOT gain a schema segment.</b> Two
    /// hundred-odd keys in <c>ErasureCascadeRegistry</c> are written in that form, and re-keying
    /// them would be a large silent rewrite of a GDPR artefact to buy a qualifier nothing has
    /// needed yet. The risk that form carries — one table name in two schemas, silently collapsing
    /// two columns into one key — is therefore not argued away, it is <b>asserted</b>: see
    /// <see cref="AssertNoCrossSchemaTableCollision"/>, which this method calls on every sweep.
    /// </remarks>
    internal static Dictionary<string, List<string>> AllModelsTextColumnsByTable() =>
        TextColumnsByTable(AllEntities());

    /// <summary>
    /// The union, with the key form's precondition asserted. <b>Every union sweep goes through
    /// here</b> — the guard belongs to the KEY FORM, not to one call path, and it was reachable
    /// from only one of the two before (dotnet-architect NTH 1 / code-reviewer Minor 7).
    /// </summary>
    private static IReadOnlyList<IEntityType> AllEntities()
    {
        AssertNoCrossSchemaTableCollision();
        return [.. AppModelEntities(), .. IdentityModelEntities()];
    }

    /// <summary>
    /// Every table name is unique ACROSS the two models, so a <c>table.column</c> key means exactly
    /// one column.
    /// </summary>
    /// <remarks>
    /// Measured 2026-08-27: no collision. The app model's tables are unprefixed; Identity's are
    /// <c>AspNetUsers</c>, <c>AspNetRoles</c>, <c>AspNetUserClaims</c>, <c>AspNetUserLogins</c>,
    /// <c>AspNetUserRoles</c>, <c>AspNetUserTokens</c>, <c>AspNetRoleClaims</c> — <b>PascalCase</b>,
    /// in schema <c>identity</c>, because Identity names them with an explicit <c>ToTable()</c> that
    /// <c>UseSnakeCaseNamingConvention()</c> does not rewrite. <b>That is a measurement, not a
    /// guarantee</b> — nothing stops a future
    /// <c>identity.users</c> beside an app-side <c>users</c>. If this fails, do not widen the key
    /// form here: the two registries key against it and would both need re-keying in the same
    /// change.
    /// </remarks>
    private static void AssertNoCrossSchemaTableCollision()
    {
        var appTables = TableNames(AppModelEntities());
        var identityTables = TableNames(IdentityModelEntities());
        var shared = appTables.Intersect(identityTables, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        shared.ShouldBeEmpty(
            "a table name exists in BOTH AppDbContext and AppIdentityDbContext, so the "
            + "`table.column` key form no longer identifies one column and the union sweep would "
            + "silently merge two different columns under one key. Collisions: "
            + string.Join(", ", shared));
    }

    private static HashSet<string> TableNames(IReadOnlyList<IEntityType> entities) =>
        [.. entities.Select(e => e.GetTableName()).Where(t => t is not null).Select(t => t!)];

    /// <summary>
    /// The app model, as a context. The EF model is the source of truth — not a list someone
    /// maintains, which is the failure mode both registries exist to prevent. <b>The connection is
    /// never opened.</b>
    /// </summary>
    internal static AppDbContext AppModelContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }

    private static IReadOnlyList<IEntityType> AppModelEntities()
    {
        using var context = AppModelContext();
        return [.. context.Model.GetEntityTypes()];
    }

    /// <summary>
    /// ASP.NET Identity's model. Mirrors the real registration in
    /// <c>Infrastructure/DependencyInjection.cs</c>: Npgsql + snake_case, and the context's own
    /// <c>OnModelCreating</c> supplies <c>HasDefaultSchema("identity")</c>.
    /// </summary>
    private static IReadOnlyList<IEntityType> IdentityModelEntities()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only")
            .UseSnakeCaseNamingConvention()
            .Options;

        using var context = new AppIdentityDbContext(options);
        return [.. context.Model.GetEntityTypes()];
    }

    internal static string ColumnKey(IEntityType entity, IProperty property) =>
        $"{entity.GetTableName()}.{property.GetColumnName()}";

    /// <summary>
    /// True when a Postgres store type can carry text. <b>The sweep enumerates FORMS, not
    /// instances</b> (round-5 security M1): the STORE type is the mapping's own word for what a
    /// column can hold, and it is invariant under every CLR-side disguise.
    /// </summary>
    /// <remarks>
    /// Every earlier version of this filter was CLR-typed, and every hole it had was a CLR shape
    /// nobody thought of while the column stayed text: <c>string</c> was the first cut,
    /// <c>byte[]</c> (the CV file) was missed once, <c>IEnumerable&lt;string&gt;</c> → <c>text[]</c>
    /// (top_skills, employer_list) was missed once, and a <c>HasConversion</c> property (CLR type
    /// <c>SearchCriteria</c>, column <c>jsonb</c> — the user's free-text <c>q</c> inside
    /// <c>saved_searches.criteria</c>) was invisible for three rounds. Deriving from
    /// <c>GetColumnType()</c> kills the whole class: a value converter, an array mapping, a
    /// SmartEnum, a tsvector — they all land on a store type, and the store type cannot lie about
    /// whether Postgres will hold text in it.
    /// </remarks>
    internal static bool IsTextBearingStoreType(string storeType)
    {
        var t = storeType.Trim().ToLowerInvariant();

        // Arrays of a text-bearing type bear text ("text[]", "character varying(400)[]").
        while (t.EndsWith("[]", StringComparison.Ordinal))
            t = t[..^2];

        // Strip the length facet ("character varying(200)" → "character varying").
        var paren = t.IndexOf('(');
        if (paren > 0)
            t = t[..paren].TrimEnd();

        return t is "text" or "citext" or "json" or "jsonb" or "xml"
            or "character varying" or "varchar" or "character" or "char"
            or "bytea"      // a document IS text at rest — resume_files.content taught us that
            or "tsvector";  // derived text is still text — job_ads.search_vector is FTS-searched
    }

    /// <summary>
    /// The app model's DEK-encrypted columns — <b>the Art. 17 cascade's reach, unchanged by the
    /// extraction.</b>
    /// </summary>
    /// <remarks>
    /// <b>This exists because the union nearly ate a boundary.</b> The first cut of the extraction
    /// gave this one entry point over BOTH models, on the reasoning that sweeping Identity anyway
    /// keeps its emptiness a measurement. But its consumer is
    /// <c>Every_DEK_encrypted_column_carries_EXACTLY_ONE_disposition_HeldButNotSearchable</c>, which
    /// requires every column in the set to be classified in <c>ErasureCascadeRegistry</c> — an
    /// artefact that excludes ASP.NET Identity wholesale by its own ground. The first DEK-encrypted
    /// Identity column would have failed that fact with <c>ABSENT from the registry entirely</c>,
    /// and the only green path would have been to write an Identity column into the recruiter
    /// cascade's register. A refactor would have changed what a DIFFERENT GDPR artefact must
    /// contain. It read green only because nothing in Identity is encrypted — <b>unchanged by
    /// accident, not by a boundary</b> (dotnet-architect Viktigt 1).
    /// </remarks>
    internal static HashSet<string> AppModelEncryptedColumns() => EncryptedIn(AppModelEntities());

    /// <summary>
    /// Both models' DEK-encrypted columns. <b>Here</b> the union is right: a <c>pg_dump</c> does not
    /// stop at a DbContext boundary, so sweeping Identity anyway is what keeps its emptiness a
    /// measurement rather than an assumption.
    /// </summary>
    internal static HashSet<string> AllModelsEncryptedColumns() => EncryptedIn(AllEntities());

    /// <summary>
    /// Every DEK-encrypted column among the given entities, as <c>table.column</c>, resolved through
    /// the EF model from Infrastructure's own encryption allowlist.
    /// </summary>
    /// <remarks>
    /// <b>Form A</b> is read from <c>EncryptedFieldRegistry</c> through its real probe, by
    /// reflection (the type is internal). <b>Form B</b> — a JSON-serialised VO written to an
    /// encrypted shadow — and <b>Form C</b> — the sealed binary store, deliberately absent from the
    /// registry because its read path is streaming and never engages the materialisation
    /// interceptor — are enumerated BY HAND.
    /// <para>
    /// <b>Those two manual lists are a seam, and both consumers must read it the same way.</b> Add a
    /// Form-B or Form-C column and you must add it here. For the Art. 17 cascade the seam fails
    /// LOUD: a DEK-encrypted column missing from this set is caught by
    /// <c>Every_DEK_encrypted_column_carries_EXACTLY_ONE_disposition_HeldButNotSearchable</c>. For
    /// the backup registry it fails <b>SAFE but SILENT</b>: a missed encrypted column presents as
    /// apparent plaintext and must then be declared as exposed — an overstatement of what a restore
    /// leaks, never an understatement.
    /// </para>
    /// <para>
    /// ⚠ <b>The seam has one direction that is NOT fail-safe, and it is written here rather than
    /// left for someone to find</b> (code-reviewer Minor 9). The Form-B/C probe matches on a BARE
    /// PROPERTY NAME against every entity in the sweep, with no check that the property is actually
    /// encrypted. A future UNencrypted property named <c>ContentEnc</c>, <c>ParsedContentEnc</c> or
    /// <c>SealedContent</c> is subtracted from the plaintext set in silence — and
    /// <c>Every_classified_column_still_exists_in_a_model</c> would then instruct, in its own failure
    /// message, that the entry be DELETED. That is the one path where this blindness understates the
    /// exposure. It is unlikely (a new Form-B shadow is named for its own field, not one of these
    /// three) and it is deliberately not closed here: closing it means asking the registry whether a
    /// property is encrypted, which is the question this probe exists to answer.
    /// </para>
    /// </remarks>
    private static HashSet<string> EncryptedIn(IReadOnlyList<IEntityType> entities)
    {
        var registry = typeof(AppDbContext).Assembly
            .GetType("Jobbliggaren.Infrastructure.Security.EncryptedFieldRegistry", throwOnError: false);

        registry.ShouldNotBeNull(
            "EncryptedFieldRegistry was not found by reflection. It moved or was renamed, and both "
            + "registries that read this just became vacuous. Fix the reflection — do not delete it.");

        var tryGet = registry.GetMethod(
            "TryGetEncryptedProperties",
            BindingFlags.Public | BindingFlags.Static,
            [typeof(Type), typeof(string[]).MakeByRefType()]);

        tryGet.ShouldNotBeNull(
            "EncryptedFieldRegistry.TryGetEncryptedProperties(Type, out string[]) was not found. The "
            + "Form-A probe changed shape and both registries that read this just became vacuous.");

        var columns = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in entities)
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

            // Form C — the binary store, THE SAME MANUAL SEAM.
            foreach (var sealedProperty in new[] { "SealedContent" })
            {
                var property = entity.FindProperty(sealedProperty);
                if (property is not null)
                    columns.Add(ColumnKey(entity, property));
            }
        }

        return columns;
    }

    /// <summary>
    /// Every text-bearing column among the given entities, grouped by table.
    /// </summary>
    /// <remarks>
    /// Several entity types can map to ONE table (an owned type is the usual case), so columns
    /// ACCUMULATE per table rather than replacing each other.
    /// </remarks>
    private static Dictionary<string, List<string>> TextColumnsByTable(IReadOnlyList<IEntityType> entities)
    {
        var byTable = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var entity in entities)
        {
            var table = entity.GetTableName();
            if (table is null)
                continue;

            if (!byTable.TryGetValue(table, out var columns))
            {
                columns = [];
                byTable[table] = columns;
            }

            foreach (var property in entity.GetProperties())
            {
                if (!IsTextBearingStoreType(property.GetColumnType()))
                    continue;

                columns.Add(ColumnKey(entity, property));
            }

            // The .ToJson() seam, CLOSED (it was ⚠-disclosed for two rounds): an owned
            // aggregate mapped to a JSON container column presents as a NAVIGATION, so its columns
            // never appear among the scalar properties above - but the container column itself is
            // text-bearing jsonb, and the model knows its name.
            var container = entity.GetContainerColumnName();
            if (container is not null)
                columns.Add($"{table}.{container}");
        }

        return byTable;
    }
}
