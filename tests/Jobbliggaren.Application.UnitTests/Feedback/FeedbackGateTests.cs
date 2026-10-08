using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Feedback;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Feedback;

/// <summary>
/// #1979 — the one rule submit, the prompt state and the notice dispatch read. Feedback opens only when it is switched
/// on, a usable recipient is configured and the transport delivers; where a queued notice may go is a separate answer
/// that ignores the switch.
/// </summary>
public sealed class FeedbackGateTests
{
    private const string Recipient = "feedback-operator@example.test";

    private static FeedbackGate Gate(bool enabled, string? recipient, bool canDeliver)
    {
        var sender = Substitute.For<IEmailSender>();
        sender.CanDeliver.Returns(canDeliver);
        return new FeedbackGate(
            Options.Create(new FeedbackOptions { Enabled = enabled, NotificationRecipient = recipient }), sender);
    }

    // A missing recipient (null) is the [InlineData(null)] row on each theory below.
    public static TheoryData<string> UnusableRecipients => new()
    {
        "",
        "   ",
        "feedback-operator.example.test",
        "@example.test",
        "feedback-operator@",
        "feedback@operator@example.test",
        "feedback-operator@example.test\r\nBcc: someone@example.test",
        new string('a', 250) + "@example.test",
    };

    [Fact]
    public void Availability_SwitchedOnWithARecipientAndADeliveringSender_IsOpen()
    {
        var gate = Gate(enabled: true, Recipient, canDeliver: true);

        gate.Availability.ShouldBe(FeedbackAvailability.Open);
        gate.AcceptsSubmissions.ShouldBeTrue();
    }

    [Fact]
    public void Availability_SwitchedOff_IsDisabledEvenWhenEverythingElseHolds()
    {
        var gate = Gate(enabled: false, Recipient, canDeliver: true);

        gate.Availability.ShouldBe(FeedbackAvailability.Disabled);
        gate.AcceptsSubmissions.ShouldBeFalse();
    }

    [Fact]
    public void Availability_SwitchedOff_OutranksAMissingRecipientAndANonDeliveringSender() =>
        Gate(enabled: false, recipient: null, canDeliver: false).Availability.ShouldBe(FeedbackAvailability.Disabled);

    [Theory]
    [InlineData(null)]
    [MemberData(nameof(UnusableRecipients))]
    public void Availability_WithoutAUsableRecipient_IsNoRecipient(string? recipient)
    {
        var gate = Gate(enabled: true, recipient, canDeliver: true);

        gate.Availability.ShouldBe(FeedbackAvailability.NoRecipient);
        gate.AcceptsSubmissions.ShouldBeFalse();
    }

    [Fact]
    public void Availability_AMissingRecipient_OutranksANonDeliveringSender() =>
        Gate(enabled: true, recipient: null, canDeliver: false).Availability.ShouldBe(FeedbackAvailability.NoRecipient);

    [Fact]
    public void Availability_WhenTheSenderCannotDeliver_IsCannotDeliver()
    {
        var gate = Gate(enabled: true, Recipient, canDeliver: false);

        gate.Availability.ShouldBe(FeedbackAvailability.CannotDeliver);
        gate.AcceptsSubmissions.ShouldBeFalse();
    }

    [Fact]
    public void DeliverableRecipient_WhenOpen_IsTheConfiguredRecipient() =>
        Gate(enabled: true, Recipient, canDeliver: true).DeliverableRecipient.ShouldBe(Recipient);

    [Fact]
    public void DeliverableRecipient_WhenSwitchedOffButDeliverable_IsStillTheRecipient() =>
        Gate(enabled: false, Recipient, canDeliver: true).DeliverableRecipient.ShouldBe(Recipient);

    [Theory]
    [InlineData(null)]
    [MemberData(nameof(UnusableRecipients))]
    public void DeliverableRecipient_WithoutAUsableRecipient_IsNull(string? recipient)
    {
        Gate(enabled: true, recipient, canDeliver: true).DeliverableRecipient.ShouldBeNull();
        Gate(enabled: false, recipient, canDeliver: true).DeliverableRecipient.ShouldBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeliverableRecipient_WhenTheSenderCannotDeliver_IsNull(bool enabled) =>
        Gate(enabled, Recipient, canDeliver: false).DeliverableRecipient.ShouldBeNull();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DispatchAvailability_IgnoresTheSwitch_AndNamesWhyNoticesCannotLeave(bool enabled)
    {
        Gate(enabled, Recipient, canDeliver: true).DispatchAvailability.ShouldBe(FeedbackAvailability.Open);
        Gate(enabled, recipient: null, canDeliver: true).DispatchAvailability.ShouldBe(FeedbackAvailability.NoRecipient);
        Gate(enabled, recipient: null, canDeliver: false).DispatchAvailability.ShouldBe(FeedbackAvailability.NoRecipient);
        Gate(enabled, Recipient, canDeliver: false).DispatchAvailability.ShouldBe(FeedbackAvailability.CannotDeliver);
    }
}
