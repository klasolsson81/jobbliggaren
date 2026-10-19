using Microsoft.Extensions.Configuration;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// The one reading of <c>Email:Provider</c>. <c>AddEmailSender</c> chooses the sender from it and
/// <c>AddEmailUsageSource</c> chooses the provider reads from it, so the two cannot disagree about which provider
/// is configured; <c>EmailUsageSourceRegistrationTests.TheSource_FollowsTheSenderAndTheReadKey</c> pins that they do not.
/// </summary>
internal static class EmailProviderSelection
{
    public const string ConsoleProvider = "Console";

    public const string ScalewayProvider = "Scaleway";

    private const string ProviderKey = $"{EmailOptions.SectionName}:{nameof(EmailOptions.Provider)}";

    /// <summary>The configured value, or <see cref="ConsoleProvider"/> when none is set.</summary>
    public static string ConfiguredProvider(IConfiguration configuration) =>
        configuration[ProviderKey] ?? ConsoleProvider;

    public static bool IsConsole(string provider) =>
        string.Equals(provider, ConsoleProvider, StringComparison.OrdinalIgnoreCase);

    public static bool IsScaleway(string provider) =>
        string.Equals(provider, ScalewayProvider, StringComparison.OrdinalIgnoreCase);
}
