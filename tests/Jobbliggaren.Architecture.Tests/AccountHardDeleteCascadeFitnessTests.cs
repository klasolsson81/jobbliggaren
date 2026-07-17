using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Files;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Domain.SavedJobAds;
using Jobbliggaren.Domain.SavedSearches;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #374 — GDPR Art. 17 hard-delete-cascade fitness function (recommended by the security-auditor
/// during the #370 re-review).
///
/// <para>
/// <b>The recurring failure mode.</b> The ADR 0011 strongly-typed soft-reference pattern means
/// FK-less aggregates keyed by <c>JobSeekerId</c>/<c>UserId</c> have NO database FK to
/// <c>job_seekers</c> — on account hard-delete there is no DB cascade, so each must be deleted
/// EXPLICITLY in <see cref="Jobbliggaren.Infrastructure.Auth.AccountHardDeleter"/>.<c>HardDeleteAccountAsync</c>,
/// or its PII rows silently ORPHAN. This has happened FOUR times reactively (SavedSearches,
/// SavedJobAd, UserJobAdMatch, ParsedResume = #370 Blocker), each caught by a security audit,
/// never by a test. This fitness function converts the reactive audit-catch into a proactive
/// build-time gate.
/// </para>
///
/// <para>
/// <b>Design (senior-cto-advisor bound; re-architected after adversarial review 2026-06-29).</b>
/// The gate is FAIL-CLOSED on classification, not on a positive shape predicate (Saltzer &amp;
/// Schroeder fail-safe defaults; Ford/Parsons/Kua, <i>Building Evolutionary Architectures</i>,
/// ch. 2). An earlier draft included an aggregate in the cascade requirement only if reflection
/// recognised its owner-key SHAPE (a <c>JobSeekerId</c>-typed or <c>Guid UserId</c> property); the
/// adversarial verifier proved that fails OPEN on any unanticipated shape — a nested-VO key, a
/// raw <c>Guid OwnerId</c>, or (worst) a strongly-typed <c>UserId</c> value object, which ADR 0011
/// actively encourages. When the shape predicate misses a type it is absent from both reflection
/// AND the map, so a dual-direction diff stays silent → the #370 class re-admitted. The durable
/// fix removes the dependence on recognising the shape:
/// <list type="number">
///   <item><b>Partition completeness (fail-closed).</b> EVERY concrete <see cref="AggregateRoot{TId}"/>
///     in the Domain assembly must be classified — either <see cref="CascadeMap"/> (FK-less
///     user-owned PII that MUST be wired into the cascade) or <see cref="NotUserOwned"/> (explicit,
///     no user PII). A new aggregate of ANY shape lands in neither and fails the build until a human
///     classifies it. The owner-key heuristic survives only as a non-load-bearing ADVISORY hint in
///     the failure message (being wrong there cannot make the guard pass).</item>
///   <item><b>Cascade wiring (source-text scan, method-body-scoped).</b> Each <see cref="CascadeMap"/>
///     aggregate's <c>db.&lt;DbSet&gt;</c> token must be bound to a deletion verb
///     (RemoveRange/Remove/ExecuteDeleteAsync) within <c>HardDeleteAccountAsync</c>'s body — the
///     house source-scan idiom (mirrors <see cref="EncryptedFieldProjectionGuardTests"/>; the
///     repo's written doctrine rejects brittle IL/method-body reflection for a focused source-scan).
///     The scan binds the DbSet to a verb within the SAME statement, so a read/count reference does
///     NOT count; and it is scoped to the method body via brace-matching, so a delete verb in a
///     SIBLING method cannot falsely satisfy an aggregate the cascade omits.</item>
///   <item><b>DbSet-name pin.</b> Each <see cref="CascadeMap"/> DbSet name is pinned to a real
///     <c>DbSet&lt;T&gt;</c> on <see cref="AppDbContext"/> — a typo'd/renamed DbSet (which would make
///     the scan silently vacuous) fails loud here instead.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Scope (accepted residual, by design).</b> This guard covers FK-less user-owned
/// <see cref="AggregateRoot{TId}"/>s requiring delete-cascade. PII <see cref="Entity{TId}"/> types
/// are OUT of scope: <c>AuditLogEntry</c> is governed by ANONYMIZATION
/// (<c>IAuditTrailEraser.AnonymizeUserAuditTrailAsync</c>), not deletion (Art. 5(2) accountability,
/// ADR 0024), and child entities (ApplicationNote/FollowUp/ResumeVersion) cascade via a parent
/// aggregate's DB-FK. Folding <c>Entity&lt;&gt;</c> into this guard would conflate two distinct
/// Art. 17 mechanisms (delete vs. anonymize) — a SoC violation. A new FK-less PII
/// <c>Entity&lt;&gt;</c> with NO parent cascade is the one residual this guard does not catch; it is
/// covered by the security-audit practice + the behavioral oracle
/// <c>HardDeleteAccountsJobIntegrationTests</c> (Testcontainers), which proves the cascade by
/// running it. This cheap build-time PROXY sits over that expensive behavioral oracle.
/// </para>
///
/// <para>Refs: #374, #370, ADR 0011 (strongly-typed soft-reference), ADR 0024 (account hard-delete),
/// ADR 0049/0080/0074 (the four aggregates added reactively), CLAUDE.md §5 (GDPR Art. 17).</para>
/// </summary>
public class AccountHardDeleteCascadeFitnessTests
{
    private const string HardDeleterRelativePath =
        "src/Jobbliggaren.Infrastructure/Auth/AccountHardDeleter.cs";

    private const string CascadeMethodName = "HardDeleteAccountAsync";

    /// <summary>
    /// FK-less by-JobSeekerId/UserId PII aggregates that MUST live in the Art. 17 cascade, mapped to
    /// their <c>AppDbContext</c> DbSet property name (the token the source-scan binds to a deletion
    /// verb).
    /// </summary>
    private static readonly IReadOnlyDictionary<Type, string> CascadeMap = new Dictionary<Type, string>
    {
        [typeof(DomainApplication)] = "Applications",
        [typeof(Resume)] = "Resumes",
        [typeof(ParsedResume)] = "ParsedResumes",
        [typeof(ResumeFile)] = "ResumeFiles",
        [typeof(SavedSearch)] = "SavedSearches",
        [typeof(RecentJobSearch)] = "RecentJobSearches",
        [typeof(SavedJobAd)] = "SavedJobAds",
        [typeof(UserJobAdMatch)] = "UserJobAdMatches",
        [typeof(CompanyWatch)] = "CompanyWatches",
        [typeof(FollowedCompanyAdHit)] = "FollowedCompanyAdHits",
        [typeof(CompanyWatchCriterion)] = "CompanyWatchCriteria",
    };

    /// <summary>
    /// Aggregate roots that carry NO user-owned PII soft-reference → need no cascade entry. A new
    /// aggregate belongs here ONLY if it holds no JobSeeker/User personal data. Keep the reason
    /// explicit so the classification stays a conscious decision.
    /// </summary>
    private static readonly HashSet<Type> NotUserOwned =
    [
        typeof(JobSeeker), // the deletion ROOT itself (removed via db.JobSeekers.Remove(jobSeeker))
        typeof(JobAd),     // public employer/job data, not user-owned PII
    ];

    [Fact]
    public void Every_aggregate_root_is_classified_user_owned_or_not()
    {
        // FAIL-CLOSED partition gate (the durable fix). Every concrete AggregateRoot<> in Domain must
        // be classified — CascadeMap (deleted in the cascade) OR NotUserOwned (no user PII). A new
        // aggregate of ANY owner-key shape lands in neither and fails here, before it can orphan PII.
        var allRoots = ReflectAllAggregateRoots();
        var classified = CascadeMap.Keys.Concat(NotUserOwned);

        var unclassified = HardDeleteCascadeScan.FindUnclassified(allRoots, classified);

        unclassified.ShouldBeEmpty(
            "Följande aggregate root(s) är OKLASSIFICERADE — varken i CascadeMap (FK-löst user-owned " +
            "PII som MÅSTE raderas i Art. 17-cascaden) eller NotUserOwned (ingen user-PII). Per " +
            "fail-safe default måste varje nytt aggregat klassificeras MEDVETET: bär det user-PII " +
            "soft-refererad via JobSeekerId/UserId (oavsett form — direkt property, nested value " +
            "object, eller strongly-typed id) → lägg i CascadeMap OCH wire:a in det i " +
            "AccountHardDeleter.HardDeleteAccountAsync; annars → NotUserOwned med en rad om varför " +
            "(annars orphan:as dess rader vid konto-hard-delete, GDPR Art. 17, #374/#370). " +
            "Oklassificerade: " + AdviseClassification(unclassified));
    }

    [Fact]
    public void CascadeMap_and_NotUserOwned_are_disjoint()
    {
        // A type cannot be both user-owned (must delete) and not-user-owned (need not). Catches a
        // mis-edit that classifies an aggregate twice.
        var overlap = CascadeMap.Keys.Intersect(NotUserOwned).Select(t => t.Name).ToList();

        overlap.ShouldBeEmpty(
            "En typ får inte vara både i CascadeMap (user-owned PII) och NotUserOwned: " +
            string.Join(", ", overlap));
    }

    [Fact]
    public void Every_mapped_DbSet_name_is_a_real_DbSet_of_the_mapped_type()
    {
        // Pins the map's DbSet NAMES to AppDbContext. The source-scan searches for the literal
        // "db.<DbSetName>" — a typo'd or renamed DbSet name would make the whole scan silently
        // vacuous, so a renamed/removed DbSet fails loud here.
        foreach (var (clrType, dbSetName) in CascadeMap)
        {
            var prop = typeof(AppDbContext).GetProperty(
                dbSetName, BindingFlags.Public | BindingFlags.Instance);

            prop.ShouldNotBeNull(
                $"CascadeMap mappar {clrType.Name} → db.{dbSetName}, men AppDbContext saknar en " +
                $"property med det namnet. Source-scan:en söker 'db.{dbSetName}' → en felstavad/" +
                "omdöpt DbSet skulle göra vakten verkningslös. Synka mot AppDbContext.");

            prop!.PropertyType.ShouldBe(typeof(DbSet<>).MakeGenericType(clrType),
                $"AppDbContext.{dbSetName} ska vara DbSet<{clrType.Name}> (CascadeMap-invariant).");
        }
    }

    [Fact]
    public void Every_user_owned_aggregate_is_wired_into_the_Art17_cascade()
    {
        // The core fitness function: every CascadeMap aggregate is bound to a deletion verb in
        // HardDeleteAccountAsync's BODY. An omission = orphaned PII on hard-delete (the #370 class).
        var body = HardDeleteCascadeScan.ExtractMethodBody(ReadHardDeleterSource(), CascadeMethodName);

        var unwired = HardDeleteCascadeScan.FindUnwiredDbSets(body, CascadeMap.Values);

        unwired.ShouldBeEmpty(
            "Följande FK-lösa user-owned-aggregat är INTE wirade till en delete-verb " +
            "(RemoveRange/Remove/ExecuteDeleteAsync) i AccountHardDeleter.HardDeleteAccountAsync → " +
            "deras PII-rader ORPHAN:as vid konto-hard-delete (GDPR Art. 17, #374; samma klass som " +
            "#370-Blockern). Radera dem i cascaden, ELLER — om aggregatet numera cascadar via en " +
            "äkta DB-FK till job_seekers — flytta det till NotUserOwned med en kommentar. " +
            "Oraderade: " + string.Join(", ", unwired));
    }

    [Fact]
    public void Cascade_reads_carry_IgnoreQueryFilters_iff_the_entity_declares_a_query_filter()
    {
        // #560 demolition (senior-cto-advisor H1b, 2026-07-17). The cascade's completeness has a
        // second failure mode the wiring scan above cannot see: a read that is wired to a delete verb
        // but SILENTLY NARROWED. RemoveRange operates on an already-materialised list, so if a query
        // filter is added to an aggregate tomorrow, the read feeding it returns fewer rows,
        // RemoveRange dutifully deletes the shortened list, the wiring scan stays green — and PII
        // survives account deletion. That is the vacuous-guarantee shape (#805-3/#868) in the one
        // path where it is an Art. 17 breach.
        //
        // The invariant, both directions:
        //   filtered   ⇒ IgnoreQueryFilters REQUIRED  — Art. 17 completeness (the load-bearing half).
        //   unfiltered ⇒ IgnoreQueryFilters FORBIDDEN — the anti-decoy half: a no-op call is a claim
        //                that a filter exists. Correlation was 12/12 before this PR, so in this
        //                codebase the call IS that claim; leaving one where it is false is how the
        //                next reader learns to distrust all twelve.
        //
        // The filtered set is read off the EF MODEL, never off a hand-kept list or a scan of the
        // Configurations folder — the model is what EF actually applies, and a second authority for
        // "what is filtered" is exactly the drift this guard exists to prevent.
        var body = HardDeleteCascadeScan.ExtractMethodBody(ReadHardDeleterSource(), CascadeMethodName);

        var violations = HardDeleteCascadeScan.FindQueryFilterViolations(
            body, CascadeMap.Values, FilteredCascadeDbSetNames());

        violations.ShouldBeEmpty(
            "IgnoreQueryFilters-invarianten i AccountHardDeleter.HardDeleteAccountAsync är bruten. " +
            "Regeln: en läsning mot ett aggregat MED query filter MÅSTE ha IgnoreQueryFilters " +
            "(annars smiter PII förbi Art. 17-raderingen — filtret krymper läsningen och " +
            "RemoveRange raderar bara det den fick), och en läsning mot ett aggregat UTAN filter får " +
            "INTE ha anropet (en no-op som påstår att ett filter finns = decoy, #868/#805-3). " +
            "Brott: " + string.Join(" | ", violations));
    }

    [Fact]
    public void QueryFilter_guard_flags_a_filtered_read_that_forgot_IgnoreQueryFilters()
    {
        // Self-proving negative (load-bearing direction): the Art. 17 hole this guard exists to
        // catch. CompanyWatches IS filtered; a read without the call must be reported.
        const string synthetic = """
            {
                var watches = await db.CompanyWatches
                    .Where(w => w.UserId == userId)
                    .ToListAsync(cancellationToken);
                db.CompanyWatches.RemoveRange(watches);
            }
            """;

        var violations = HardDeleteCascadeScan.FindQueryFilterViolations(
            synthetic, ["CompanyWatches"], new HashSet<string>(StringComparer.Ordinal) { "CompanyWatches" });

        violations.ShouldHaveSingleItem()
            .ShouldContain("CompanyWatches",
                customMessage: "en FILTRERAD läsning utan IgnoreQueryFilters måste rapporteras — " +
                "det är precis den tysta Art. 17-luckan vakten finns för");
    }

    [Fact]
    public void QueryFilter_guard_flags_an_unfiltered_read_that_carries_a_pointless_IgnoreQueryFilters()
    {
        // Self-proving negative (anti-decoy direction): this is the state THIS PR removed, encoded
        // so it cannot come back by hand. CompanyWatchCriteria has no filter; the call would be a
        // no-op asserting one.
        const string synthetic = """
            {
                var criteria = await db.CompanyWatchCriteria
                    .IgnoreQueryFilters()
                    .Where(c => c.UserId == userId)
                    .ToListAsync(cancellationToken);
                db.CompanyWatchCriteria.RemoveRange(criteria);
            }
            """;

        var violations = HardDeleteCascadeScan.FindQueryFilterViolations(
            synthetic, ["CompanyWatchCriteria"], new HashSet<string>(StringComparer.Ordinal));

        violations.ShouldHaveSingleItem()
            .ShouldContain("CompanyWatchCriteria",
                customMessage: "ett OFILTRERAT aggregat med IgnoreQueryFilters måste rapporteras — " +
                "annars är anropet en no-op som påstår att ett filter finns (#868-decoy-klassen)");
    }

    [Fact]
    public void QueryFilter_guard_exempts_RemoveRange_and_reads_code_not_comments()
    {
        // The two ways a naive version of this guard is WORSE than none.
        //
        // 1. RemoveRange/Remove take already-materialised entities: no query, no filter, nothing to
        //    ignore. Scoping to query-executing verbs is what keeps the guard from reporting the
        //    RemoveRange statement of every unfiltered aggregate and getting widened into uselessness
        //    on its first run (senior-cto-advisor H1b, "the trap that must not be got wrong").
        // 2. A comment that NAMES IgnoreQueryFilters is not a call. AccountHardDeleter's criteria arm
        //    carries exactly such a comment ("NO IgnoreQueryFilters, and that is enforced…"), and the
        //    deleted decoy guard fell for its own prose TWICE on two runs before it stripped literals.
        //    A guard that fires on documentation teaches people to delete documentation.
        const string synthetic = """
            {
                // This mentions IgnoreQueryFilters() in prose and must not count as a call.
                var criteria = await db.CompanyWatchCriteria
                    .Where(c => c.UserId == userId)
                    .ToListAsync(cancellationToken);
                db.CompanyWatchCriteria.RemoveRange(criteria);
            }
            """;

        HardDeleteCascadeScan.FindQueryFilterViolations(
                synthetic, ["CompanyWatchCriteria"], new HashSet<string>(StringComparer.Ordinal))
            .ShouldBeEmpty(
                "ett OFILTRERAT aggregat vars läsning saknar anropet är KORREKT — och varken " +
                "RemoveRange (ingen query) eller ordet IgnoreQueryFilters i en KOMMENTAR får " +
                "förvandla det till ett brott");
    }

    [Fact]
    public void Cascade_scan_flags_an_omitted_aggregate_even_when_referenced_for_a_read()
    {
        // Self-proving negative #1 (synthetic stand-in, never production). A read/count reference
        // must NOT satisfy the cascade — the exact false-pass a bare getter-invocation signal misses.
        const string syntheticAllButOne = """
            {
                db.Applications.RemoveRange(applications);
                db.Resumes.RemoveRange(resumes);
                db.SavedSearches.RemoveRange(savedSearches);
                db.RecentJobSearches.RemoveRange(recentSearches);
                db.SavedJobAds.RemoveRange(savedJobAds);
                db.CompanyWatches.RemoveRange(companyWatches);
                db.FollowedCompanyAdHits.RemoveRange(followedCompanyAdHits);
                db.CompanyWatchCriteria.RemoveRange(companyWatchCriteria);
                var queued = await db.UserJobAdMatches.Where(m => m.UserId == userId).CountAsync(ct);
                await db.ParsedResumes.Where(p => p.JobSeekerId == jsId).ExecuteDeleteAsync(ct);
                await db.ResumeFiles.Where(f => f.JobSeekerId == jsId).ExecuteDeleteAsync(ct);
            }
            """;

        var unwired = HardDeleteCascadeScan.FindUnwiredDbSets(syntheticAllButOne, CascadeMap.Values);

        unwired.ShouldBe(["UserJobAdMatches"],
            "Detektorn ska rapportera exakt det utelämnade aggregatet (UserJobAdMatches) som " +
            "oraderat — även om dess DbSet refereras för en read/count.");
    }

    [Fact]
    public void Partition_helper_flags_an_unclassified_aggregate_root()
    {
        // Self-proving negative #2: option (ii)'s HEADLINE guarantee can FAIL. A root in NEITHER
        // list is reported regardless of owner-key shape — here a synthetic root with NO recognisable
        // user key (the exact case a positive shape-predicate would have let pass). The fixture lives
        // in the TEST assembly, so it never pollutes the real Domain-assembly reflection.
        Type[] allRoots = [typeof(DomainApplication), typeof(UnclassifiedSampleAggregate)];
        Type[] classified = [typeof(DomainApplication)];

        HardDeleteCascadeScan.FindUnclassified(allRoots, classified)
            .ShouldBe([typeof(UnclassifiedSampleAggregate)],
                "partition-helpern ska rapportera ett aggregat som varken är klassificerat " +
                "user-owned eller NotUserOwned — oavsett nyckel-form (fail-closed default).");
    }

    [Fact]
    public void Method_body_scan_is_scoped_to_HardDeleteAccountAsync()
    {
        // Proves the brace-matched extraction isolates the target method: it sees HardDeleteAccountAsync's
        // real deletes but NOT a sibling method's body — so a delete verb in CleanupIdentityOrphansAsync
        // / GetAccountsReadyForHardDeleteAsync cannot falsely satisfy an aggregate the cascade omits.
        var body = HardDeleteCascadeScan.ExtractMethodBody(ReadHardDeleterSource(), CascadeMethodName);

        body.Contains("db.UserJobAdMatches.RemoveRange", StringComparison.Ordinal).ShouldBeTrue(
            "den extraherade kroppen ska innehålla HardDeleteAccountAsync:s riktiga deletes.");
        body.Contains("orphanIds", StringComparison.Ordinal).ShouldBeFalse(
            "den extraherade kroppen ska EXKLUDERA CleanupIdentityOrphansAsync (en syskon-metod) — " +
            "bevisar att brace-matchningen isolerar målmetoden och dödar whole-file-false-pass:en.");
    }

    /// <summary>
    /// The CascadeMap DbSet names whose entity type declares an EF query filter, read off the MODEL
    /// (the authority on what EF actually applies) — precedent <c>ErasureCascadeRegistryTests</c>
    /// / <c>JobAdFacetColumnMappingTests</c>. A hand-kept list, or a source-scan of the
    /// Configurations folder, would be a SECOND authority that can drift from the model: exactly the
    /// failure this guard exists to prevent (DRY as single knowledge authority).
    /// </summary>
    private static HashSet<string> FilteredCascadeDbSetNames()
    {
        using var db = ModelOnlyContext();

        var filtered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (clrType, dbSetName) in CascadeMap)
        {
            var entity = db.Model.FindEntityType(clrType);
            entity.ShouldNotBeNull(
                $"CascadeMap mappar {clrType.Name}, men EF-modellen känner inte typen — " +
                "vakten kan inte avgöra om den är filtrerad och skulle tyst svara 'ofiltrerad'.");

            if (entity!.GetDeclaredQueryFilters().Count > 0)
                filtered.Add(dbSetName);
        }

        // Fail-loud on a vacuous read: this repo filters most user-owned aggregates, so an EMPTY set
        // means the model API returned nothing useful (an EF upgrade reshaped the accessor) rather
        // than "nothing is filtered". Without this the guard would silently invert into "no arm may
        // carry the call" and demand the removal of eleven load-bearing ones.
        filtered.ShouldNotBeEmpty(
            "inget CascadeMap-aggregat rapporterades som filtrerat — det är inte troligt, det är en " +
            "trasig modell-avläsning (jfr EF 10:s named query filters). Vakten vore vakuös.");

        return filtered;
    }

    private static AppDbContext ModelOnlyContext()
    {
        // Model-only: the connection is never opened, so this needs no database. Precedent:
        // ErasureCascadeRegistryTests.ModelOnlyContext.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }

    private static HashSet<Type> ReflectAllAggregateRoots() =>
        typeof(AggregateRoot<>).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(IsAggregateRoot)
            .ToHashSet();

    private static bool IsAggregateRoot(Type type)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
            if (b.IsGenericType && b.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
                return true;
        return false;
    }

    private static string AdviseClassification(IEnumerable<Type> unclassified) =>
        string.Join(", ", unclassified.Select(t => LooksUserOwned(t)
            ? $"{t.Name} (ser user-owned ut — bär en JobSeekerId/UserId-liknande nyckel → troligen CascadeMap)"
            : $"{t.Name} (ingen uppenbar user-nyckel — NotUserOwned om det saknar PII)"));

    // ADVISORY ONLY — demoted from gate logic to operator hint. The gate is the fail-closed
    // partition; this heuristic merely nudges the human's classification. Being wrong here is
    // harmless: it cannot make the guard pass (Goodhart-safe), only mis-hint a message.
    private static bool LooksUserOwned(Type type)
    {
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        return props.Any(p =>
            (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) == typeof(JobSeekerId)
            || p.Name.EndsWith("UserId", StringComparison.Ordinal)
            || p.Name.EndsWith("SeekerId", StringComparison.Ordinal)
            || p.Name.EndsWith("OwnerId", StringComparison.Ordinal));
    }

    private static string ReadHardDeleterSource()
    {
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(
            repoRoot, HardDeleterRelativePath.Replace('/', Path.DirectorySeparatorChar));

        File.Exists(path).ShouldBeTrue(
            $"arch-testet pekar på en fil som inte finns: {path}. Uppdatera sökvägen om " +
            "AccountHardDeleter flyttats/döpts om.");

        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.ShouldNotBeNull(
            "kunde inte hitta repo-roten (CLAUDE.md) uppåt från test-bin — " +
            "arch-testet behöver källträdet för källtext-scan");
        return dir!.FullName;
    }

    private readonly record struct SampleId(Guid Value);

    // A synthetic aggregate root in NEITHER list, with no recognisable user-owner key — stands in
    // for "a new aggregate someone forgot to classify". In the TEST assembly, so it never reaches
    // the real Domain-assembly reflection.
    private sealed class UnclassifiedSampleAggregate : AggregateRoot<SampleId>;
}

/// <summary>
/// Pure detection helpers for the Art. 17 cascade fitness function (#374). Side-effect-free so each
/// is independently testable — see the self-proving negative tests in
/// <see cref="AccountHardDeleteCascadeFitnessTests"/>.
/// </summary>
internal static class HardDeleteCascadeScan
{
    // The deletion verbs the cascade uses to remove an FK-less aggregate's rows. RemoveRange before
    // Remove so the alternation prefers the longer token.
    private static readonly string[] DeletionVerbs = ["RemoveRange", "Remove", "ExecuteDeleteAsync"];

    /// <summary>
    /// Returns the aggregate roots in <paramref name="aggregateRoots"/> that are NOT in
    /// <paramref name="classified"/> — the fail-closed partition: an unclassified root of ANY shape
    /// is reported, so a new aggregate fails the build until a human files it under CascadeMap or
    /// NotUserOwned.
    /// </summary>
    internal static IReadOnlyList<Type> FindUnclassified(
        IEnumerable<Type> aggregateRoots, IEnumerable<Type> classified)
    {
        var known = classified.ToHashSet();
        return aggregateRoots.Where(t => !known.Contains(t)).ToList();
    }

    /// <summary>
    /// Returns the DbSet names from <paramref name="expectedDbSetNames"/> NOT wired to a deletion
    /// verb in <paramref name="source"/>. A name counts as wired only when <c>db.&lt;Name&gt;</c> is
    /// bound to one of RemoveRange/Remove/ExecuteDeleteAsync within the SAME statement (no intervening
    /// <c>;</c>), so a DbSet referenced only for a read/count does NOT count. The caller passes the
    /// scoped method body, so a sibling method's delete verb cannot satisfy a name here.
    /// </summary>
    internal static IReadOnlyList<string> FindUnwiredDbSets(
        string source, IEnumerable<string> expectedDbSetNames)
    {
        var verbs = string.Join("|", DeletionVerbs);

        return expectedDbSetNames
            .Where(name => !IsWiredToDeletion(source, name, verbs))
            .ToList();
    }

    // Verbs that EXECUTE a query — i.e. the statements a query filter can silently narrow. Remove /
    // RemoveRange are deliberately ABSENT: they take already-materialised entities, run no query,
    // and have nothing to ignore. Including them would report the RemoveRange statement of every
    // unfiltered aggregate on the guard's first run (senior-cto-advisor H1b).
    private static readonly string[] QueryExecutingVerbs =
    [
        "ToListAsync", "ToArrayAsync", "CountAsync", "AnyAsync",
        "FirstOrDefaultAsync", "FirstAsync", "SingleOrDefaultAsync", "SingleAsync",
        "ExecuteDeleteAsync", "ExecuteUpdateAsync",
    ];

    /// <summary>
    /// Reports every statement in <paramref name="source"/> that executes a query against one of
    /// <paramref name="dbSetNames"/> whose <c>IgnoreQueryFilters()</c> presence disagrees with
    /// <paramref name="filteredDbSetNames"/> — in BOTH directions (filtered ⇒ required;
    /// unfiltered ⇒ forbidden).
    /// </summary>
    internal static IReadOnlyList<string> FindQueryFilterViolations(
        string source, IEnumerable<string> dbSetNames, ISet<string> filteredDbSetNames)
    {
        // CODE ONLY. A comment naming IgnoreQueryFilters is not a call, and a guard that fires on
        // prose trains people to delete prose — the deleted CompanyWatchCriterionSoftDeleteDecoyTests
        // learned this twice on two runs before it stripped literals first.
        var code = StripCommentsAndStrings(source);
        var verbs = string.Join("|", QueryExecutingVerbs);
        var violations = new List<string>();

        // Statement-scoped: ';' cannot appear inside a fluent chain, so it is the statement boundary
        // — the same reach the wiring scan's [^;] class relies on.
        foreach (var statement in code.Split(';'))
        {
            foreach (var name in dbSetNames)
            {
                if (!Regex.IsMatch(statement, $@"\bdb\.{Regex.Escape(name)}\b"))
                    continue;

                if (!Regex.IsMatch(statement, $@"\bdb\.{Regex.Escape(name)}\b.*?\.(?:{verbs})\(",
                        RegexOptions.Singleline))
                    continue;   // not a query-executing statement (e.g. the RemoveRange call)

                var hasCall = statement.Contains(".IgnoreQueryFilters(", StringComparison.Ordinal);
                var mustHaveCall = filteredDbSetNames.Contains(name);

                if (hasCall == mustHaveCall)
                    continue;

                violations.Add(mustHaveCall
                    ? $"db.{name}: FILTRERAT aggregat läses UTAN IgnoreQueryFilters → filtret " +
                      "krymper Art. 17-läsningen och rader överlever konto-raderingen"
                    : $"db.{name}: OFILTRERAT aggregat läses MED IgnoreQueryFilters → anropet är en " +
                      "no-op som påstår att ett filter finns (decoy)");
            }
        }

        return violations;
    }

    // Strips what code never lives in — string literals first (a // inside a string is not a
    // comment), then block comments, then line comments. Mirrors the stripper the demolished
    // CompanyWatchCriterionSoftDeleteDecoyTests carried; kept here because this guard reads the same
    // kind of prose-rich source.
    private static string StripCommentsAndStrings(string source)
    {
        var s = Regex.Replace(source, "\"\"\".*?\"\"\"", "\"\"", RegexOptions.Singleline);
        s = Regex.Replace(s, "@\"(?:[^\"]|\"\")*\"", "\"\"");
        s = Regex.Replace(s, "\"(?:[^\"\\\\\\r\\n]|\\\\.)*\"", "\"\"");
        s = Regex.Replace(s, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(s, @"//[^\r\n]*", "");
    }

    private static bool IsWiredToDeletion(string source, string dbSetName, string verbs) =>
        // \bdb\.<Name>\b  — the DbSet getter access on the `db` context field.
        // [^;]*?          — same-statement reach (the char class excludes ';' so it can never cross a
        //                   statement boundary, but DOES span newlines → the multi-line
        //                   db.X.IgnoreQueryFilters().Where(...).ExecuteDeleteAsync(...) chain).
        // \.(?:verbs)\(   — a deletion verb invoked on that DbSet / its query.
        Regex.IsMatch(source, $@"\bdb\.{Regex.Escape(dbSetName)}\b[^;]*?\.(?:{verbs})\(");

    /// <summary>
    /// Extracts the body (braces included) of the method named <paramref name="methodName"/> from
    /// C# <paramref name="source"/> by comment-/string-aware brace matching, so the cascade scan is
    /// scoped to one method. Assumes no raw-string (<c>"""</c>) or interpolated-string literals in
    /// the scanned method (true for AccountHardDeleter — it is pure EF operations + comments).
    /// </summary>
    internal static string ExtractMethodBody(string source, string methodName)
    {
        var sig = source.IndexOf(methodName + "(", StringComparison.Ordinal);
        if (sig < 0)
            throw new InvalidOperationException($"method '{methodName}' not found in source");

        var open = source.IndexOf('{', sig);
        if (open < 0)
            throw new InvalidOperationException($"no body brace found after '{methodName}'");

        var depth = 0;
        var state = ScanState.Code;

        for (var i = open; i < source.Length; i++)
        {
            var c = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            switch (state)
            {
                case ScanState.Code:
                    if (c == '/' && next == '/') { state = ScanState.LineComment; i++; }
                    else if (c == '/' && next == '*') { state = ScanState.BlockComment; i++; }
                    else if (c == '@' && next == '"') { state = ScanState.VerbatimString; i++; }
                    else if (c == '"') state = ScanState.String;
                    else if (c == '\'') state = ScanState.Char;
                    else if (c == '{') depth++;
                    else if (c == '}' && --depth == 0) return source[open..(i + 1)];
                    break;
                case ScanState.LineComment:
                    if (c == '\n') state = ScanState.Code;
                    break;
                case ScanState.BlockComment:
                    if (c == '*' && next == '/') { state = ScanState.Code; i++; }
                    break;
                case ScanState.String:
                    if (c == '\\') i++;            // skip the escaped character
                    else if (c == '"') state = ScanState.Code;
                    break;
                case ScanState.VerbatimString:
                    if (c == '"' && next == '"') i++;   // "" escape inside @"..."
                    else if (c == '"') state = ScanState.Code;
                    break;
                case ScanState.Char:
                    if (c == '\\') i++;
                    else if (c == '\'') state = ScanState.Code;
                    break;
            }
        }

        throw new InvalidOperationException($"unbalanced braces extracting '{methodName}'");
    }

    private enum ScanState { Code, LineComment, BlockComment, String, VerbatimString, Char }
}
