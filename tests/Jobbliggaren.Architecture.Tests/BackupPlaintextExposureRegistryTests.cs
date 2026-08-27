using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// The backup plaintext enumeration, enforced at COLUMN granularity against BOTH EF models (#1285).
/// </summary>
/// <remarks>
/// <b>Both arms fail, and they fail for opposite reasons.</b> An unclassified column fails because
/// UNKNOWN IS EXPOSED UNTIL SOMEBODY WRITES THE GROUND — the enumeration must never present itself
/// as complete while a column is missing, which is the defect #1285 reported. A classified column
/// the model does not have fails because that is how a dead entry is caught: all four prose homes
/// named <c>waitlist_entries</c> for fourteen months after it was dropped, and none of them could
/// notice.
/// </remarks>
public class BackupPlaintextExposureRegistryTests
{
    /// <summary>
    /// Every DEK-free text-bearing column in either model is classified. <b>Absence must FAIL, not
    /// be filtered away</b> — the sibling registry learned that the hard way when a
    /// <c>TryGetValue</c> guard silently passed over the two columns it existed to catch.
    /// </summary>
    [Fact]
    public void Every_DEK_free_text_column_in_either_model_is_classified()
    {
        var unclassified = PlaintextColumns()
            .Where(c => !BackupPlaintextExposureRegistry.Columns.ContainsKey(c))
            .Order(StringComparer.Ordinal)
            .ToList();

        unclassified.ShouldBeEmpty(
            "every DEK-free text-bearing column is readable in a restore WITH NO KEY, and the "
            + "enumeration Klas accepts residual exposure against (ADR 0125 Case 2, #197) is only "
            + "honest if it is complete.\n\n"
            + "UNKNOWN IS EXPOSED. A column with no classification is not 'probably fine' — it is a "
            + "column nobody has looked at, presented inside a list that claims to be exhaustive. "
            + "That is exactly what #1285 reported: four prose enumerations, none of them complete "
            + "in the CV direction.\n\n"
            + "Classify it in BackupPlaintextExposureRegistry.Columns. NoPersonalData requires the "
            + "ADMISSION RULE: a closed domain, or a retired write path with the population counted "
            + "at zero. A LIVE FREE-TEXT COLUMN IS NEVER NoPersonalData.\n\n"
            + "Unclassified:\n  " + string.Join("\n  ", unclassified));
    }

    /// <summary>
    /// Every classified column still exists. <b>This is the arm that makes a dead entry
    /// unwritable</b>, and it is the one no prose home could ever have.
    /// </summary>
    [Fact]
    public void Every_classified_column_still_exists_in_a_model()
    {
        var live = PlaintextColumns();
        var encrypted = ModelSweep.EncryptedColumns();

        var stale = BackupPlaintextExposureRegistry.Columns.Keys
            .Where(c => !live.Contains(c))
            .Select(c => encrypted.Contains(c)
                ? $"{c} — EXISTS but is DEK-ENCRYPTED, so it is not plaintext in a restore"
                : $"{c} — NO SUCH COLUMN in either model")
            .Order(StringComparer.Ordinal)
            .ToList();

        stale.ShouldBeEmpty(
            "a classified column is not in either EF model as a DEK-free text-bearing column.\n\n"
            + "If it was DROPPED: delete the entry. This arm exists because `waitlist_entries` was "
            + "dropped on 2026-06-27 and went on being named by ADR 0050:221, ADR 0125 (three "
            + "times) and the ROPA's backup entry — an enumeration Klas was meant to sign against, "
            + "one of whose four entries was a ghost. No prose home could catch that; this does.\n\n"
            + "If it became ENCRYPTED: delete the entry — it is no longer plaintext in a restore, "
            + "and leaving it OVERSTATES what a backup leaks.\n\n"
            + "Stale:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// <b>Anti-vacuity, per BUCKET.</b> Both facts above are emptiness checks over a derived set, so
    /// they pass trivially if the sweep narrows or a bucket empties. Each bucket pins a column that
    /// must be in it, and each pin is a column whose loss would mean something specific.
    /// </summary>
    [Fact]
    public void Each_bucket_holds_the_column_that_defines_it()
    {
        // The two entries the oldest enumeration named that the Art. 17 registry structurally
        // cannot see. If these fall out, the union sweep has stopped reaching Identity and this
        // registry has silently become the thing it replaced.
        Exposed("AspNetUsers.email");
        Exposed("AspNetUsers.user_name");

        // #1285's own finding: the DEK-free CV projections. These exist BECAUSE the CV body is
        // encrypted, and they are what "incomplete in the CV direction" meant.
        Exposed("resumes.name");
        Exposed("resumes.latest_role");
        Exposed("resumes.top_skills");
        Exposed("parsed_resumes.source_file_name");

        // The ROPA's sixth entry, which ADR 0125's five-item list omitted. The three-way
        // divergence was not noise: this one was right.
        Exposed("job_ads.description");

        // The remaining entries from the delivered enumerations, so a narrowing shows up here.
        Exposed("audit_log.ip_address");
        Exposed("resume_files.file_name");

        // NoPersonalData must not empty out either — if it does, the admission rule has stopped
        // costing anything and every awkward column will drift into it.
        NoPersonalData("taxonomy_concepts.label");
        NoPersonalData("applications.status");

        // The ADMISSION RULE's second clause has exactly one user. If this flips, someone has
        // reopened a retired write path and the "population counted at zero" ground is void.
        NoPersonalData("resume_versions.content");

        static void Exposed(string column) =>
            BackupPlaintextExposureRegistry.Columns.TryGetValue(column, out var e).ShouldSatisfyAllConditions(
                () => BackupPlaintextExposureRegistry.Columns.ShouldContainKey(column),
                () => BackupPlaintextExposureRegistry.Columns[column]
                    .ShouldBe(BackupExposure.PlaintextPersonalData,
                        $"{column} is a load-bearing member of the exposed set — it is named in at "
                        + "least one delivered enumeration, or it is the finding #1285 reported."));

        static void NoPersonalData(string column) =>
            BackupPlaintextExposureRegistry.Columns.ShouldContainKeyAndValue(
                column, BackupExposure.NoPersonalData,
                $"{column} pins the NoPersonalData bucket. A bucket that empties stops enforcing "
                + "its admission rule.");
    }

    /// <summary>
    /// <b>Anti-vacuity, per MODEL.</b> The whole mechanism change in #1285 is that the sweep unions
    /// <c>AppIdentityDbContext</c>. If that union silently breaks, the two facts above still pass —
    /// over a smaller set — and <c>email</c> and <c>name</c> quietly leave the enumeration again.
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
    /// <b>Anti-vacuity for the sweep's FORMS.</b> Inherited from the sibling registry, which learned
    /// it three times: narrow <see cref="ModelSweep.IsTextBearingStoreType"/> and the sweep reports
    /// fewer columns, which reads as MORE classified. Every store-type form pins a column.
    /// </summary>
    [Fact]
    public void The_sweep_SEES_a_sentinel_column_of_every_text_bearing_form()
    {
        var all = ModelSweep.AllModelsTextColumnsByTable().SelectMany(kv => kv.Value).ToHashSet(StringComparer.Ordinal);

        // varchar — the plain case.
        all.ShouldContain("resumes.name");

        // text[] — the array unwrap. Missed once.
        all.ShouldContain("resumes.top_skills");

        // jsonb behind a HasConversion — CLR type SearchCriteria, invisible for three rounds.
        all.ShouldContain("saved_searches.criteria");

        // tsvector — derived text is still text.
        all.ShouldContain("job_ads.search_vector");

        // The .ToJson() container seam — an owned aggregate presents as a NAVIGATION.
        all.ShouldContain("job_seekers.preferences");
    }

    /// <summary>
    /// A DEK-encrypted column is never in this registry: it is not plaintext in a restore, and
    /// listing it would OVERSTATE the exposure Klas accepts against.
    /// </summary>
    /// <remarks>
    /// The complement of the sibling registry's cross-check, which requires every encrypted column
    /// to be classified <c>HeldButNotSearchable</c> there. Together they say: encrypted columns are
    /// held-but-unsearchable for Art. 17, and absent here.
    /// </remarks>
    [Fact]
    public void No_DEK_encrypted_column_is_listed_as_plaintext()
    {
        var encrypted = ModelSweep.EncryptedColumns();

        encrypted.Count.ShouldBeGreaterThanOrEqualTo(7,
            "the encryption probe resolved fewer columns than the three forms deliver (Form A 4, "
            + "Form B 2, Form C 1). If it has gone vacuous, EVERY encrypted column would present as "
            + "plaintext and this fact would pass by being empty.");

        var listed = encrypted
            .Where(BackupPlaintextExposureRegistry.Columns.ContainsKey)
            .Order(StringComparer.Ordinal)
            .ToList();

        listed.ShouldBeEmpty(
            "a DEK-encrypted column is classified in the backup plaintext registry. It is not "
            + "plaintext in a restore — the ciphertext is what travels, and ADR 0125's split-artefact "
            + "design is what keeps the wrapped DEK out of the same envelope. Remove it:\n  "
            + string.Join("\n  ", listed));
    }

    /// <summary>
    /// Every DEK-free text-bearing column in either model, which is what a restore hands a reader
    /// with no key at all.
    /// </summary>
    private static HashSet<string> PlaintextColumns()
    {
        var all = ModelSweep.AllModelsTextColumnsByTable()
            .SelectMany(kv => kv.Value)
            .ToHashSet(StringComparer.Ordinal);

        all.ExceptWith(ModelSweep.EncryptedColumns());
        return all;
    }
}
