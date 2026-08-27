using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// The plaintext exposure enumeration, enforced against BOTH EF models (#1285).
/// </summary>
/// <remarks>
/// <b>Both arms fail, and they fail for opposite reasons.</b> An uncovered column fails because
/// UNKNOWN IS EXPOSED UNTIL SOMEBODY WRITES THE GROUND — the enumeration must never present itself
/// as complete while a column is missing, which is the defect #1285 reported. A listed table or
/// column the models do not have fails because that is how a dead entry is caught: the prose homes
/// named <c>waitlist_entries</c> after it was dropped, and none of them could notice.
/// </remarks>
public class MappedPlaintextExposureRegistryTests
{
    /// <summary>
    /// Every DEK-free text-bearing column in either model is covered: its table falls the row test,
    /// or the column carries its own verdict. <b>Absence must FAIL, not be filtered away</b> — the
    /// sibling registry learned that when a <c>TryGetValue</c> guard silently passed over the two
    /// columns it existed to catch.
    /// </summary>
    [Fact]
    public void Every_DEK_free_text_column_in_either_model_is_covered()
    {
        var uncovered = PlaintextColumns()
            .Where(c => !MappedPlaintextExposureRegistry.PersonGrainedTables.ContainsKey(TableOf(c))
                        && !MappedPlaintextExposureRegistry.Columns.ContainsKey(c))
            .Order(StringComparer.Ordinal)
            .ToList();

        uncovered.ShouldBeEmpty(
            "every DEK-free text-bearing column is readable in a restore WITH NO KEY, and the "
            + "enumeration Klas accepts residual exposure against (ADR 0125 Case 2, #197) is only "
            + "honest if it is complete.\n\n"
            + "UNKNOWN IS EXPOSED. A column with no verdict is not 'probably fine' — it is a column "
            + "nobody has looked at, inside a list that claims to be exhaustive. That is exactly "
            + "what #1285 reported.\n\n"
            + "STEP 1 — THE ROW TEST: can the table's row be attributed to an identifiable natural "
            + "person? If yes, add the TABLE to PersonGrainedTables with a written ground; every "
            + "text column on it is then exposed, derived. A closed domain beside a person's id is "
            + "still that person's personal data (Art. 4(1)), and a table falls the row test WHOLE "
            + "if it falls for any subset of its rows.\n"
            + "STEP 2 — only for tables that PASS the row test: give the column its own verdict.\n\n"
            + "Uncovered:\n  " + string.Join("\n  ", uncovered));
    }

    /// <summary>
    /// Every listed table and column still exists. <b>This is the arm that makes a dead entry
    /// unwritable</b>, and it is the one no prose home could ever have.
    /// </summary>
    [Fact]
    public void Every_listed_table_and_column_still_exists_in_a_model()
    {
        var live = PlaintextColumns();
        var encrypted = ModelSweep.AllModelsEncryptedColumns();
        var liveTables = ModelSweep.AllModelsTextColumnsByTable().Keys.ToHashSet(StringComparer.Ordinal);

        var stale = MappedPlaintextExposureRegistry.PersonGrainedTables.Keys
            .Where(t => !liveTables.Contains(t))
            .Select(t => $"TABLE {t} — NO SUCH TABLE in either model")
            .Concat(MappedPlaintextExposureRegistry.Columns.Keys
                .Where(c => !live.Contains(c))
                .Select(c => encrypted.Contains(c)
                    ? $"{c} — EXISTS but is DEK-ENCRYPTED, so it is not plaintext in a restore"
                    : $"{c} — NO SUCH COLUMN in either model"))
            .Order(StringComparer.Ordinal)
            .ToList();

        stale.ShouldBeEmpty(
            "a listed table or column is not in either EF model.\n\n"
            + "If it was DROPPED: delete the entry. This arm exists because `waitlist_entries` was "
            + "dropped on 2026-06-27 and went on being named by ADR 0050:221, ADR 0125 (three "
            + "times) and the ROPA's backup entry — an enumeration Klas was meant to sign against, "
            + "one of whose four entries was a ghost. No prose home could catch that; this does.\n\n"
            + "If it became ENCRYPTED: delete the entry — it is no longer plaintext in a restore, "
            + "and leaving it OVERSTATES what a restore leaks.\n\n"
            + "Stale:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// A table gets its verdict in exactly one step. A table in BOTH lists would have a derived
    /// verdict and a hand-written one over the same bytes, and nothing would say which wins.
    /// </summary>
    [Fact]
    public void No_table_is_judged_in_both_steps()
    {
        var columnTestTables = MappedPlaintextExposureRegistry.Columns.Keys
            .Select(TableOf)
            .ToHashSet(StringComparer.Ordinal);

        var both = columnTestTables
            .Where(MappedPlaintextExposureRegistry.PersonGrainedTables.ContainsKey)
            .Order(StringComparer.Ordinal)
            .ToList();

        both.ShouldBeEmpty(
            "a table falls the row test AND carries per-column verdicts. The row test is wholesale "
            + "by construction — every text column on such a table is exposed — so a per-column "
            + "entry there is either redundant or an attempt to opt one column out, and the second "
            + "is exactly what the row test exists to forbid.\n\n"
            + "In both steps:\n  " + string.Join("\n  ", both));
    }

    /// <summary>
    /// <b>A wholesale verdict is the cheapest possible false verdict in the system</b>, so every
    /// row-test ground must be a re-derivation and not a label.
    /// </summary>
    /// <remarks>
    /// Inherited from <c>Every_wholesale_excluded_tables_ground_names_every_text_bearing_column</c>
    /// one registry over, where a 60-character floor let a ground survive that never mentioned the
    /// column it hid. Here the wholesale verdict is the SAFE one, so the ground's job is different:
    /// it must say WHY the row is a person's, so that a table added for the wrong reason is visible.
    /// </remarks>
    [Fact]
    public void Every_person_grained_table_carries_a_written_ground()
    {
        var thin = MappedPlaintextExposureRegistry.PersonGrainedTables
            .Where(kv => kv.Value.Length < 60)
            .Select(kv => $"{kv.Key} — {kv.Value.Length} chars")
            .Order(StringComparer.Ordinal)
            .ToList();

        thin.ShouldBeEmpty(
            "a row-test ground is too short to be a re-derivation. Say WHY the row can be "
            + "attributed to a natural person — a foreign key, or the subset ground that put "
            + "company_register and job_ads here.\n\n" + string.Join("\n  ", thin));
    }

    /// <summary>
    /// <b>Anti-vacuity, per BUCKET.</b> The completeness facts are emptiness checks over derived
    /// sets, so they pass trivially if the sweep narrows or a bucket empties.
    /// </summary>
    [Fact]
    public void Each_bucket_holds_the_column_that_defines_it()
    {
        // The two entries the oldest enumeration named that the Art. 17 registry structurally
        // cannot see. If these fall out, the union sweep has stopped reaching Identity.
        Exposed("AspNetUsers.email");
        Exposed("AspNetUsers.user_name");

        // #1285's own finding: the DEK-free CV projections.
        Exposed("resumes.name");
        Exposed("resumes.latest_role");
        Exposed("resumes.top_skills");
        Exposed("parsed_resumes.source_file_name");

        // The ROPA's sixth entry, which ADR 0125's five-item list omitted.
        Exposed("job_ads.description");

        // The remaining entries from the delivered enumerations.
        Exposed("audit_log.ip_address");
        Exposed("resume_files.file_name");

        // The three collisions that forced the row test. Each is a sibling column of a row already
        // classified as exposed, and each read NoPersonalData under the first cut's rule.
        Exposed("user_job_ad_matches.grade");          // the profiling OUTCOME, Art. 4(4)/ADR 0090
        Exposed("parsed_resumes.parse_confidence");    // derived from her file, like layout_metrics
        Exposed("company_register.sni_codes");         // same row as an org.nr that is a personnummer

        // The .ToJson() container and a property inside it are the same bytes. The row test gives
        // them one verdict by construction; this pins that they did not drift apart.
        Exposed("job_seekers.preferences");
        Exposed("job_seekers.Language");

        // NoPersonalData must not empty out — if it does, the row test has swallowed everything and
        // stopped being a test.
        NotPersonalData("taxonomy_concepts.label");
        NotPersonalData("AspNetRoles.name");

        static void Exposed(string column) =>
            MappedPlaintextExposureRegistry.PlaintextPersonalDataColumns.ShouldContain(column,
                $"{column} is a load-bearing member of the exposed set — it is named in a delivered "
                + "enumeration, it is the finding #1285 reported, or it is one of the three "
                + "collisions that forced the row test.");

        static void NotPersonalData(string column) =>
            MappedPlaintextExposureRegistry.Columns.ShouldContainKeyAndValue(
                column, PlaintextExposure.NoPersonalData,
                $"{column} pins the NoPersonalData bucket. A bucket that empties stops enforcing "
                + "its admission rule.");
    }

    /// <summary>
    /// <b>Anti-vacuity, per MODEL.</b> The mechanism change in #1285 is that the sweep unions
    /// <c>AppIdentityDbContext</c>. If that breaks, the completeness facts still pass over a smaller
    /// set and <c>email</c> and <c>name</c> quietly leave the enumeration again.
    /// </summary>
    [Fact]
    public void The_sweep_reaches_BOTH_models()
    {
        var all = ModelSweep.AllModelsTextColumnsByTable().SelectMany(kv => kv.Value).ToHashSet(StringComparer.Ordinal);
        var app = ModelSweep.AppModelTextColumnsByTable().SelectMany(kv => kv.Value).ToHashSet(StringComparer.Ordinal);

        app.ShouldContain("resumes.name",
            "the app model must be reached — this is the column #1285 is named for.");

        all.ShouldContain("AspNetUsers.email",
            "AppIdentityDbContext must be reached. This is THE mechanism change in #1285: the Art. "
            + "17 registry excludes ASP.NET Identity wholesale (and keys it `asp_net_users`, which "
            + "matches no table in either model), so `email` and `name` — two of the four entries "
            + "in the oldest enumeration — were outside every sweep this project had.");

        all.Except(app).ShouldNotBeEmpty(
            "the union must add columns the app model does not have. If this is empty, the Identity "
            + "context resolved to nothing and the union is decorative.");
    }

    /// <summary>
    /// <b>Anti-vacuity for the sweep's FORMS.</b> Narrow
    /// <see cref="ModelSweep.IsTextBearingStoreType"/> and the sweep reports fewer columns, which
    /// reads as MORE covered. Every store-type form pins a column.
    /// </summary>
    [Fact]
    public void The_sweep_SEES_a_sentinel_column_of_every_text_bearing_form()
    {
        var all = ModelSweep.AllModelsTextColumnsByTable().SelectMany(kv => kv.Value).ToHashSet(StringComparer.Ordinal);

        all.ShouldContain("resumes.name");            // varchar
        all.ShouldContain("resumes.top_skills");      // text[] — the array unwrap
        all.ShouldContain("saved_searches.criteria"); // jsonb behind a HasConversion
        all.ShouldContain("job_ads.search_vector");   // tsvector
        all.ShouldContain("job_seekers.preferences"); // the .ToJson() container seam
    }

    /// <summary>
    /// A DEK-encrypted column is never in the enumeration: it is not plaintext in a restore, and
    /// listing it would OVERSTATE the exposure Klas accepts against.
    /// </summary>
    [Fact]
    public void No_DEK_encrypted_column_is_listed_as_plaintext()
    {
        var encrypted = ModelSweep.AllModelsEncryptedColumns();

        encrypted.Count.ShouldBeGreaterThanOrEqualTo(7,
            "the encryption probe resolved fewer columns than the three forms deliver (Form A 4, "
            + "Form B 2, Form C 1). If it has gone vacuous, EVERY encrypted column would present as "
            + "plaintext. Regenerate: EncryptedFieldRegistry's Map + JsonMap, plus SealedContent.");

        var listed = encrypted
            .Where(MappedPlaintextExposureRegistry.PlaintextPersonalDataColumns.Contains)
            .Order(StringComparer.Ordinal)
            .ToList();

        listed.ShouldBeEmpty(
            "a DEK-encrypted column reached the enumeration. It is not plaintext in a restore — the "
            + "ciphertext is what travels, and ADR 0125's split-artefact design keeps the wrapped "
            + "DEK out of the same envelope.\n  " + string.Join("\n  ", listed));
    }

    /// <summary>
    /// The DERIVED view is what the documents cite by name, and it was the one thing here with no
    /// consumer and no pin.
    /// </summary>
    /// <remarks>
    /// <c>PlaintextPersonalDataColumns</c> is what ADR 0125, the ROPA and ADR 0050 point at — and
    /// measured 2026-08-27 it has <b>zero</b> C# consumers. Invert its predicate and every other
    /// fact stays green while the enumeration Klas signs against says the opposite of what it means.
    /// That mutant survived the first round's mutation testing (dotnet-architect Viktigt 2 /
    /// code-reviewer Major 4).
    /// </remarks>
    [Fact]
    public void The_derived_view_the_documents_cite_is_the_exposed_set_and_only_it()
    {
        var exposed = MappedPlaintextExposureRegistry.PlaintextPersonalDataColumns;

        exposed.ShouldNotBeEmpty("an empty enumeration reads as 'a restore exposes nothing'.");

        exposed.ShouldNotContain("taxonomy_concepts.label",
            "a NoPersonalData column reached the published enumeration — the derivation is "
            + "inverted, and the set ADR 0125 and the ROPA cite by name now says the opposite of "
            + "what it means.");

        exposed.ShouldNotContain("AspNetRoles.name",
            "a role-vocabulary column reached the published enumeration.");

        exposed.ShouldBe(exposed.Distinct(StringComparer.Ordinal).ToList(),
            "the enumeration must not repeat a column — the two steps overlap somewhere.");

        // The whole exposed set is exactly: every plaintext column on a person-grained table, plus
        // any column-test column judged exposed. Nothing is dropped on the way to the documents.
        var expected = PlaintextColumns()
            .Where(c => MappedPlaintextExposureRegistry.PersonGrainedTables.ContainsKey(TableOf(c)))
            .Concat(MappedPlaintextExposureRegistry.Columns
                .Where(kv => kv.Value == PlaintextExposure.PlaintextPersonalData)
                .Select(kv => kv.Key))
            .Order(StringComparer.Ordinal)
            .ToList();

        exposed.ShouldBe(expected,
            "the published enumeration must be exactly what the two steps decide — no filter of its "
            + "own, nothing dropped.");
    }

    private static string TableOf(string columnKey) => columnKey[..columnKey.IndexOf('.')];

    /// <summary>
    /// Every DEK-free text-bearing column in either model — what a restore hands a reader with no
    /// key at all, within the models' reach.
    /// </summary>
    private static HashSet<string> PlaintextColumns()
    {
        var all = ModelSweep.AllModelsTextColumnsByTable()
            .SelectMany(kv => kv.Value)
            .ToHashSet(StringComparer.Ordinal);

        all.ExceptWith(ModelSweep.AllModelsEncryptedColumns());
        return all;
    }
}
