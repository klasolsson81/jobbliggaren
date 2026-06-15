namespace Jobbliggaren.Application.Resumes.Improvement.Abstractions;

/// <summary>
/// A structural (non-text-replacement) operation: a removal, a reorder, or a sanitization
/// (Fas 4 STEG 10, F4-10). <paramref name="Kind"/> declares the pure transform;
/// <paramref name="Target"/> is a PII-SAFE structural descriptor of WHAT is operated on
/// (e.g. "personnummer", "utbildningspost 2", "sektionsordning") — NEVER the raw PII value
/// or a byte offset (parity the no-offset contract of <c>PersonnummerScanOutcome</c>).
/// </summary>
public sealed record StructuralOperation(StructuralTransformKind Kind, string Target);
