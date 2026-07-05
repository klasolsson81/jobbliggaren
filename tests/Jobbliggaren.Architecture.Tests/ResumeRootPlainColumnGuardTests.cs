using System.IO;
using System.Reflection;
using Ardalis.SmartEnum;
using Jobbliggaren.Application.Resumes.Queries;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Fas 4b PR-3 (#652, epic #649 — ADR 0096, CTO-bind D1/D5d/D9).
///
/// <para>
/// Invariant: the <c>resumes</c> root stores CV free text ONLY as the encrypted
/// <c>content_enc</c> shadow on <c>resume_versions</c> (ADR 0049 Form B) or as the
/// three ADR 0059 denormalized projections (<c>LatestRole</c>/<c>SectionCount</c>/
/// <c>TopSkills</c>, derived — never raw prose). The PR-3 source-metadata/template
/// surface (<c>Origin</c>/<c>AdoptedAt</c>/<c>TemplateOptions</c>) must stay provably
/// non-PII: every member is a closed SmartEnum, a bool, or a timestamp — deliberately
/// ZERO free-text members, which is what makes the plain-column mapping sound
/// (parity with AdSnapshot's "plaintext, NO DEK" ruling, ADR 0086 D5).
/// </para>
///
/// <para>
/// Form (CTO "precision over breadth" + Saltzer &amp; Schroeder fail-safe default,
/// mirroring <c>ResumeContentPersonnummerGuardTests</c>): a reflection allowlist over
/// the aggregate's public surface — a NEW property on <c>Resume</c> or a NEW member on
/// <c>CvTemplateOptions</c> fails this test until a human classifies it here with a
/// reason. Plus a containment probe: the enumerated config types must never migrate
/// into the encrypted/personnummer-scanned content graph (<c>ResumeContent</c>/
/// <c>ResumeContentDto</c>), and vice versa the config must never grow a string.
/// </para>
/// </summary>
public class ResumeRootPlainColumnGuardTests
{
    /// <summary>
    /// Explicit, reason-carrying classification of every public instance property on
    /// the Resume root. Fail-safe default: an unlisted property fails the build until
    /// classified. "PlainNonPii" entries additionally have their CLR type pinned by
    /// <see cref="ResumeRoot_Pr3Surface_IsFullyEnumerated_NoFreeText"/>.
    /// </summary>
    private static readonly Dictionary<string, string> ClassifiedRootProperties = new(StringComparer.Ordinal)
    {
        // Identity / lifecycle (pre-existing)
        [nameof(Resume.Id)] = "strongly-typed id",
        [nameof(Resume.JobSeekerId)] = "strongly-typed FK",
        [nameof(Resume.Name)] = "internal CV display name (user label, max 200; never CV content — pre-existing)",
        [nameof(Resume.Language)] = "SmartEnum, ADR 0058",
        [nameof(Resume.CreatedAt)] = "timestamp",
        [nameof(Resume.UpdatedAt)] = "timestamp",
        [nameof(Resume.DeletedAt)] = "soft-delete stamp",
        // ADR 0059 denormalized projections (derived from encrypted Content inside
        // aggregate methods — bounded, never raw prose)
        [nameof(Resume.LatestRole)] = "ADR 0059 denorm projection",
        [nameof(Resume.SectionCount)] = "ADR 0059 denorm projection",
        [nameof(Resume.TopSkills)] = "ADR 0059 denorm projection",
        // Aggregate machinery
        [nameof(Resume.Versions)] = "child collection (content lives encrypted on the child)",
        [nameof(Resume.MasterVersion)] = "computed getter (not mapped)",
        [nameof(Resume.DomainEvents)] = "EF-ignored, raise-only",
        // Fas 4b PR-3 (ADR 0096) — the surface this guard exists for
        [nameof(Resume.Origin)] = "SmartEnum provenance, set by construction (ADR 0096)",
        [nameof(Resume.AdoptedAt)] = "one-way adoption stamp (ADR 0096)",
        [nameof(Resume.IsAdopted)] = "computed (EF-ignored)",
        [nameof(Resume.TemplateOptions)] = "owned VO, fully enumerated (ADR 0096)",
        // Fas 4b PR-4 (ADR 0093 §D2(e), lokal ADR 0097) — parity with the Versions entry
        [nameof(Resume.FindingStatuses)] =
            "child collection (DEK-free status ledger: closed enum + bounded tokens + " +
            "fingerprint + timestamps; content lives nowhere — shape pinned fail-closed " +
            "by ResumeFindingStatusColumnGuardTests)",
    };

    [Fact]
    public void ResumeRoot_EveryPublicProperty_IsClassified()
    {
        var actual = typeof(Resume)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        // Staleness anchors: the properties this guard was written for must exist,
        // else the allowlist has drifted from the aggregate (loud, not silent).
        actual.ShouldContain(nameof(Resume.Origin));
        actual.ShouldContain(nameof(Resume.AdoptedAt));
        actual.ShouldContain(nameof(Resume.TemplateOptions));

        var unclassified = actual.Where(n => !ClassifiedRootProperties.ContainsKey(n)).ToList();
        unclassified.ShouldBeEmpty(
            "Every public property on the Resume root must be classified in " +
            "ResumeRootPlainColumnGuardTests.ClassifiedRootProperties with a reason " +
            "(ADR 0096 fail-closed plain-column discipline: CV free text lives ONLY in " +
            "the encrypted content_enc shadow or the ADR 0059 projections — a new " +
            "plaintext root column is a deliberate, reviewed decision, never a drive-by). " +
            "Unclassified: " + string.Join(", ", unclassified));

        var stale = ClassifiedRootProperties.Keys.Where(n => !actual.Contains(n)).ToList();
        stale.ShouldBeEmpty(
            "Allowlist entries no longer on Resume (rename/removal must update this " +
            "guard in the same change): " + string.Join(", ", stale));
    }

    [Fact]
    public void ResumeRoot_Pr3Surface_IsFullyEnumerated_NoFreeText()
    {
        // The ADR 0096 non-PII ruling rests on these exact CLR shapes — pin them.
        typeof(Resume).GetProperty(nameof(Resume.Origin))!.PropertyType
            .ShouldBe(typeof(ResumeSourceOrigin));
        typeof(Resume).GetProperty(nameof(Resume.AdoptedAt))!.PropertyType
            .ShouldBe(typeof(DateTimeOffset?));
        typeof(Resume).GetProperty(nameof(Resume.TemplateOptions))!.PropertyType
            .ShouldBe(typeof(CvTemplateOptions));

        // Fail-closed over the owned VO: every public member must be a closed
        // SmartEnum or a bool. A string (or any other free-form type) added to
        // CvTemplateOptions would silently become a plaintext column — refuse it
        // here until it is deliberately re-classified (and, if free text, moved to
        // the encrypted surface instead, ADR 0049).
        var offending = typeof(CvTemplateOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != nameof(CvTemplateOptions.IsComplete)) // computed, EF-ignored
            .Where(p => !IsSmartEnum(p.PropertyType) && p.PropertyType != typeof(bool))
            .Select(p => $"{p.Name}: {p.PropertyType.Name}")
            .ToList();

        offending.ShouldBeEmpty(
            "CvTemplateOptions must stay fully enumerated (SmartEnum/bool members " +
            "only) — that is what makes its plain-column mapping non-PII by " +
            "construction (ADR 0096; CLAUDE.md §5). Free text (incl. a filename or a " +
            "free hex color) belongs in the encrypted surface or a curated SmartEnum. " +
            "Offending: " + string.Join(", ", offending));
    }

    /// <summary>
    /// Containment both ways (ADR 0096): the enumerated display/provenance config must
    /// never migrate INTO the encrypted, personnummer-scanned content graph — and no
    /// content type may leak onto the config. A relocation in either direction would
    /// silently change the field's encryption/scan posture without a decision.
    /// </summary>
    [Theory]
    [InlineData(typeof(ResumeContent))]
    [InlineData(typeof(ResumeContentDto))]
    public void EncryptedContentGraph_MustNotReference_Pr3ConfigTypes(Type contentRoot)
    {
        Type[] configTypes =
        [
            typeof(ResumeSourceOrigin), typeof(CvTemplateOptions), typeof(CvTemplate),
            typeof(CvAccentColor), typeof(CvFontPair), typeof(CvDensity), typeof(CvPhotoShape),
        ];

        var visited = new HashSet<Type>();
        var queue = new Queue<Type>();
        queue.Enqueue(contentRoot);

        var violations = new List<string>();
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (!visited.Add(type))
                continue;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var reached in Unwrap(property.PropertyType))
                {
                    if (configTypes.Contains(reached))
                        violations.Add($"{type.Name}.{property.Name} → {reached.Name}");
                    else if (reached.Namespace?.StartsWith("Jobbliggaren", StringComparison.Ordinal) == true)
                        queue.Enqueue(reached);
                }
            }
        }

        violations.ShouldBeEmpty(
            $"{contentRoot.Name} (the encrypted/pnr-scanned content graph) must not " +
            "reference the PR-3 enumerated config types — display/provenance config " +
            "lives as plain columns on the Resume root (ADR 0096), CV content lives in " +
            "content_enc (ADR 0049); relocating either silently changes its " +
            "encryption/scan posture. Violations: " + string.Join(", ", violations));
    }

    /// <summary>
    /// Precision-over-breadth source-scan (EncryptedFieldProjectionGuardTests idiom):
    /// the Resume EF config must keep the PR-3 columns as discrete Name-string
    /// columns — never a JSON blob (a second non-encrypted JSON surface was
    /// explicitly rejected, CTO-bind Fork 2) — and must keep the explicit
    /// snake_case column names the migration pinned.
    /// </summary>
    [Fact]
    public void ResumeConfiguration_KeepsPr3Columns_PlainAndDiscrete()
    {
        var repoRoot = FindRepoRoot();
        var configPath = Path.Combine(repoRoot,
            "src", "Jobbliggaren.Infrastructure", "Persistence", "Configurations",
            "ResumeConfiguration.cs");

        File.Exists(configPath).ShouldBeTrue(configPath);
        var source = File.ReadAllText(configPath);

        source.Contains(".ToJson(", StringComparison.Ordinal).ShouldBeFalse(
            "TemplateOptions maps to discrete plain columns (CTO-bind Fork 2a; " +
            "ManualPosting/AdSnapshot precedent) — OwnsOne(...).ToJson() would " +
            "introduce a second non-encrypted JSON surface, explicitly rejected.");

        foreach (var column in (string[])
                 ["\"origin\"", "\"adopted_at\"", "\"template\"", "\"template_accent\"",
                  "\"template_font\"", "\"template_density\"", "\"template_photo_enabled\"",
                  "\"template_photo_shape\""])
        {
            source.Contains(column, StringComparison.Ordinal).ShouldBeTrue(
                $"ResumeConfiguration must keep the explicit {column} HasColumnName " +
                "(ADR 0096 column contract — the migration and this guard pin the same " +
                "names; a rename is a schema change, not a refactor).");
        }
    }

    private static bool IsSmartEnum(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(SmartEnum<,>))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Unwraps Nullable&lt;T&gt;, arrays, and generic collections so the graph walk sees
    /// element types (the array branch is defense-in-depth — content DTOs use
    /// IReadOnlyList&lt;T&gt; today, but a future T[] member must not stop the walk;
    /// flagged independently by security-auditor and code-reviewer, 2026-07-05).
    /// </summary>
    private static IEnumerable<Type> Unwrap(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            yield return underlying;
            yield break;
        }

        if (type.IsArray && type.GetElementType() is { } element)
        {
            yield return element;
            yield break;
        }

        if (type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
                yield return arg;
            yield break;
        }

        yield return type;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.ShouldNotBeNull(
            "could not locate the repo root (CLAUDE.md) walking up from the test bin — " +
            "the source-scan needs the source tree");
        return dir!.FullName;
    }
}
