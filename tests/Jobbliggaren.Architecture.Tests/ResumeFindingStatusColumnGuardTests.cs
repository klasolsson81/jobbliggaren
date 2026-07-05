using System.IO;
using System.Reflection;
using Ardalis.SmartEnum;
using Jobbliggaren.Domain.Resumes;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Fas 4b PR-4 (#653, ADR 0093 §D2(e); CTO-bind PR-4 Q1 DoD 2/4).
///
/// <para>
/// The D2(e) fitness function — "the arch-test forbids free-text columns" on the
/// DEK-free finding-status ledger, expressed as a column-TYPE allowlist (never content
/// inspection): every mapped property on <see cref="ResumeFindingStatus"/> must be a
/// strongly-typed id, a closed SmartEnum, a bounded machine token, a fixed-length hex
/// digest, or a timestamp. NO CV text at rest is what lets this table live outside the
/// DEK envelope (ADR 0074 compute-on-demand stays intact — review RESULTS are never
/// persisted; this is a pure status ledger). Fail-safe default: a NEW property fails
/// until a human classifies it here with a reason.
/// </para>
/// </summary>
public class ResumeFindingStatusColumnGuardTests
{
    /// <summary>
    /// Explicit, reason-carrying classification of every public instance property.
    /// String-typed entries additionally have their bounded shape pinned by
    /// <see cref="LedgerEntity_StringProperties_AreBoundedTokens_NeverFreeText"/>.
    /// </summary>
    private static readonly Dictionary<string, string> ClassifiedProperties = new(StringComparer.Ordinal)
    {
        [nameof(ResumeFindingStatus.Id)] = "strongly-typed id",
        [nameof(ResumeFindingStatus.RubricVersion)] =
            "bounded machine token (major.minor.patch, max 14; aggregate-validated)",
        [nameof(ResumeFindingStatus.CriterionId)] =
            "bounded machine token (rubric id A1..E12, max 3; aggregate-validated)",
        [nameof(ResumeFindingStatus.Status)] = "closed SmartEnum (Open/Resolved/Ignored)",
        [nameof(ResumeFindingStatus.TargetFingerprint)] =
            "fixed-length SHA-256 hex digest (64; one-way — never CV text, CTO-bind Q4)",
        [nameof(ResumeFindingStatus.StaleAt)] = "staleness timestamp (CTO-bind Q3)",
        [nameof(ResumeFindingStatus.CreatedAt)] = "timestamp",
        [nameof(ResumeFindingStatus.UpdatedAt)] = "timestamp",
    };

    // The only CLR types a ledger property may have — free-form types (raw string is
    // special-cased in the string-shape test below) can never appear unclassified.
    private static readonly Type[] PermittedPropertyTypes =
    [
        typeof(ResumeFindingStatusId),
        typeof(ReviewFindingStatus),
        typeof(string),
        typeof(DateTimeOffset),
        typeof(DateTimeOffset?),
    ];

    [Fact]
    public void LedgerEntity_EveryPublicProperty_IsClassified()
    {
        var actual = typeof(ResumeFindingStatus)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        // Staleness anchors: the D2(e) key + fingerprint must exist, else the
        // allowlist has drifted from the entity (loud, not silent).
        actual.ShouldContain(nameof(ResumeFindingStatus.RubricVersion));
        actual.ShouldContain(nameof(ResumeFindingStatus.CriterionId));
        actual.ShouldContain(nameof(ResumeFindingStatus.TargetFingerprint));

        var unclassified = actual.Where(n => !ClassifiedProperties.ContainsKey(n)).ToList();
        unclassified.ShouldBeEmpty(
            "Every public property on ResumeFindingStatus must be classified in " +
            "ResumeFindingStatusColumnGuardTests.ClassifiedProperties with a reason " +
            "(ADR 0093 §D2(e): the ledger is DEK-free BECAUSE no CV text can rest here " +
            "— a new column is a deliberate, reviewed decision, never a drive-by). " +
            "Unclassified: " + string.Join(", ", unclassified));

        var stale = ClassifiedProperties.Keys.Where(n => !actual.Contains(n)).ToList();
        stale.ShouldBeEmpty(
            "Allowlist entries no longer on ResumeFindingStatus (rename/removal must " +
            "update this guard in the same change): " + string.Join(", ", stale));
    }

    [Fact]
    public void LedgerEntity_PropertyTypes_AreClosedShapes_Only()
    {
        var offending = typeof(ResumeFindingStatus)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !PermittedPropertyTypes.Contains(p.PropertyType))
            .Select(p => $"{p.Name}: {p.PropertyType.Name}")
            .ToList();

        offending.ShouldBeEmpty(
            "ResumeFindingStatus properties must stay id/SmartEnum/bounded-string/" +
            "timestamp — any other CLR type is an unreviewed at-rest surface on the " +
            "DEK-free ledger (ADR 0093 §D2(e)). Offending: " + string.Join(", ", offending));

        // The Status property must be a genuine closed SmartEnum (locked-set parity
        // with CvReviewEngineLayerTests) — pin both the type and its member set.
        IsSmartEnum(typeof(ReviewFindingStatus)).ShouldBeTrue();
        // The finding-status vocabulary is a closed set (handoff §5.3: öppen/åtgärdad/
        // ignorerad); staleness rides orthogonally on StaleAt, NEVER as a fourth status
        // (CTO-bind PR-4 Q3).
        ReviewFindingStatus.List.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(["Ignored", "Open", "Resolved"]);
    }

    [Fact]
    public void LedgerEntity_StringProperties_AreBoundedTokens_NeverFreeText()
    {
        // The three string columns are machine tokens with aggregate-enforced shapes —
        // pin the EF mapping bounds so a widened column (the free-text smuggling path)
        // fails here. Source-scan parity with ResumeRootPlainColumnGuardTests (Q1 DoD 4).
        var repoRoot = FindRepoRoot();
        var configPath = Path.Combine(repoRoot,
            "src", "Jobbliggaren.Infrastructure", "Persistence", "Configurations",
            "ResumeFindingStatusConfiguration.cs");

        File.Exists(configPath).ShouldBeTrue(configPath);
        var source = File.ReadAllText(configPath);

        source.Contains(".ToJson(", StringComparison.Ordinal).ShouldBeFalse(
            "the ledger maps to discrete plain columns — a JSON blob would degrade the " +
            "forbid-free-text guarantee from column-type enforcement to content " +
            "inspection (CTO-bind PR-4 Q1, rejected variant C).");

        foreach (var pin in (string[])
                 ["\"rubric_version\"", "\"criterion_id\"", "\"status\"",
                  "\"target_fingerprint\"", "\"stale_at\"",
                  "HasMaxLength(14)", "HasMaxLength(3)", "HasMaxLength(64)", "IsFixedLength()"])
        {
            source.Contains(pin, StringComparison.Ordinal).ShouldBeTrue(
                $"ResumeFindingStatusConfiguration must keep {pin} — the snake_case " +
                "column contract + bounded token lengths are what make every column " +
                "provably non-free-text (ADR 0093 §D2(e); the migration pins the same shapes).");
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
