using System.Collections.Frozen;
using System.Collections.Immutable;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// Dev/MVP-impl av IEmailSender. Skriver email-innehåll till ILogger istället
/// för att skicka via riktig mailserver. Registreras BARA i Development/Test
/// (<c>AddEmailSender</c>); i övriga miljöer faller "Console" tillbaka på
/// <see cref="NullEmailSender"/>.
///
/// Den riktiga transaktionella mejlvägen ÄR byggd och lever bredvid den här:
/// <see cref="ScalewayEmailSender"/> bakom <c>Email:Provider=Scaleway</c> (Scaleway
/// Transactional Email i fr-par, #183).
///
/// Security: the whole plaintext body — activation and reset tokens included — reaches
/// <c>ILogger</c>, and dev's Seq persists it. Since #1208 that happens ONLY for a recipient at a
/// domain RFC 2606 / RFC 6761 reserve, i.e. one that can never be a real mailbox; every other
/// recipient gets a kind-only <c>Warning</c> and no body at all. Dev-Seq is loopback-bound and
/// admin-authenticated on top of that (#1198). <c>ConsoleEmailSenderReservedRecipientTests</c>
/// pins both arms.
/// </summary>
public sealed partial class ConsoleEmailSender(
    ILogger<ConsoleEmailSender> logger,
    IOptions<EmailOptions> options)
    : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    /// <summary>
    /// <see langword="true"/>, and that is a substantive answer rather than a convenience for the
    /// test suite (#1087). This sender writes the whole body — activation and confirmation links
    /// included — to <c>ILogger</c> for a reserved recipient, so a developer CAN complete a
    /// token→email→confirm flow from the log. Delivery-dependent handlers must therefore work in
    /// Development exactly as they will in production; answering <see langword="false"/> here would
    /// refuse the very flows dev exists to exercise. See <see cref="IEmailSender.CanDeliver"/>.
    ///
    /// <para>
    /// <b>The recipient gate does not read this, and must not.</b> It changes what is written to the
    /// log, never the delivery contract. <c>ChangeEmailCommandHandler</c> and
    /// <c>RequestPasswordResetCommandHandler</c> both refuse up front on
    /// <see langword="false"/> and return 503, so reasoning "the body is withheld, therefore we
    /// cannot deliver" would turn a withheld log line into a failed request — in the one
    /// environment where the flow is supposed to be exercised.
    /// </para>
    /// </summary>
    public bool CanDeliver => true;

    public Task SendMatchNotificationEmailAsync(
        string toEmail,
        MatchNotificationEmail content,
        CancellationToken cancellationToken)
    {
        var body = EmailTemplates.MatchNotification(_options.BaseUrl, content);
        WriteEmail("match-notification", toEmail, body.Subject, body.PlainTextBody);
        return Task.CompletedTask;
    }

    public Task SendFollowedCompanyNotificationEmailAsync(
        string toEmail,
        FollowedCompanyNotificationEmail content,
        CancellationToken cancellationToken)
    {
        var body = EmailTemplates.FollowedCompanyNotification(_options.BaseUrl, content);
        WriteEmail("followed-company-notification", toEmail, body.Subject, body.PlainTextBody);
        return Task.CompletedTask;
    }

    public Task SendEmailChangeConfirmationAsync(
        string toEmail,
        EmailChangeConfirmationEmail content,
        CancellationToken cancellationToken)
    {
        var body = EmailTemplates.EmailChangeConfirmation(_options.BaseUrl, content);
        WriteEmail("email-change-confirmation", toEmail, body.Subject, body.PlainTextBody);
        return Task.CompletedTask;
    }

    public Task SendEmailChangedNotificationAsync(
        string toEmail,
        CancellationToken cancellationToken)
    {
        var body = EmailTemplates.EmailChangedNotification();
        WriteEmail("email-changed-notification", toEmail, body.Subject, body.PlainTextBody);
        return Task.CompletedTask;
    }

    public Task SendEmailConfirmationAsync(
        string toEmail,
        EmailConfirmationEmail content,
        CancellationToken cancellationToken)
    {
        var body = EmailTemplates.EmailConfirmation(_options.BaseUrl, content);
        WriteEmail("email-confirmation", toEmail, body.Subject, body.PlainTextBody);
        return Task.CompletedTask;
    }

    public Task SendAccountExistsNoticeAsync(
        string toEmail,
        CancellationToken cancellationToken)
    {
        var body = EmailTemplates.AccountExistsNotice(_options.BaseUrl);
        WriteEmail("account-exists-notice", toEmail, body.Subject, body.PlainTextBody);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(
        string toEmail,
        PasswordResetEmail content,
        CancellationToken cancellationToken)
    {
        var body = EmailTemplates.PasswordReset(_options.BaseUrl, content);
        WriteEmail("password-reset", toEmail, body.Subject, body.PlainTextBody);
        return Task.CompletedTask;
    }

    public Task SendPasswordChangedNoticeAsync(
        string toEmail,
        CancellationToken cancellationToken)
    {
        var body = EmailTemplates.PasswordChangedNotice(_options.BaseUrl);
        WriteEmail("password-changed-notice", toEmail, body.Subject, body.PlainTextBody);
        return Task.CompletedTask;
    }

    // THE GATE (#1208). Every IEmailSender method above funnels through here, which is what makes
    // this a complete cut of the producer set: an invariant enforced at the writer holds for every
    // future write, where a reading of the sink only ever covers the writes already taken.
    //
    // CLAUDE.md §11 used to accept this sink on a condition nobody could fail — "it holds no
    // real-user PII" is a property of the CONTENTS, so the only way to know it was to go and look,
    // and the answer expired with the next registration.
    private void WriteEmail(string emailKind, string toEmail, string subject, string body)
    {
        if (IsReservedRecipient(toEmail))
        {
            LogEmail(toEmail, subject, body);
            return;
        }

        LogSuppressedBody(emailKind);
    }

    /// <summary>
    /// RFC 6761 §6.2–6.5 reserves these TLDs; a name under one of them cannot resolve to a real
    /// mailbox, which is the property the gate needs. The leading dot makes each a label-boundary
    /// test rather than a bare suffix match.
    /// </summary>
    internal static readonly ImmutableArray<string> ReservedTopLevelDomains =
        [".test", ".example", ".invalid", ".localhost"];

    /// <summary>
    /// RFC 2606 §3's reserved second-level names, and their sub-domains.
    /// </summary>
    internal static readonly FrozenSet<string> ReservedSecondLevelDomains = new[]
    {
        "example.com",
        "example.net",
        "example.org",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="toEmail"/> sits at a domain reserved for documentation and testing.
    /// Fail-closed: an address this cannot parse is NOT reserved, so it loses its body.
    ///
    /// <para>
    /// The membership rule is an RFC rather than an allow-list, and that is the point: an addition
    /// is checked against a document neither this repo nor its owner controls, so the set cannot
    /// drift toward convenience. Nothing is granted for convenience today either — every recipient
    /// that legitimately reaches this sender is already under the rule (<c>user@example.com</c> in
    /// the unit tests, <c>klas@jobbliggaren.test</c> per <see cref="Identity.AdminBootstrapOptions"/>,
    /// <c>test-e2e-*@e2e.jobbliggaren.test</c> per the Playwright helper).
    /// </para>
    /// <para>
    /// It is deliberately NOT an <c>IOptions</c> value. Anything settable at runtime can be widened
    /// to the very domains it exists to exclude, and <see cref="EmailOptions"/> is constructed by
    /// every sender, so a Console-arm detail on it would be reachable from arms that have none
    /// (#220, ISP).
    /// </para>
    /// </summary>
    internal static bool IsReservedRecipient(string? toEmail)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
            return false;

        // LastIndexOf, not IndexOf: a quoted local part may itself contain '@'.
        var at = toEmail.LastIndexOf('@');
        if (at < 0 || at == toEmail.Length - 1)
            return false;

        // A trailing dot is the FQDN spelling of the same domain.
        var domain = toEmail[(at + 1)..].Trim().TrimEnd('.');
        if (domain.Length == 0)
            return false;

        foreach (var tld in ReservedTopLevelDomains)
        {
            if (domain.EndsWith(tld, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (ReservedSecondLevelDomains.Contains(domain))
            return true;

        foreach (var reserved in ReservedSecondLevelDomains)
        {
            // Sub-domains of a reserved name are reserved with it. The '.' is checked explicitly so
            // this is a label boundary and not a suffix: "example.com.attacker.example" would pass
            // a bare EndsWith while belonging to attacker.example.
            if (domain.Length > reserved.Length
                && domain[domain.Length - reserved.Length - 1] == '.'
                && domain.EndsWith(reserved, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Only PlainTextBody reaches this log, and the omission of HtmlBody is deliberate rather than an
    // oversight to be "completed" later: this line carries the WHOLE body, including confirmation and
    // activation links, into a sink with no retention (CLAUDE.md §11). Logging the HTML part as well
    // would widen that surface for no dev benefit, since the two parts say the same thing (#183,
    // 2026-08-12).
    [LoggerMessage(3001, LogLevel.Information,
        "[ConsoleEmailSender] To={To} Subject={Subject}\n---\n{Body}\n---")]
    private partial void LogEmail(string to, string subject, string body);

    // Kind ONLY — no recipient, not even a masked one, and no subject or body. Parity with
    // NullEmailSender.LogSuppressedConsequential, whose doc states the invariant this line must not
    // break: "Warning reaches a durable sink, so a recipient or token added here later 'for
    // debuggability' becomes durable PII". A [LoggerMessage] parameter becomes a structured property
    // whether or not the template renders it, so the SIGNATURE is the control, not the string.
    //
    // Warning, not Information: this is a dev flow that will not complete, and the developer has to
    // learn why from the log they were about to read the link out of.
    [LoggerMessage(3008, LogLevel.Warning,
        "[ConsoleEmailSender] Body withheld for {EmailKind}: the recipient is not at a domain "
        + "reserved by RFC 2606/6761, and this sink has no retention. Use a .test address "
        + "(docs/runbooks/local-dev-setup.md) to read the link out of the log.")]
    private partial void LogSuppressedBody(string emailKind);
}
