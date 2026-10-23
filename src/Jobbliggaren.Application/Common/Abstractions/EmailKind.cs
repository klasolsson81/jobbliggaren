using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// The kind of a transactional mail: one member per <see cref="IEmailSender"/> send method (#1981 PR 3, ADR 0180).
/// The send counts store the numeric value, so a member is never renumbered and a removed one leaves its number unused.
/// The tokens are the strings the senders log and <see cref="Exceptions.EmailDeliveryException.EmailKind"/> carries.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmailKind>))]
public enum EmailKind
{
    [JsonStringEnumMemberName(EmailKinds.MatchNotificationToken)]
    MatchNotification = 1,

    [JsonStringEnumMemberName(EmailKinds.FollowedCompanyNotificationToken)]
    FollowedCompanyNotification = 2,

    [JsonStringEnumMemberName(EmailKinds.EmailChangedNotificationToken)]
    EmailChangedNotification = 3,

    [JsonStringEnumMemberName(EmailKinds.AccountEmailChangeRequestedNotificationToken)]
    AccountEmailChangeRequestedNotification = 4,

    [JsonStringEnumMemberName(EmailKinds.LoginChallengeToken)]
    LoginChallenge = 5,

    [JsonStringEnumMemberName(EmailKinds.FeedbackReceivedNotificationToken)]
    FeedbackReceivedNotification = 6,

    [JsonStringEnumMemberName(EmailKinds.FeedbackReplyToken)]
    FeedbackReply = 7,
}

/// <summary>The one vocabulary of <see cref="EmailKind"/> in logs, exceptions and on the wire.</summary>
public static class EmailKinds
{
    public const string MatchNotificationToken = "match-notification";
    public const string FollowedCompanyNotificationToken = "followed-company-notification";
    public const string EmailChangedNotificationToken = "email-changed-notification";
    public const string AccountEmailChangeRequestedNotificationToken = "account-email-change-requested-notification";
    public const string LoginChallengeToken = "login-challenge";
    public const string FeedbackReceivedNotificationToken = "feedback-received-notification";
    public const string FeedbackReplyToken = "feedback-reply";

    public static string TokenOf(EmailKind kind) => kind switch
    {
        EmailKind.MatchNotification => MatchNotificationToken,
        EmailKind.FollowedCompanyNotification => FollowedCompanyNotificationToken,
        EmailKind.EmailChangedNotification => EmailChangedNotificationToken,
        EmailKind.AccountEmailChangeRequestedNotification => AccountEmailChangeRequestedNotificationToken,
        EmailKind.LoginChallenge => LoginChallengeToken,
        EmailKind.FeedbackReceivedNotification => FeedbackReceivedNotificationToken,
        EmailKind.FeedbackReply => FeedbackReplyToken,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not an email kind."),
    };
}
