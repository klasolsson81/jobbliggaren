using System.Text.Json;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #1981 PR 3 (ADR 0180): the send counts store <see cref="EmailKind"/> and <see cref="EmailSendOutcome"/> as numbers,
/// so a renumbered member would silently re-label every stored row. The expected values are written out here rather
/// than read back from the production members.
/// </summary>
public sealed class EmailSendVocabularyTests
{
    [Fact]
    public void EmailKind_HasExactlyItsSevenNumberedMembers()
        => Enum.GetValues<EmailKind>()
            .Select(k => (k.ToString(), (int)k))
            .ShouldBe(
            [
                ("MatchNotification", 1),
                ("FollowedCompanyNotification", 2),
                ("EmailChangedNotification", 3),
                ("AccountEmailChangeRequestedNotification", 4),
                ("LoginChallenge", 5),
                ("FeedbackReceivedNotification", 6),
                ("FeedbackReply", 7),
            ]);

    [Fact]
    public void EmailSendOutcome_HasExactlyItsSevenNumberedMembers()
        => Enum.GetValues<EmailSendOutcome>()
            .Select(o => (o.ToString(), (int)o))
            .ShouldBe(
            [
                ("Accepted", 1),
                ("Refused", 2),
                ("RateLimited", 3),
                ("Unreachable", 4),
                ("ProviderError", 5),
                ("Timeout", 6),
                ("Other", 7),
            ]);

    /// <summary>One kind per send method: a new method without a kind fails here, not in production.</summary>
    [Fact]
    public void EmailKind_HasOneMemberPerSendMethodOfThePort()
        => typeof(IEmailSender).GetMethods()
            .Count(m => !m.IsSpecialName)
            .ShouldBe(Enum.GetValues<EmailKind>().Length);

    [Fact]
    public void EmailKinds_TokensAreTheStringsTheSendersHaveAlwaysLogged()
        => Enum.GetValues<EmailKind>()
            .Select(EmailKinds.TokenOf)
            .ShouldBe(
            [
                "match-notification",
                "followed-company-notification",
                "email-changed-notification",
                "account-email-change-requested-notification",
                "login-challenge",
                "feedback-received-notification",
                "feedback-reply",
            ]);

    [Fact]
    public void EmailKind_SerialisesAsItsToken()
        => Enum.GetValues<EmailKind>()
            .Select(k => JsonSerializer.Serialize(k))
            .ShouldBe(Enum.GetValues<EmailKind>().Select(k => $"\"{EmailKinds.TokenOf(k)}\""));

    [Fact]
    public void EmailKinds_TokenOfAnUndefinedValue_Throws()
        => Should.Throw<ArgumentOutOfRangeException>(() => EmailKinds.TokenOf((EmailKind)0));

    [Theory]
    [InlineData(EmailSendOutcome.Refused, EmailDeliveryDisposition.NotAccepted)]
    [InlineData(EmailSendOutcome.RateLimited, EmailDeliveryDisposition.NotAccepted)]
    [InlineData(EmailSendOutcome.Unreachable, EmailDeliveryDisposition.NotAccepted)]
    [InlineData(EmailSendOutcome.ProviderError, EmailDeliveryDisposition.Unknown)]
    [InlineData(EmailSendOutcome.Timeout, EmailDeliveryDisposition.Unknown)]
    [InlineData(EmailSendOutcome.Other, EmailDeliveryDisposition.Unknown)]
    public void DispositionOf_AFailureClass_IsTheDispositionCallersAlreadyActOn(
        EmailSendOutcome outcome, EmailDeliveryDisposition expected)
        => EmailSendOutcomes.DispositionOf(outcome).ShouldBe(expected);

    [Fact]
    public void DispositionOf_AnUndefinedValue_IsUnknownAndNeverThrows()
        => EmailSendOutcomes.DispositionOf((EmailSendOutcome)99).ShouldBe(EmailDeliveryDisposition.Unknown);

    [Fact]
    public void FailureClasses_AreEveryOutcomeButAcceptedInTheirDisplayOrder()
        => EmailSendOutcomes.FailureClasses.ShouldBe(
        [
            EmailSendOutcome.Refused,
            EmailSendOutcome.RateLimited,
            EmailSendOutcome.Unreachable,
            EmailSendOutcome.ProviderError,
            EmailSendOutcome.Timeout,
            EmailSendOutcome.Other,
        ]);
}
