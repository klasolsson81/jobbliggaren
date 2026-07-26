using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Application.Resumes.Common;

/// <summary>
/// Resolves the LABEL an imported CV is filed under (<c>Resume.Name</c>) — a use-case default,
/// deliberately separate from the person's name that goes into the content (#1060).
///
/// <para>The two are different concepts in different data-protection classes:
/// <c>Resume.Name</c> is a plaintext column that surfaces in CV lists, and its classification
/// rests on it being a LABEL (see <c>Resume.ValidateName</c>'s remarks), while
/// <c>PersonalInfo.FullName</c> rides the DEK-encrypted content shadow. Until #1060 one string
/// fed both, so labelling a CV "Backend-CV 2026" printed that where the person's name belongs.</para>
///
/// <para><b>The default is system-generated and non-PII by construction</b> (CTO-bind D5-REBIND-2):
/// no user text, no file text, no account text. Two defaults were considered and rejected:
/// the ACCOUNT NAME puts the person's name back into the plaintext column for every user who
/// never edits it — the separation would exist in the schema and be undone on the first screen;
/// the FILE NAME was refused outright by ADR 0096 D-B ("PII-near — it can carry a name"), whose
/// Alt B ("store the filename plaintext on Resume now") is a recorded rejection, and it would
/// additionally outlive the staging-retention rule written for <c>SourceFileName</c>
/// (Art. 5(1)(e)) and falsify the documented rule that a filename never reaches the canonical
/// Resume.</para>
///
/// <para>Application layer on purpose: "which of several candidate strings becomes the label" is
/// use-case policy. The INVARIANTS (non-empty, length, no personnummer) stay in
/// <c>Resume.ValidateName</c> — this type never re-encodes them, it only picks a candidate. It
/// lives here rather than as a private handler method because the auto-promote gate needs the
/// same resolution from a second call site (the read side, PR C).</para>
/// </summary>
public static class ResumeLabelResolver
{
    /// <summary>
    /// The generated label's prefix. Swedish because it is user-facing copy that the backend
    /// owns on this path (parity with <c>DomainError</c>'s Swedish messages); the date follows
    /// CLAUDE.md §10's <c>YYYY-MM-DD</c> locale rule and is what makes repeated imports
    /// distinguishable in the hub.
    /// </summary>
    private const string GeneratedPrefix = "Importerat CV";

    /// <summary>
    /// The label for an auto-promoted CV: the user's own text when they typed one, else a
    /// generated, non-PII default. The upload form only sends the field when the user actually
    /// edited it, so an absent value here means "no human named this" — not "the form failed".
    /// </summary>
    public static string Resolve(string? nameOverride, IDateTimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (string.IsNullOrWhiteSpace(nameOverride))
            return $"{GeneratedPrefix} {clock.UtcNow:yyyy-MM-dd}";

        var trimmed = nameOverride.Trim();
        return Truncate(trimmed, Resume.MaxNameLength);
    }

    /// <summary>
    /// Caps at the aggregate's limit without splitting a surrogate pair — a lone surrogate in a
    /// plaintext column is unencodable UTF-8 and would fail the write, not merely render badly.
    /// The validator already caps the wire value; this closes the same hole defensively so a
    /// missing validation behavior cannot turn into a mis-reported <c>IncompleteContent</c>.
    /// </summary>
    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
            return value;

        var cut = max;
        if (char.IsHighSurrogate(value[cut - 1]))
            cut--;

        return value[..cut];
    }
}
