using Microsoft.AspNetCore.Identity;

namespace Jobbliggaren.Infrastructure.Identity;

/// <summary>
/// Options for the password-reset token provider (#1171). Exists so the reset link's lifespan can
/// be set INDEPENDENTLY of the other two DataProtector token kinds.
/// <para>
/// <b>Why a derived options class rather than <c>Configure&lt;DataProtectionTokenProviderOptions&gt;</c>.</b>
/// Email-confirmation, change-email and password-reset tokens all resolve to
/// <c>TokenOptions.DefaultProvider</c>, and <c>DataProtectorTokenProvider&lt;TUser&gt;</c> reads the
/// UNNAMED <c>IOptions&lt;DataProtectionTokenProviderOptions&gt;</c>. Configuring that type therefore
/// shortens all three at once — and the other two email bodies literally promise "Länken gäller i 24
/// timmar" (<c>EmailTemplates</c>), so shortening them silently makes published copy false.
/// Registering a distinct provider under its own name, bound to this derived type, is what isolates
/// the reset lifespan. <c>IOptions&lt;out TOptions&gt;</c> is covariant, so the base provider accepts it.
/// </para>
/// <para>
/// <b><see cref="LifespanMinutes"/> is the single source of the number.</b> The provider enforces it
/// and <c>EmailTemplates.PasswordReset</c> interpolates it into the mail body, so the promise and the
/// enforcement cannot drift apart. Do not restate the value anywhere else.
/// </para>
/// </summary>
public sealed class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    /// <summary>
    /// How long a password-reset link stays valid, in minutes. Short because a reset token is the
    /// single credential that grants account access, and the mail round-trip it has to survive is
    /// minutes rather than the day an activation link may sit unread. A user who misses the window
    /// requests a new link, which is one click.
    /// </summary>
    public const int LifespanMinutes = 60;

    public PasswordResetTokenProviderOptions()
    {
        // Name IS the DataProtector purpose string, so this value partitions the reset tokens'
        // cryptographic namespace away from the shared default. Safe to introduce now precisely
        // because the flow is new: no token has ever been minted under another purpose, so there is
        // nothing in flight to invalidate. Changing it LATER would invalidate every unopened link.
        Name = "JobbliggarenPasswordResetTokenProvider";
        TokenLifespan = TimeSpan.FromMinutes(LifespanMinutes);
    }
}
