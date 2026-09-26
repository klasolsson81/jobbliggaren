using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Infrastructure.Auth.ExternalLogins;

/// <summary>
/// The Google OAuth client (#1744, ADR 0142 D8). A class, never a record: a record's compiler-written
/// <c>ToString</c> would print <see cref="ClientSecret"/>. The secret reaches the box through the <c>_FILE</c> seam,
/// never a committed file.
/// </summary>
public sealed class GoogleOAuthOptions
{
    public const string SectionName = "Auth:OAuth:Google";

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string ClientSecret { get; set; } = string.Empty;
}
