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
    // told to use this ctor for round-trip deserialization. #426: foundInFileName is a
    // trailing bool, so an outcome persisted before this slice carries no key for it and
    // STJ supplies the parameter default (false) — additive, back-compat, no migration
    // (jsonb missing-key → CLR default; the safe value, never a false alarm).
    [JsonConstructor]
    private PersonnummerScanOutcome(
        bool found, int count, IReadOnlyList<PersonnummerKind> kinds, bool foundInFileName)
    {
        Found = found;
        Count = count;
        Kinds = kinds;
        FoundInFileName = foundInFileName;
    }

    /// <summary>True if at least one personnummer/samordningsnummer was detected in the CV
    /// BODY TEXT. This is the invariant-bearing signal: it drives the promotion block
    /// (ADR 0074 Invariant 1) and the B4 body auto-fail. A filename-only detection does NOT
    /// set this — see <see cref="FoundInFileName"/>.</summary>
    public bool Found { get; }

    /// <summary>How many BODY detections (not de-duplicated — a number repeated twice
    /// counts twice; the user is asked to remove all of them). Filename detections are a
    /// bool flag only (<see cref="FoundInFileName"/>), never folded into this count.</summary>
    public int Count { get; }

    /// <summary>The distinct kinds detected in the body, ordered. Empty when the body is
    /// clean.</summary>
    public IReadOnlyList<PersonnummerKind> Kinds { get; }

    /// <summary>
    /// True if a personnummer/samordningsnummer was detected in the SOURCE FILE NAME
    /// (defense-in-depth, #426) — a signal SEPARATE from the body scan above. Deliberately
    /// a bool only: the filename channel needs a flag to prompt a rename, not a count/kind
    /// breakdown, and it stays PII-safe by construction (no raw value, no offset). A
    /// filename-only detection does NOT set <see cref="Found"/>, so it does NOT block
    /// promotion (the invariant is body PII; the filename never reaches the canonical
    /// Resume) — it is surfaced as a B4 Warn so the user renames the file. Non-PII, safe to
    /// log; an outcome persisted before #426 deserializes this to the safe default false.
    /// </summary>
    public bool FoundInFileName { get; }

    /// <summary>The clean outcome — nothing detected, in body or filename.</summary>
    public static PersonnummerScanOutcome None { get; } = new(false, 0, [], false);

    /// <summary>
    /// Projects BODY-text scanner matches into the PII-safe outcome. The matches' offsets and
    /// masked forms are intentionally discarded here — only the count and distinct kinds
    /// survive (no PII, no reconstruction surface). <paramref name="foundInFileName"/> (#426)
    /// carries the SEPARATE filename-scan result as a bool; it never contributes to
    /// <see cref="Found"/>/<see cref="Count"/>/<see cref="Kinds"/> (those stay body-exclusive).
    /// When the body is clean AND the filename flag is false, the shared <see cref="None"/>
    /// singleton is returned.
    /// </summary>
    public static PersonnummerScanOutcome FromMatches(
        IReadOnlyList<PersonnummerMatch> matches, bool foundInFileName = false)
    {
        var hasBody = matches is not null && matches.Count > 0;
        if (!hasBody && !foundInFileName)
        {
            return None;
        }

        var kinds = hasBody
            ? matches!
                .Select(m => m.Kind)
                .Distinct()
                .OrderBy(k => (int)k)
                .ToList()
            : (IReadOnlyList<PersonnummerKind>)[];

        return new PersonnummerScanOutcome(
            hasBody, hasBody ? matches!.Count : 0, kinds, foundInFileName);
    }
}
