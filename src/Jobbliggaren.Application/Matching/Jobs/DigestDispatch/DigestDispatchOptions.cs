using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.Matching.Jobs.DigestDispatch;

/// <summary>
/// Tunables for the Strong-match digest dispatch (ADR 0080 Vag 4 PR-4b). Application owns the
/// contract; the Worker binds it (<c>Digest</c> section) with <c>ValidateDataAnnotations</c> +
/// <c>ValidateOnStart</c> (parity the backfill options). A future production-tuning change is a
/// config edit, not a code change.
/// </summary>
public sealed class DigestDispatchOptions
{
    public const string SectionName = "Digest";

    /// <summary>
    /// The anti-spam cap on how many Strong matches a single digest email LISTS (ADR 0080
    /// Negativa — a value must be set before PR-4 ships; CTO-ratified default 20). The body shows
    /// the most recent <c>MaxItemsPerDigest</c> while the honest <c>TotalCount</c> reports the full
    /// window ("och N till"). ALL window rows are marked Sent regardless of the display cap, so the
    /// un-displayed remainder cannot re-surface next digest (the cap drains correctly).
    /// Range-validated.
    /// </summary>
    [Range(1, 100)]
    public int MaxItemsPerDigest { get; set; } = 20;
}
