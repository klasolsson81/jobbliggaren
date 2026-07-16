namespace Jobbliggaren.Domain.Privacy;

/// <summary>
/// One detected recruiter contact span (#842 Tier A). <paramref name="Raw"/> is the text as it
/// stood in the body; <paramref name="Normalized"/> is the canonical comparison form, computed by
/// the SAME normalizer the recogniser matched with
/// (<see cref="RecruiterContactRedactor.NormalizeEmail"/> /
/// <see cref="RecruiterContactRedactor.NormalizePhone"/>).
/// </summary>
/// <remarks>
/// The recogniser owns the question, the split AND the normalization — every consumer (the
/// promote/merge step in <c>AdContacts</c>) compares on <see cref="Normalized"/> and never
/// re-normalizes. A rule with two normalisers is two rules: #844 shipped seven manifestations of
/// exactly that, all the same silent loss. No offsets are exposed — the redactor performs its own
/// in-place replacement (unlike <c>PersonnummerScanner</c>, whose caller masks by span).
/// </remarks>
public readonly record struct ContactSpan(string Raw, string Normalized, ContactKind Kind);
