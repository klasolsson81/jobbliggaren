using System.Reflection;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;
using Jobbliggaren.Domain.Common;
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
    /// <remarks>
    /// ⚠ <b>THIS LIST IS A CLAIM, AND IT WAS FALSE.</b> <c>parsed_resumes</c> and
    /// <c>resume_files</c> sat here — the tables holding the RAW CV TEXT and the CV FILE — under a
    /// docstring saying their columns "structurally cannot hold recruiter free text". They swallowed
    /// three DEK-encrypted columns and two plaintext filenames whole, one level ABOVE the registry,
    /// where the column-level guard could not see them.
    /// <para>
    /// <b>A wholesale exclusion is the cheapest possible false verdict in the entire system.</b>
    /// <c>NotRecruiterData</c> costs a line per column; this costs ONE STRING and an entire table
    /// disappears — every column in it, present and future. Strongest claim, lowest price. That is
    /// the same inversion the registry's own dispositions had, one level up, and it is why the list
    /// is now a <b>Dictionary with a written ground per table</b>, pinned by a test.
    /// </para>
    /// <para>
    /// <b>ADMISSION RULE:</b> a table may be excluded wholesale ONLY if every column is closed-domain,
    /// or is that data subject's OWN datum whose write path cannot receive a third party's free text.
    /// <b>A table with even one arbitrary-free-text column may never be on this list.</b> That rule
    /// is what would have caught <c>parsed_resumes</c>, and writing the ground IS the re-derivation:
    /// you cannot write "every column here is closed-domain" about a table without noticing when it
    /// is not.
    /// </para>
    /// <para>
    /// And <see cref="Every_DEK_encrypted_column_carries_EXACTLY_ONE_disposition_HeldButNotSearchable"/>
    /// deliberately does <b>NOT</b> honour this list at all — an encrypted column must be classified
    /// wherever it lives. That guard is strictly stricter than this sweep.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> NonRecruiterTables = new(StringComparer.Ordinal)
    {
        // ── ASP.NET Identity: closed domains we mint, plus the USER'S OWN contact fields. ─────
        ["asp_net_users"] = "ASP.NET Identity. Every column is either a closed domain we mint "
            + "(normalised names, password hash, security stamp, concurrency stamp, lockout flags) "
            + "or the USER'S OWN email/phone. No write path lets a THIRD PARTY'S free text land in "
            + "any of them.",
        ["asp_net_roles"] = "Identity plumbing: role names drawn from a fixed set WE mint in code "
            + "(AuthorizationPolicies). No user write path reaches this table at all, so no free "
            + "text can enter it.",
        ["asp_net_user_roles"] = "Identity JOIN TABLE: two foreign keys and nothing else. There is "
            + "no column that could hold text of any kind, let alone a recruiter's.",
        ["asp_net_user_claims"] = "Identity claims: type/value pairs minted by our own code when a "
            + "role or policy is assigned. The user cannot author a claim, so no free text enters.",
        ["asp_net_user_logins"] = "Identity external-login keys: the provider's name and the "
            + "provider's opaque key. Both are minted by the OAuth provider, not by any user of "
            + "ours, and neither is free text.",
        ["asp_net_user_tokens"] = "Identity tokens: opaque values minted by the token provider "
            + "(2FA, password reset). No user write path, no free text.",
        ["asp_net_role_claims"] = "Identity claims attached to roles: minted by our own "
            + "authorisation code from a fixed vocabulary. No user write path.",

        // ── The seeker's OWN data, with no third-party free-text column. ──────────────────────
        ["job_seekers"] = "The SEEKER'S OWN profile and preferences: her display name, her "
            + "notification consent, her digest cadence, her watermarks. Every column is either her "
            + "own datum or a closed-domain preference (enum, boolean, timestamp). Not one column "
            + "accepts free text ABOUT A THIRD PARTY, which is the only thing this registry is "
            + "about.",
        // `resumes` USED TO BE HERE, on the ground "ids, timestamps and a status enum; it holds no
        // content". That sentence was copied from the AGGREGATE'S DOCSTRING and never checked
        // against the MAPPING. ResumeConfiguration maps `name` (varchar 200, free text she types via
        // Rename()), `latest_role` (varchar 500) and `top_skills` (text[]). The docstring was true
        // about the CV's BODY and false about the row.
        //
        // ⇒ It is column-classified now, and `resumes.name` is SEARCHED — it is the same datum, in
        // the same form, as saved_searches.name, which this registry already searches.
        //
        // THE LESSON, and it is the fourth time in this issue: WRITE THE GROUND AGAINST THE
        // MAPPING, NOT AGAINST THE DOCSTRING. An aggregate's prose describes what it is FOR; the EF
        // configuration describes what it HOLDS. Only the second one is a fact about the database.

        // ── Infrastructure the user cannot write into. ────────────────────────────────────────
        // (`sessions` USED TO BE HERE — an entry for a table that DOES NOT EXIST in the model. A
        // wholesale exclusion pre-granted to an undesigned table is a ground that by definition
        // cannot be re-derived; round-5 security M4. Removed. If a sessions table is ever built,
        // its author classifies it then, against the mapping that actually exists.)
        ["user_data_keys"] = "The DEK envelope itself: wrapped key material (bytes) and key ids. "
            + "There is no text column, and the write path is the key provider, not any user.",
        ["taxonomy_snapshot_meta"] = "Sync bookkeeping for the Arbetsförmedlingen taxonomy import: "
            + "a version string and timestamps, minted by the sync job. No user, and no free text, "
            + "can reach it.",

        // ── Pure link tables: ids, enums and timestamps. No text column exists. ───────────────
        ["saved_job_ads"] = "A LINK ROW: (job_seeker_id, job_ad_id, created_at). It records THAT she "
            + "bookmarked an ad, never anything about it. There is no text column to hold a "
            + "recruiter's name, and the ad's own text is classified under job_ads.",
        ["user_job_ad_matches"] = "A LINK ROW: (user_id, job_ad_id) plus a grade enum, a "
            + "notification-status enum, matched-term concept ids and timestamps. The matched terms "
            + "are taxonomy/skill ids from a closed vocabulary, never free text. The ad's text is "
            + "classified under job_ads.",
        ["company_watches"] = "A FOLLOW ROW: (user_id, organisation number) plus enums, "
            + "timestamps, and the `filter` jsonb — a WatchFilterSpec whose every string is a "
            + "concept-id validated against ConceptIdPattern (^[A-Za-z0-9_-]{1,32}) plus one bool "
            + "(OnlyMatched); no free text can enter it. The user-authored LABEL lives on "
            + "company_watch_criteria, which IS column-classified and IS searched. Nothing "
            + "free-text remains here.",
        ["followed_company_ad_hits"] = "A LINK ROW: (user_id, job_ad_id, company_watch_id) plus a "
            + "status enum and timestamps. It records THAT a followed company posted an ad. The "
            + "ad's text is classified under job_ads.",
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
    private static bool IsTextBearingStoreType(string storeType)
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

    private static List<string> RecruiterTextColumns()
    {
        using var context = ModelOnlyContext();

        var columns = new List<string>();
        foreach (var entity in context.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null || NonRecruiterTables.ContainsKey(table))
                continue;

            foreach (var property in entity.GetProperties())
            {
                if (!IsTextBearingStoreType(property.GetColumnType()))
                    continue;

                columns.Add(ColumnKey(entity, property));
            }

            // The .ToJson() seam, CLOSED (it was ⚠-disclosed for two rounds): an owned aggregate
            // mapped to a JSON container column presents as a NAVIGATION, so its columns never
            // appear among the scalar properties above — but the container column itself is
            // text-bearing jsonb, and the model knows its name.
            var container = entity.GetContainerColumnName();
            if (container is not null)
                columns.Add($"{table}.{container}");
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
    public void Every_DEK_encrypted_column_carries_EXACTLY_ONE_disposition_HeldButNotSearchable()
    {
        var encrypted = EncryptedColumns();

        // ── Anti-vacuity, per FORM. ──────────────────────────────────────────────────────────
        // A single floor over the union is not enough: Form A alone (4 columns) satisfies
        // `Count >= 4`, so the Form-B arm could go silently empty and the guard would still pass.
        // Each arm is pinned by a column that must be in it.
        var formA = new[]
        {
            "applications.cover_letter",
            "application_notes.content",
            "follow_ups.note",
            "parsed_resumes.raw_text",
        };

        var formB = new[]
        {
            "resume_versions.content_enc",
            "parsed_resumes.parsed_content_enc",
        };

        // Form C — the sealed binary store. Pinned by name, because its DISPOSITION was otherwise
        // unguarded: nothing stopped someone flipping it to NotRecruiterData, dropping the CV file
        // out of the "could not search" list we hand a named data subject, with a green suite.
        var formC = new[]
        {
            "resume_files.content",
        };

        foreach (var column in formA.Concat(formB).Concat(formC))
        {
            encrypted.ShouldContain(column,
                $"{column} is DEK-encrypted and the cross-check must SEE it. If this fails, the "
                + "reflection or the EF-model → column-name mapping has drifted, and the guard is "
                + "reaching past the very columns it exists to catch. Resolved: "
                + string.Join(", ", encrypted.Order(StringComparer.Ordinal)));
        }

        // ── THE PREDICATE. Absence must FAIL, not be filtered away. ──────────────────────────
        //
        // The first cut of this test read:
        //
        //     encrypted.Where(c => Columns.TryGetValue(c, out var d) && Searched.Contains(d))
        //             .ShouldBeEmpty();
        //
        // TryGetValue returns FALSE for a column that is not in the registry at all — so it was
        // filtered out and the assertion passed over it. AN ENCRYPTED COLUMN THAT WAS ENTIRELY
        // ABSENT PASSED THE GUARD AGAINST ENCRYPTED COLUMNS. Two did: parsed_resumes.raw_text and
        // parsed_resumes.parsed_content_enc, swallowed one level up by NonRecruiterTables — a list
        // whose own doc calls its tables "structurally incapable of holding recruiter free text",
        // about the table holding the raw CV text.
        //
        // Emptiness was guarded. INCOMPLETENESS was not. That is the same inversion this whole
        // registry exists to end, reproduced inside the guard written to end it.
        var wrong = new List<string>();

        foreach (var column in encrypted.Order(StringComparer.Ordinal))
        {
            if (!ErasureCascadeRegistry.Columns.TryGetValue(column, out var disposition))
            {
                wrong.Add($"{column} — ABSENT from the registry entirely");
                continue;
            }

            if (disposition != ErasureColumnDisposition.HeldButNotSearchable)
                wrong.Add($"{column} — classified {disposition}");
        }

        wrong.ShouldBeEmpty(
            "every DEK-encrypted column carries EXACTLY ONE disposition: HeldButNotSearchable.\n\n"
            + "NOT SEARCHED — a plaintext LIKE against ciphertext compares a name to base64 and "
            + "matches NOTHING, structurally, forever, and the reply turns that zero into 'we hold no "
            + "data about you', to a named data subject.\n"
            + "NOT NotRecruiterData — that word claims we hold nothing of hers. We DO hold it. We "
            + "simply cannot read it.\n"
            + "NOT ABSENT — an unclassified encrypted column never reaches CouldNotSearch, so the "
            + "list we hand her presents itself as complete and is not. Art. 5(1)(d), 5(2), 12(3).\n\n"
            + "Offenders:\n  " + string.Join("\n  ", wrong));
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

            // Form C — the binary store. THE SAME MANUAL SEAM, and it was PROMISED and not
            // delivered: the registry's written ground said "this column is enumerated BY HAND
            // there", and it was not. So flipping resume_files.content to NotRecruiterData would
            // have dropped it out of CouldNotSearch in silence, with a green suite — the round-3
            // Blocker again, one form over.
            //
            // Form C is sealed via IBinaryFieldSealer/IBinaryFieldOpener and is DELIBERATELY absent
            // from EncryptedFieldRegistry (its read path is streaming and never engages the
            // materialisation interceptor), so there is no allowlist to reflect over. Hence the hand
            // enumeration — and hence saying so, loudly, instead of letting the guard read as if it
            // covered all three forms.
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
    /// The ground requirement is per COLUMN, not per table (round-5 security M2). A new column
    /// gliding into an already-grounded <c>(table, disposition)</c> bucket used to cost NOTHING —
    /// the cost inversion this file exists to close, one level down. The <c>text[]</c> sweep
    /// exploited it the same day it landed: <c>resumes.reviewed_rubric_version</c> and
    /// <c>company_register.sni_codes</c> both entered the strongest bucket under grounds that
    /// never mentioned them.
    /// </summary>
    /// <remarks>
    /// The ground IS the re-derivation — that is the file's whole thesis — so a column that its
    /// own ground does not name has not been re-derived. Requiring the bare column name in the
    /// ground text is deliberately crude: it cannot prove the sentence ABOUT the column is true,
    /// but it makes silently inheriting a bucket impossible, and the reviewer reads the sentence.
    /// </remarks>
    [Fact]
    public void Every_grounded_columns_ground_actually_names_the_column()
    {
        var unnamed = new List<string>();

        foreach (var (key, disposition) in ErasureCascadeRegistry.Columns)
        {
            if (!GroundedDispositions.Contains(disposition))
                continue;

            var parts = key.Split('.');
            var groundKey = $"{parts[0]}:{disposition}";

            if (!ErasureCascadeRegistry.WrittenGrounds.TryGetValue(groundKey, out var ground))
                continue; // the missing-ground case is Every_disposition_that_owes_a_ground_has_one's

            if (!ground.Contains(parts[1], StringComparison.OrdinalIgnoreCase))
                unnamed.Add($"{key} — absent from the {groundKey} ground");
        }

        unnamed.ShouldBeEmpty(
            "a column whose own ground never names it has inherited a verdict, not earned one. "
            + "Extend the ground with the column's OWN write-path sentence:\n  "
            + string.Join("\n  ", unnamed));
    }

    // ════════════════════════════════════════════════════════════════════════════════════
    // THE CHANNEL PIN (round 6) — the registry DRIVES the port. Round 5's two Blockers were
    // both instances of the same unpinned seam: a column classified as searched that no port
    // method searched (snapshot_url), and a port arm whose column the registry misdescribed
    // (employer_list). These tests make the seam a build break.
    // ════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Every column the registry claims is SEARCHED belongs to exactly ONE channel. This is the
    /// test that makes round-5 B5-2 (<c>applications.snapshot_url</c> — classified
    /// <c>MatchedRetained</c>, "searched and reported", never queried) unshippable: a searched
    /// classification with no channel breaks the build, and the channel's own single-column
    /// integration test breaks it if the SQL does not follow.
    /// </summary>
    [Fact]
    public void Every_searched_column_belongs_to_exactly_one_channel()
    {
        var channelColumns = ErasureCascadeRegistry.Channels
            .SelectMany(c => c.Columns.Select(col => (c.Surface, Column: col)))
            .ToList();

        // No column is claimed by two channels — a double claim would double-report her.
        channelColumns.GroupBy(x => x.Column, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList()
            .ShouldBeEmpty("a column may be claimed by exactly one channel.");

        var claimed = channelColumns.Select(x => x.Column).ToHashSet(StringComparer.Ordinal);

        var unclaimed = ErasureCascadeRegistry.Columns
            .Where(kv => SearchedDispositions.Contains(kv.Value) && !claimed.Contains(kv.Key))
            .Select(kv => $"{kv.Key} ({kv.Value})")
            .Order(StringComparer.Ordinal)
            .ToList();

        unclaimed.ShouldBeEmpty(
            "these columns are classified as SEARCHED (MatchedHumanErases/MatchedRetained) and "
            + "belong to NO channel — i.e. the registry certifies a search no port method runs. "
            + "That is the round-2 Blocker, the round-5 Blocker, and the shape of five review "
            + "rounds. Add the column to its surface's ErasureChannel AND to the port method's "
            + "SQL, then seed the single-column match test:\n  "
            + string.Join("\n  ", unclaimed));
    }

    /// <summary>
    /// Every channel names a REAL port method and a REAL reported surface, and every channel
    /// column is a classified registry column. A channel pointing at a renamed method — or a
    /// surface the response never reports — would be the vacuous control rebuilt one level up.
    /// </summary>
    [Fact]
    public void Every_channel_names_a_real_port_method_and_a_reported_surface()
    {
        ErasureCascadeRegistry.Channels.ShouldNotBeEmpty();

        var surfaces = new List<string>();

        foreach (var channel in ErasureCascadeRegistry.Channels)
        {
            typeof(Jobbliggaren.Application.JobAds.Abstractions.IRecruiterErasureMatchQuery)
                .GetMethod(channel.PortMethod)
                .ShouldNotBeNull(
                    $"channel {channel.Surface} names port method '{channel.PortMethod}', which "
                    + "does not exist on IRecruiterErasureMatchQuery. The registry is driving a "
                    + "port that is not there.");

            foreach (var column in channel.Columns)
            {
                ErasureCascadeRegistry.Columns.ShouldContainKey(column,
                    $"channel {channel.Surface} claims to search '{column}', which is not a "
                    + "classified registry column. A channel may only claim columns the registry "
                    + "has ruled on.");
            }

            surfaces.Add(channel.Surface);
        }

        surfaces.ToHashSet(StringComparer.Ordinal).ShouldBe(
            ErasureCascadeRegistry.ReportedSurfaces, ignoreOrder: true,
            "the channel set and the reported surfaces must be the SAME set — a surface with no "
            + "channel is a count derived from no search, and a channel with no surface is a "
            + "search whose result never reaches her.");

        surfaces.Count.ShouldBe(surfaces.Distinct(StringComparer.Ordinal).Count(),
            "one channel per surface — two channels reporting into one count would double it.");
    }

    /// <summary>
    /// Every <c>Erased</c> column is EITHER searched by a channel OR carries a written derivation
    /// ground in <see cref="ErasureCascadeRegistry.ErasedWithoutSearchChannel"/>. No third state.
    /// </summary>
    /// <remarks>
    /// This is round-5 security m2 made exhaustive — the Minor that predicted round 5's Blocker
    /// one round in advance (<i>"a third table entering the Erased bucket still slips through
    /// silently"</i> — one did, and it was the Blocker). An <c>Erased</c> column dies with its
    /// carrier only if the MATCH can find the carrier, so a column with no channel of its own owes
    /// a written argument for why no row can carry her identifier in that column ALONE. It also
    /// closes the code-reviewer's predicted round-6 opener (<c>job_ads.url</c> /
    /// <c>organization_number</c>): org.nr became a channel column; url carries the argument.
    /// </remarks>
    [Fact]
    public void Every_Erased_column_is_channel_searched_or_carries_a_derivation_ground()
    {
        var channelClaimed = ErasureCascadeRegistry.Channels
            .SelectMany(c => c.Columns)
            .ToHashSet(StringComparer.Ordinal);

        var erased = ErasureCascadeRegistry.Columns
            .Where(kv => kv.Value == ErasureColumnDisposition.Erased)
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);

        erased.ShouldNotBeEmpty("no Erased columns — this test is itself vacuous.");

        var unaccounted = erased
            .Where(c => !channelClaimed.Contains(c)
                && !ErasureCascadeRegistry.ErasedWithoutSearchChannel.ContainsKey(c))
            .Order(StringComparer.Ordinal)
            .ToList();

        unaccounted.ShouldBeEmpty(
            "an Erased column must be reachable: either a channel searches it, or "
            + "ErasedWithoutSearchChannel carries the written derivation for why a row cannot "
            + "hold her identifier in this column alone. 'It is erased anyway' certified round 5's "
            + "Blocker. Unaccounted:\n  " + string.Join("\n  ", unaccounted));

        // The inverse: no stale derivation entries for columns that are not Erased (or that a
        // channel now searches — the ground would then be dead prose shadowing a live claim).
        var stale = ErasureCascadeRegistry.ErasedWithoutSearchChannel.Keys
            .Where(c => !erased.Contains(c) || channelClaimed.Contains(c))
            .Order(StringComparer.Ordinal)
            .ToList();

        stale.ShouldBeEmpty(
            "ErasedWithoutSearchChannel entries must be exactly the Erased columns no channel "
            + "claims. Stale:\n  " + string.Join("\n  ", stale));

        foreach (var (column, ground) in ErasureCascadeRegistry.ErasedWithoutSearchChannel)
        {
            ground.Length.ShouldBeGreaterThan(60,
                $"{column}'s derivation ground is too thin to be a derivation.");
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
    /// <c>Total</c> is a HAND-WRITTEN sum, and it is load-bearing twice: it decides
    /// <c>NoMatchInSearchableSurfaces</c> and it feeds the derived <c>Outcome</c>.
    /// </summary>
    /// <remarks>
    /// The outcome word cannot lie <b>only because the counts are right</b>. Add a surface, forget
    /// the sum, and <c>Total</c> silently under-reports: we would answer <i>"we found nothing"</i> to
    /// a data subject we searched for and FOUND, on the very surface just added. The property is
    /// excluded from the reported-surface reflection by name, so nothing else pins it. This does.
    /// </remarks>
    [Fact]
    public void Total_sums_EVERY_reported_surface_and_not_a_subset()
    {
        var surfaces = typeof(ErasureSurfaceCounts)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(int) && p.Name != nameof(ErasureSurfaceCounts.Total))
            .ToList();

        surfaces.ShouldNotBeEmpty("reflection found no surfaces — this test is itself vacuous.");

        // One surface at a time: set it to 1, everything else 0, and require Total == 1. A surface
        // missing from the sum contributes 0 and is named in the failure.
        var missing = new List<string>();

        foreach (var surface in surfaces)
        {
            var args = surfaces
                .Select(p => (object)(p.Name == surface.Name ? 1 : 0))
                .ToArray();

            var counts = (ErasureSurfaceCounts)Activator.CreateInstance(
                typeof(ErasureSurfaceCounts), args)!;

            if (counts.Total != 1)
                missing.Add(surface.Name);
        }

        missing.ShouldBeEmpty(
            "these surfaces are NOT in ErasureSurfaceCounts.Total's hand-written sum. A surface that "
            + "does not reach Total is a surface we searched, found her on, and then reported "
            + "'NoMatchInSearchableSurfaces' about — because Total drives the outcome word. "
            + "Missing from the sum: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The AUDIT PAYLOAD reports every surface too — it is the Art. 5(2) accountability record, and
    /// a surface missing from it is a surface an auditor cannot check us on.
    /// </summary>
    /// <remarks>
    /// Adding a surface currently requires remembering EIGHT places (registry, counts record, Total,
    /// port, implementation, handler, audit payload, reply template) and exactly ONE of them breaks
    /// the build. This closes a second one. The remaining gap is real and is written down rather
    /// than papered over: the port's channel set and the reply template are still pinned by nothing
    /// but a human.
    /// </remarks>
    [Fact]
    public void The_audit_payload_reports_every_surface()
    {
        var surfaces = typeof(ErasureSurfaceCounts)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(int) && p.Name != nameof(ErasureSurfaceCounts.Total))
            .Select(p => p.Name)
            .ToList();

        surfaces.ShouldNotBeEmpty();

        // Drive the REAL BuildAuditPayload through the REAL command, with a fake pseudonymiser.
        var counts = new ErasureSurfaceCounts(1, 2, 3, 4, 5, 6, 7, 8);
        var response = new EraseRecruiterAdsResponse(
            RequestId: Guid.NewGuid(),
            DryRun: true,
            Matched: counts,
            Erased: ErasureSurfaceCounts.None,
            Matches: [],
            MatchedRecentSearchTerms: [],
            ErasedExternalIds: [],
            CouldNotSearch: UnsearchableSurfaces.FromRegistry());

        var command = new EraseRecruiterAdsCommand(
            Guid.NewGuid(), "magnus.fagerberg@example.se", DryRun: true, ConfirmedJobAdIds: null);

        var json = command.BuildAuditPayload(
            Result.Success(response), new FixedPseudonymizer());

        json.ShouldNotBeNull();

        var missing = surfaces
            .Where(s => !json.Contains(
                char.ToLowerInvariant(s[0]) + s[1..], StringComparison.Ordinal))
            .ToList();

        missing.ShouldBeEmpty(
            "these surfaces are not in the audit payload. The audit row is the Art. 5(2) "
            + "accountability record — what an auditor, or the recruiter herself, checks us against. "
            + "A surface we searched and did not record is a search nobody can verify. Missing: "
            + string.Join(", ", missing));
    }

    private sealed class FixedPseudonymizer : IIdentifierPseudonymizer
    {
        public string Pseudonymize(string identifier) => "hmac-fixture";
    }

    /// <summary>
    /// Every table excluded WHOLESALE from the column sweep carries a written ground naming its
    /// write-path guarantee.
    /// </summary>
    /// <remarks>
    /// <b>This is the cheapest false verdict in the system, and it produced a Blocker.</b>
    /// <c>NotRecruiterData</c> costs a line per column; a wholesale exclusion costs ONE STRING and an
    /// entire table vanishes — every column in it, present and future. <c>parsed_resumes</c> and
    /// <c>resume_files</c> sat on that list, and with them went the raw CV text, the CV file, and two
    /// plaintext filenames.
    /// <para>
    /// The ground IS the re-derivation: you cannot write <i>"every column here is closed-domain"</i>
    /// about a table without noticing when it is not.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_wholesale_excluded_table_carries_a_written_ground()
    {
        NonRecruiterTables.ShouldNotBeEmpty();

        foreach (var (table, ground) in NonRecruiterTables)
        {
            ground.Length.ShouldBeGreaterThan(60,
                $"{table} is excluded WHOLESALE — every column in it, present and future, disappears "
                + "from the Art. 17 registry on the strength of this one sentence. It may be excluded "
                + "ONLY if every column is closed-domain, or is that data subject's own datum whose "
                + "write path cannot receive a third party's free text. A table with even one "
                + "arbitrary-free-text column may never be here. Name the write-path guarantee.");
        }
    }

    /// <summary>
    /// Every aggregate reachable from <c>IAppDbContext</c> has at least one column in the registry,
    /// or is wholesale-excluded WITH a ground. There is no third option.
    /// </summary>
    /// <remarks>
    /// The previous version of this test reflected the DbSets and asserted only
    /// <c>ShouldNotBeEmpty()</c> — it compared them against nothing. It could not fail for the reason
    /// it existed. <b>A test that cannot fail is not a test; it is a comment with a green tick.</b>
    /// </remarks>
    [Fact]
    public void Every_persisted_aggregate_is_classified_or_excluded_with_a_ground()
    {
        using var context = ModelOnlyContext();

        var dbSetTypes = typeof(IAppDbContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.PropertyType)
            .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(t => t.GetGenericArguments()[0])
            .ToList();

        dbSetTypes.ShouldNotBeEmpty("reflection over IAppDbContext must find the DbSets.");

        var classifiedTables = ErasureCascadeRegistry.Columns.Keys
            .Select(k => k.Split('.')[0])
            .ToHashSet(StringComparer.Ordinal);

        var unaccounted = new List<string>();

        foreach (var clr in dbSetTypes)
        {
            var table = context.Model.FindEntityType(clr)?.GetTableName();
            if (table is null)
                continue;

            if (!classifiedTables.Contains(table) && !NonRecruiterTables.ContainsKey(table))
                unaccounted.Add(table);
        }

        unaccounted.ShouldBeEmpty(
            "an aggregate the application can reach is in NEITHER the column registry NOR the "
            + "wholesale-exclusion list. A whole new table with ids only would otherwise slip past "
            + "the column sweep in silence. "
            + "Unaccounted: " + string.Join(", ", unaccounted));
    }
}
