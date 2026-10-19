using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Infrastructure.Admin.Email;

/// <summary>
/// The credentials every provider read authenticates with (#1981): the read application's own key, never the sending
/// key. Bound only in <see cref="EmailUsageSourceCollectionExtensions.AddEmailUsageSource"/>'s configured branch, with
/// <c>ValidateDataAnnotations</c> and <c>ValidateOnStart</c>.
/// <para>
/// <b>A sibling section, never a child of <c>Email:Scaleway</c>.</b> <c>AddEmailSender</c> binds that whole section in
/// both hosts, and its <c>SecretKey</c> is the sending key: a read options class bound from it, or from a parent of
/// it, would be filled with the sending key without a word. <see cref="SecretKey"/> is therefore bound from
/// <see cref="SectionName"/> alone, and <see cref="ProjectId"/> is copied in by <c>PostConfigure</c> from the one raw
/// key it shares with the sender, so no read-side type ever depends on the sender's options.
/// </para>
/// </summary>
internal sealed class ScalewayReadOptions
{
    public const string SectionName = "Email:ScalewayRead";

    /// <summary>The read application's API key, sent as <c>X-Auth-Token</c>. Never logged, never committed.</summary>
    [Required]
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// The project the reads are scoped to, sent as <c>project_id</c>. Never bound from this section: overwritten by
    /// <c>PostConfigure</c> with <c>Email:Scaleway:ProjectId</c>, the project the mail is sent from.
    /// </summary>
    [Required]
    public string ProjectId { get; set; } = string.Empty;
}
