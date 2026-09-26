using Jobbliggaren.Application.Dev.Abstractions;
using Jobbliggaren.Infrastructure.Email;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// DEV-ONLY — REMOVE BEFORE LAUNCH (Klas). Asks <see cref="ConsoleEmailSender.IsReservedRecipient"/>, the same
/// member the Console sender's body guard and <see cref="DevLoginCodeCapture"/> use, so the three cannot drift.
/// </summary>
internal sealed class DevSeedableAddressPolicy : IDevSeedableAddressPolicy
{
    public bool IsSeedable(string email) => ConsoleEmailSender.IsReservedRecipient(email);
}
