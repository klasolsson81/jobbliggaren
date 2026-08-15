using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// Transactional <see cref="IEmailSender"/> over the Scaleway Transactional Email HTTPS API in
/// <c>fr-par</c> (#183). Registered ONLY when <c>Email:Provider="Scaleway"</c> (the DI switch in
/// <c>AddEmailSender</c>).
/// <para>
/// <b>Hand-rolled over <see cref="HttpClient"/>, and that is a simplification rather than a
/// compromise.</b> Scaleway publishes official SDKs for Python, Go and JavaScript and none for
/// .NET (measured against its own SDK index 2026-08-15), so there is no library to confine here.
/// The wire format is one POST with a JSON body, so <see cref="HttpClient"/> plus
/// <see cref="System.Text.Json"/> covers it with no dependency at all. The previous arm's
/// package-confinement apparatus went with the package: the allow-listed SDK and the exempt
/// namespace are gone. Its IL fact did NOT go — it was widened to cover Infrastructure, which the
/// ADR 0124 form had to exempt (<c>NoAmazonReferenceTests</c> is stricter now, not looser).
/// </para>
/// <para>
/// <b>HTTPS API, never SMTP.</b> Netcup drops outbound 25/465/587 (`Mail block`), and ADR 0050 §10
/// ratified the HTTPS-API choice before any of these arms existed. Do not add an SMTP fallback and
/// do not ask the host to open 587. Scaleway offers an SMTP interface; it is not reachable from our
/// box and is not an option.
/// </para>
/// <para>
/// <b>PII discipline (CLAUDE.md §5).</b> This class logs no recipient address, token, body, subject
/// or credential — only the email kind and, on failure, the exception TYPE name plus the HTTP
/// status. The response BODY is never read at all: an API error payload is exactly where a
/// provider echoes the address it rejected, and reading it in order to log it is how that leak
/// gets built. The status code carries the entire actionable difference between a 401 (bad key),
/// a 403 (wrong project), a 429 (throttled) and a 5xx, and a status code is not PII.
/// <para>
/// <b>Scoped to THIS class's logger, deliberately</b> (code-reviewer Minor, PR #1339).
/// <c>Microsoft.Extensions.Http</c> emits its own records per send naming the request URI and the
/// status. Those carry no PII — the URI is <c>…/regions/fr-par/emails</c> with no query — but they
/// exist, so this paragraph is a statement about what the adapter writes, not a claim that the
/// process emits exactly one line per send. Header VALUES are redacted by that logger's default
/// (measured against <c>Microsoft.Extensions.Http</c> 10.0.10, security-auditor 2026-08-15), which
/// is why the auth token does not need a <c>RedactLoggedHeaders</c> call here.
/// </para>
/// </para>
/// <para>
/// <b>A note for whoever restores "missing" telemetry.</b> A historical SES sender deleted in
/// 2026-06 logged <c>To</c>, <c>Subject</c> and <c>MessageId</c> at Information. That is a PII
/// regression under today's rules and is durable once the Seq sink is attached. Scaleway returns a
/// message id of its own; it is not captured either, for the same reason the SES one was not — it
/// is a provider correlation id that joins to a recipient inside the provider's console, so
/// surfacing it is a security-auditor question, not a default.
/// </para>
/// <para>
/// <b>No idempotency parameter.</b> Scaleway's send endpoint offers none, and the port stopped
/// carrying one in ADR 0124 — dedupe across calls is owned by the claim-then-send spine and by
/// <c>ICooldownGate</c> (ADR 0103), one layer up. The transport adds no retry of its own; see
/// <see cref="ScalewayClientRegistration"/> for why none may be added.
/// </para>
/// </summary>
public sealed partial class ScalewayEmailSender(
    IHttpClientFactory httpClientFactory,
    IOptions<EmailOptions> options,
    IOptions<ScalewayEmailOptions> scalewayOptions,
    ILogger<ScalewayEmailSender> logger) : IEmailSender
{
    /// <summary>Relative to the client's region-bearing base address (see <see cref="ScalewayClientRegistration"/>).</summary>
    private const string SendPath = "emails";

    private const string AuthHeaderName = "X-Auth-Token";

    /// <summary>
    /// The <c>HttpStatus</c> logged when the failure produced no HTTP response at all — DNS, TLS,
    /// a socket reset, a client-side timeout. Zero is not a status code, which is the point: it
    /// says "the request never got an answer", not "the answer was 0".
    /// </summary>
    private const int NoHttpStatus = 0;

    private readonly EmailOptions _options = options.Value;
    private readonly ScalewayEmailOptions _scaleway = scalewayOptions.Value;

    /// <summary>
    /// <see langword="true"/> — this is the real transactional path. Reaching this class at all
    /// already means <c>AddEmailSender</c> accepted an explicit <c>Email:Provider=Scaleway</c>
    /// together with an allowed region, a secret key and a project id, fail-loud at REGISTRATION
    /// time; the arm cannot be entered half-configured. See <see cref="IEmailSender.CanDeliver"/>.
    /// <para>
    /// A transport failure after that is a different question from capability and is NOT modelled
    /// here: it surfaces as <c>EmailDeliveryException</c> from the send itself. This property answers
    /// "is delivery possible at all", never "will this particular message arrive".
    /// </para>
    /// </summary>
    public bool CanDeliver => true;

    public Task SendMatchNotificationEmailAsync(
        string toEmail, MatchNotificationEmail content, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            EmailTemplates.MatchNotification(_options.BaseUrl, content),
            "match-notification",
            cancellationToken);

    public Task SendFollowedCompanyNotificationEmailAsync(
        string toEmail, FollowedCompanyNotificationEmail content, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            EmailTemplates.FollowedCompanyNotification(_options.BaseUrl, content),
            "followed-company-notification",
            cancellationToken);

    public Task SendEmailChangeConfirmationAsync(
        string toEmail, EmailChangeConfirmationEmail content, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            EmailTemplates.EmailChangeConfirmation(_options.BaseUrl, content),
            "email-change-confirmation",
            cancellationToken);

    public Task SendEmailChangedNotificationAsync(
        string toEmail, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            EmailTemplates.EmailChangedNotification(),
            "email-changed-notification",
            cancellationToken);

    public Task SendEmailConfirmationAsync(
        string toEmail, EmailConfirmationEmail content, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            EmailTemplates.EmailConfirmation(_options.BaseUrl, content),
            "email-confirmation",
            cancellationToken);

    public Task SendAccountExistsNoticeAsync(
        string toEmail, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            EmailTemplates.AccountExistsNotice(_options.BaseUrl),
            "account-exists-notice",
            cancellationToken);

    public Task SendPasswordResetAsync(
        string toEmail, PasswordResetEmail content, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            EmailTemplates.PasswordReset(_options.BaseUrl, content),
            "password-reset",
            cancellationToken);

    public Task SendPasswordChangedNoticeAsync(
        string toEmail, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            EmailTemplates.PasswordChangedNotice(_options.BaseUrl),
            "password-changed-notice",
            cancellationToken);

    private async Task SendAsync(
        string toEmail,
        EmailTemplates.EmailContent body,
        string emailKind,
        CancellationToken cancellationToken)
    {
        // The try starts here, not at the send: everything that touches `toEmail` or the rendered
        // body belongs inside the boundary. The line is the THREAT MODEL, not throw-site
        // arithmetic — this adapter contains a THIRD PARTY's exceptions, whose messages we neither
        // write nor control. Our own code above (EmailTemplates) is outside it for the same reason,
        // not because it happens to have no throw sites today.
        try
        {
            var payload = new SendEmailPayload(
                From: new SenderContact(_options.FromName, _options.FromAddress),
                To: [new RecipientContact(toEmail)],
                Subject: body.Subject,

                // Both parts, which Scaleway sends as multipart/alternative: the client picks html
                // and falls back to text. Text is NOT vestigial and is not allowed to rot — it is
                // what a plain-text client, a screen reader in text mode and a spam filter comparing
                // the two parts actually read (#183).
                Text: body.PlainTextBody,
                Html: body.HtmlBody,
                ProjectId: _scaleway.ProjectId,

                // Reply-To is the contact address, not the From. The From is no-reply@ and stays
                // that way, but three security notices say "hör av dig till oss" — and for the
                // stressed, less technical recipient those mails exist for, Reply is the natural
                // action, not copying an address out of the body. Without this the reply reaches
                // no-reply@, where it either bounces or is swallowed by whatever catch-all the
                // mailbox host applies. A breach report that disappears silently is a security
                // defect, not a cosmetic one (security-auditor Major 2, 2026-08-12).
                //
                // Scaleway carries it in additional_headers rather than a first-class field; that
                // the header survives that route is the condition on which the 2026-08-12 finding
                // stays closed across the provider swap, so it is pinned at this seam rather than
                // assumed.
                AdditionalHeaders: [new AdditionalHeader("Reply-To", EmailTemplates.ContactAddress)]);

            using var request = new HttpRequestMessage(HttpMethod.Post, SendPath)
            {
                // Explicit UTF-8 on the wire. System.Text.Json escapes non-ASCII to \uXXXX by
                // default, which decodes back to the same characters — but the charset parameter is
                // what tells the receiver how to read the bytes it was handed, and every template in
                // this codebase is Swedish (CLAUDE.md §10: "UTF-8 everywhere, åäö must survive
                // serialization"). The round trip, not the header, is what the tests assert.
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"),
            };

            // Per-request rather than on the client's DefaultRequestHeaders, so every fact about
            // Scaleway's wire protocol lives in this one file and is reachable by a handler-level
            // test — which is where the header's presence is pinned.
            //
            // The bool return is discarded deliberately: TryAddWithoutValidation can only fail here
            // by rejecting the header NAME, which is a compile-time constant, and the failure is
            // self-announcing anyway — no header means 401, which is contained and logged WITH its
            // status rather than swallowed. Branching on it would add an unreachable arm.
            request.Headers.TryAddWithoutValidation(AuthHeaderName, _scaleway.SecretKey);

            // CreateClient per send, deliberately: this sender is a singleton, and one captured
            // HttpClient would freeze HttpMessageHandler rotation for the process lifetime. See
            // ScalewayClientRegistration.HttpClientName.
            var client = httpClientFactory.CreateClient(ScalewayClientRegistration.HttpClientName);

            // ResponseHeadersRead, so "the body is never read" is true at the TRANSPORT level and
            // not merely of our own code (dotnet-architect, PR #1339). The default,
            // ResponseContentRead, buffers the whole error payload into managed memory before
            // EnsureSuccessStatusCode ever runs — and that payload is precisely where a provider
            // echoes the address it rejected. Nothing logged it either way; this stops it being
            // materialised at all. `using` disposes the response and with it the unread stream.
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            // The body is deliberately never read — not to log it, not to parse a message id.
            // EnsureSuccessStatusCode's own message carries the status and reason phrase only, and
            // it records the status on the thrown HttpRequestException, which is where the failure
            // log below picks it up.
            response.EnsureSuccessStatusCode();

            LogSent(emailKind);
        }
        // Contain everything EXCEPT a cancellation the caller actually asked for.
        //
        // The second disjunct is the whole point: HttpClient raises its OWN timeout as
        // TaskCanceledException, which IS an OperationCanceledException, so the SES arm's filter
        // (`ex is not OperationCanceledException`) would have let every provider timeout escape as a
        // cancellation — where the callers' identical filters swallow it as a host shutdown.
        //
        // KNOWN RESIDUAL, MEASURED RATHER THAN ASSUMED (code-reviewer Minor 1, dotnet-architect
        // residual (ii), PR #1339): a provider timeout that COINCIDES with caller cancellation is
        // classified as a cancellation and propagates.
        //
        // The obvious repair is a third disjunct `ex.InnerException is TimeoutException` — the
        // marker HttpClient sets on its own timeout. It was implemented and MEASURED NOT TO WORK on
        // 2026-08-15: the test asserting the REPAIRED behaviour (containment) failed, so the
        // disjunct never fired. Measured outcome in that race: the exception reaching this catch is
        // a TaskCanceledException carrying the framework's default message and a NULL
        // InnerException, even though the transport threw one carrying a TimeoutException. The
        // marker does not survive, so the disjunct is unreachable and shipping it would have been
        // dead code behind a comment claiming a fix.
        //
        // WHERE the marker is lost is deliberately NOT claimed here, and three attempts to name it
        // were each wrong or unmeasured (PR #1339). `ScalewayEmailSenderTests` pins the outcome;
        // nobody has measured which layer performs the replacement, and the filter's design does not
        // depend on knowing. Bounded to shutdown, and the cost is one reaped notification; closing
        // it needs a different seam, not a wider filter here.
        catch (Exception ex)
            when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Log WITHOUT recipient/body (PII) and without the exception message.
            LogFailed(emailKind, ex.GetType().Name, HttpStatusOf(ex));

            // The provider's exception does not leave this adapter. `ex` becomes a TYPE NAME, never
            // an InnerException. Why containment rather than patching the log sites, and why the
            // port declares this contract: ADR 0124.
            throw new EmailDeliveryException(emailKind, ex.GetType().Name);
        }
    }

    /// <summary>
    /// The status the failure carried, or <see cref="NoHttpStatus"/> when it never reached one.
    /// <para>
    /// <c>HttpRequestException.StatusCode</c> is populated by
    /// <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> and is null for a transport-level
    /// failure, so one property separates "the service answered and refused" from "there was no
    /// answer" without inspecting a message.
    /// </para>
    /// </summary>
    private static int HttpStatusOf(Exception ex) =>
        ex is HttpRequestException { StatusCode: { } status } ? (int)status : NoHttpStatus;

    [LoggerMessage(3005, LogLevel.Information, "[ScalewayEmailSender] {EmailKind} email sent")]
    private partial void LogSent(string emailKind);

    [LoggerMessage(3006, LogLevel.Error,
        "[ScalewayEmailSender] {EmailKind} email FAILED ({ErrorType}, HTTP {HttpStatus}) — no recipient/body logged")]
    private partial void LogFailed(string emailKind, string errorType, int httpStatus);

    // ---------------------------------------------------------------------------------------
    // The wire payload. Property names are Scaleway's, spelled out per member rather than left to
    // a naming policy: `project_id` and `additional_headers` are snake_case while `from`/`to` are
    // single words, so a policy would have to be right about a convention instead of this being
    // right about a field. Nothing here is nullable, so no ignore-condition is configured and the
    // serializer's defaults suffice.
    // ---------------------------------------------------------------------------------------

    private sealed record SendEmailPayload(
        [property: JsonPropertyName("from")] SenderContact From,
        [property: JsonPropertyName("to")] IReadOnlyList<RecipientContact> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("project_id")] string ProjectId,
        [property: JsonPropertyName("additional_headers")] IReadOnlyList<AdditionalHeader> AdditionalHeaders);

    private sealed record SenderContact(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("email")] string Email);

    /// <summary>
    /// A recipient carries the address and nothing else. Scaleway accepts an optional display name
    /// here; we never send one, because the only name we hold for a recipient would be personal
    /// data we have no reason to put on the wire twice.
    /// </summary>
    private sealed record RecipientContact(
        [property: JsonPropertyName("email")] string Email);

    private sealed record AdditionalHeader(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("value")] string Value);
}
