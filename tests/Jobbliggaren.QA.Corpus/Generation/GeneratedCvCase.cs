using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.QA.Corpus.Generation;

/// <summary>
/// A single reviewer corpus case: a real <see cref="ParsedResume"/> aggregate (built via the
/// real <c>ParsedResume.Create</c> factory — the same path the import handler uses) plus its
/// stratum and observe-only expectations.
///
/// <para><b>PII discipline (ADR 0074 Invariant 1):</b> a <see cref="CorpusStratum.FakePersonnummer"/>
/// case carries a Luhn-valid fake personnummer INSIDE <see cref="ParsedResume"/> (raw text /
/// experience), and <see cref="ExpectsPersonnummerFlagged"/> records that the review engine's
/// B4 criterion should surface it. The case never exposes the raw value through
/// <see cref="Label"/> or any property — reports and logs cite only the PII-safe
/// <c>PersonnummerScanOutcome</c> (Found/Count/Kinds) that already rides on the aggregate.</para>
/// </summary>
public sealed record GeneratedCvCase(
    int Index,
    CorpusStratum Stratum,
    ParsedResume Cv,
    bool ExpectsPersonnummerFlagged)
{
    /// <summary>Stable, PII-safe case label for reports and assertion messages.</summary>
    public string Label => $"{Stratum}#{Index}";
}
