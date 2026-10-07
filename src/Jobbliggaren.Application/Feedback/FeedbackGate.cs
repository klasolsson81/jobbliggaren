using System.Text.Json.Serialization;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Validation;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Feedback;

/// <summary>
/// The feedback feature's switch and the operator's notification address (#1979). Both are server
/// configuration: the address is never part of a request and never committed.
/// </summary>
public sealed class FeedbackOptions
{
    public const string SectionName = "Feedback";

    /// <summary>Whether users may send feedback. Off until the feature launches.</summary>
    public bool Enabled { get; init; }

    /// <summary>Where the "feedback saved" notice goes.</summary>
    public string? NotificationRecipient { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FeedbackAvailability
{
    Open,
    Disabled,
    NoRecipient,
    CannotDeliver,
}

/// <summary>
/// The one rule submit, the prompt state and the notification job all read (senior-cto-advisor,
/// 2026-10-07). Feedback opens only when it is switched on, a usable recipient is configured and
/// the mail transport delivers, so a missing address or a non-delivering transport is caught
/// before any user can send something whose notice would go nowhere.
/// </summary>
public sealed class FeedbackGate(IOptions<FeedbackOptions> options, IEmailSender emailSender)
{
    public FeedbackAvailability Availability =>
        !options.Value.Enabled ? FeedbackAvailability.Disabled
        : !HasUsableRecipient ? FeedbackAvailability.NoRecipient
        : !emailSender.CanDeliver ? FeedbackAvailability.CannotDeliver
        : FeedbackAvailability.Open;

    public bool AcceptsSubmissions => Availability == FeedbackAvailability.Open;

    /// <summary>
    /// Where queued notices can go, or <see langword="null"/> when they cannot leave. Independent of
    /// <see cref="FeedbackOptions.Enabled"/>: notices queued before the switch was turned off still go out.
    /// </summary>
    public string? DeliverableRecipient =>
        HasUsableRecipient && emailSender.CanDeliver ? options.Value.NotificationRecipient : null;

    private bool HasUsableRecipient =>
        EmailAddressRules.IsUsableInboxAddress(options.Value.NotificationRecipient);
}
