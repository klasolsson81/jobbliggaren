using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Admin.Email;

/// <summary>
/// Why the provider could not deliver a mail, as a closed set (#1981 PR 2). The adapter maps each provider flag it knows
/// onto one of these, so a provider string never crosses into Application; a flag it does not know, or none, is
/// <see cref="Other"/>. Declared from the most severe to the least, the order <see cref="EmailFailureReasons.MostSevere"/>
/// picks by when one mail carries several.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmailFailureReason>))]
public enum EmailFailureReason
{
    MailboxNotFound,
    HardBounce,
    MailboxFull,
    SoftBounce,
    Spam,
    Blocklisted,
    Other,
}

public static class EmailFailureReasons
{
    /// <summary>The most severe of <paramref name="candidates"/>; <see cref="EmailFailureReason.Other"/> when there are none.</summary>
    public static EmailFailureReason MostSevere(ReadOnlySpan<EmailFailureReason> candidates)
    {
        var most = EmailFailureReason.Other;
        foreach (var candidate in candidates)
        {
            if (Rank(candidate) < Rank(most))
            {
                most = candidate;
            }
        }

        return most;
    }

    private static int Rank(EmailFailureReason reason) => reason switch
    {
        EmailFailureReason.MailboxNotFound => 0,
        EmailFailureReason.HardBounce => 1,
        EmailFailureReason.MailboxFull => 2,
        EmailFailureReason.SoftBounce => 3,
        EmailFailureReason.Spam => 4,
        EmailFailureReason.Blocklisted => 5,
        EmailFailureReason.Other => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Not a failure reason."),
    };
}
