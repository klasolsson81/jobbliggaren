using System.Text.Json.Serialization;

namespace Jobbliggaren.Domain.Privacy;

/// <summary>
/// The PII-safe summary of a personnummer scan over a body of text (the F4-8
/// call-site result, ADR 0074 Invariant 1). Deliberately carries ONLY a count and
/// the distinct kinds — never a raw value, never offsets into the source text.
/// Surfacing offsets into persisted PII would itself be a reconstruction aid
/// (dotnet-architect/security-auditor ruling) — the guard FLAGS ("we found N
/// personnummer, remove them"), it never points at byte ranges. Safe to log.
/// </summary>
public sealed record PersonnummerScanOutcome
{
    // [JsonConstructor]: this outcome is persisted as jsonb on ParsedResume (F4-8).
    // The construction surface stays private (no public add/echo of PII), so STJ is
    // told to use this ctor for round-trip deserialization.
    [JsonConstructor]
    private PersonnummerScanOutcome(bool found, int count, IReadOnlyList<PersonnummerKind> kinds)
    {
        Found = found;
        Count = count;
        Kinds = kinds;
    }

    /// <summary>True if at least one personnummer/samordningsnummer was detected.</summary>
    public bool Found { get; }

    /// <summary>How many detections (not de-duplicated — a number repeated twice
    /// counts twice; the user is asked to remove all of them).</summary>
    public int Count { get; }

    /// <summary>The distinct kinds detected, ordered. Empty when nothing was found.</summary>
    public IReadOnlyList<PersonnummerKind> Kinds { get; }

    /// <summary>The clean outcome — nothing detected.</summary>
    public static PersonnummerScanOutcome None { get; } = new(false, 0, []);

    /// <summary>
    /// Projects scanner matches into the PII-safe outcome. The matches' offsets and
    /// masked forms are intentionally discarded here — only the count and distinct
    /// kinds survive (no PII, no reconstruction surface).
    /// </summary>
    public static PersonnummerScanOutcome FromMatches(IReadOnlyList<PersonnummerMatch> matches)
    {
        if (matches is null || matches.Count == 0)
        {
            return None;
        }

        var kinds = matches
            .Select(m => m.Kind)
            .Distinct()
            .OrderBy(k => (int)k)
            .ToList();

        return new PersonnummerScanOutcome(true, matches.Count, kinds);
    }
}
