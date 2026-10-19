using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Admin.Email;

/// <summary>
/// The provider's subscription plan, as a closed set (#1981). The adapter maps Scaleway's <c>offer_name</c> onto
/// it, so a provider string never crosses into Application; a value it does not know is <see cref="Unknown"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmailPlan>))]
public enum EmailPlan
{
    Unknown,
    Essential,
    Scale,
}

/// <summary>Whether the allowance region has a reading to show.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmailAllowanceStatus>))]
public enum EmailAllowanceStatus
{
    Observed,

    /// <summary>This deployment sends through no provider that can be asked. Expected in dev and CI, never an error.</summary>
    NotObserved,

    /// <summary>The deployment sends through Scaleway, but the read key is not configured. Carries no reason.</summary>
    ReadKeyMissing,

    Failed,
}

/// <summary>
/// Why there is no reading. One closed set from the adapter to the browser, so each cause has one wording and
/// 401 and 403 stay apart: a 401 is the read key itself refused (expired, revoked or mistyped), a 403 a read key the
/// provider knows but that lacks the permission. Neither says anything about the sending key.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmailProviderReadReason>))]
public enum EmailProviderReadReason
{
    /// <summary><c>Email:Provider</c> is not Scaleway. The only reason that is not a failure.</summary>
    NotConfigured,

    /// <summary>401: the key was refused.</summary>
    Unauthorized,

    /// <summary>403: the key lacks the read permission.</summary>
    Forbidden,

    /// <summary>429.</summary>
    RateLimited,

    /// <summary>Any other status the provider answered with.</summary>
    ProviderError,

    /// <summary>No answer within the read client's timeout.</summary>
    Timeout,

    /// <summary>The request never reached the provider: name resolution, connection, TLS.</summary>
    Unreachable,

    /// <summary>A 2xx whose body could not be read.</summary>
    Malformed,
}
