using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Identity;

/// <summary>
/// The password-reset token provider (#1171): an ordinary <see cref="DataProtectorTokenProvider{TUser}"/>
/// bound to <see cref="PasswordResetTokenProviderOptions"/> instead of the shared
/// <c>DataProtectionTokenProviderOptions</c>, so the reset link's lifespan is its own.
/// <para>
/// It deliberately adds NO behaviour. The security properties come from the base type and are the
/// same ones #679 chose the DataProtector provider for over the "Email" TOTP provider: the token is
/// HMAC'd and encrypted rather than a brute-forceable 6-digit code, and it is bound to the user's
/// <c>SecurityStamp</c> — which <c>ResetPasswordAsync</c> rotates on success, making the token
/// single-use without any table to store it in.
/// </para>
/// <para>
/// Api-only, and that is a constraint rather than an accident: token providers need
/// <c>IDataProtectionProvider</c>, and <c>AddCoreIdentityForWorker</c> deliberately registers none
/// (ADR 0102). Minting and validating a reset token must therefore both happen in the Api process,
/// which is where both endpoints live.
/// </para>
/// </summary>
public sealed class PasswordResetTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<PasswordResetTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class;
