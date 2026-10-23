using System.Text.Json.Serialization;
using Jobbliggaren.Application.Common.Exceptions;

namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// What one attempt to hand a mail to the provider came to, as the send path saw it (#1981 PR 3, ADR 0181). Only the
/// class is kept: never a status code, a reason phrase, an exception type or a message. The send counts store the
/// numeric value, so a member is never renumbered.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmailSendOutcome>))]
public enum EmailSendOutcome
{
    /// <summary>The provider answered 2xx: it took the mail. Not proof of delivery.</summary>
    Accepted = 1,

    /// <summary>The provider refused it with a 4xx other than 429. Our own 401/403 lands here too.</summary>
    Refused = 2,

    /// <summary>The provider refused it with 429.</summary>
    RateLimited = 3,

    /// <summary>The request never left: name resolution, connection, TLS or proxy failed.</summary>
    Unreachable = 4,

    /// <summary>The provider answered 5xx. It may have taken the mail.</summary>
    ProviderError = 5,

    /// <summary>No answer within the client's timeout. The provider may have taken the mail.</summary>
    Timeout = 6,

    /// <summary>Anything else. The provider may have taken the mail.</summary>
    Other = 7,
}

public static class EmailSendOutcomes
{
    /// <summary>The failure classes in the order a page lists them. A display order, not a precedence.</summary>
    public static IReadOnlyList<EmailSendOutcome> FailureClasses { get; } =
    [
        EmailSendOutcome.Refused,
        EmailSendOutcome.RateLimited,
        EmailSendOutcome.Unreachable,
        EmailSendOutcome.ProviderError,
        EmailSendOutcome.Timeout,
        EmailSendOutcome.Other,
    ];

    /// <summary>
    /// Whether the outcome proves the provider did not take the mail (#1979). Total and never throws: everything that
    /// is not a proven refusal is <see cref="EmailDeliveryDisposition.Unknown"/>, the value that is never retried.
    /// </summary>
    public static EmailDeliveryDisposition DispositionOf(EmailSendOutcome outcome) => outcome switch
    {
        EmailSendOutcome.Refused or EmailSendOutcome.RateLimited or EmailSendOutcome.Unreachable =>
            EmailDeliveryDisposition.NotAccepted,
        _ => EmailDeliveryDisposition.Unknown,
    };
}
