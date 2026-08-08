using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Email;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// ADR 0124 / #1237 — locks <see cref="SesEmailSender"/>'s message composition, its six-way
/// template mapping, and its PII discipline against a faked
/// <see cref="IAmazonSimpleEmailServiceV2"/> (NSubstitute). Successor to the deleted
/// <c>ResendEmailSenderTests</c>; the invariants that survived the provider swap are carried over
/// verbatim and the SES-specific ones are new.
///
/// <para>
/// <b>What is pinned, and why each fact exists:</b>
/// <list type="bullet">
///   <item><b>From</b> is composed from <see cref="EmailOptions"/> ("{FromName} &lt;{FromAddress}&gt;")
///     and the recipient reaches <c>Destination.ToAddresses</c> — carried over from the Resend suite.</item>
///   <item><b>Charset = "UTF-8" on BOTH Subject and Body.Text</b> — NEW, and SES-specific. Resend
///     inferred the charset from the payload; SES does not (its own API docs: "Amazon SES uses 7-bit
///     ASCII by default … if the text includes characters outside of the ASCII range, you have to
///     specify a character set"). Every template in this codebase is Swedish and carries åäö, so a
///     dropped charset mojibakes real mail. CLAUDE.md §10: "UTF-8 everywhere (åäö must survive
///     serialization)". The charset facts deliberately ALSO assert that the subject/body they cover
///     actually contain non-ASCII — a UTF-8 declaration over pure-ASCII content pins nothing.</item>
///   <item><b>The six-way template mapping</b> — one fact per port method, asserting the exact
///     subject AND a body substring unique to that template AND the kebab email-kind that reaches the
///     log. This is where a copy-paste bug lands, and two of the six subjects differ by a single word
///     ("Bekräfta din e-postadress" vs "Bekräfta din nya e-postadress"), so the subject assertions are
///     <c>ShouldBe</c>, never <c>ShouldContain</c>.</item>
///   <item><b>The SDK send happens EXACTLY once</b>, on the success path and on the failure path.
///     <c>MaxErrorRetry = 0</c> (SesClientRegistration) closes the transport retry; this closes the
///     sender's own. A retry fan-out would re-emit recipient + body per attempt.</item>
///   <item><b>Rethrow</b> on provider failure — caller isolation (the dispatch jobs' per-user
///     try/catch) is the design, so the sender must not swallow.</item>
///   <item><b>PII discipline (CLAUDE.md §5)</b> — the strongest facts in the file. A real
///     <see cref="RecordingLogger{T}"/> oracle over the FORMATTED message and the STRUCTURED property
///     values proves that neither the success nor the failure path emits the recipient address, the
///     subject, the body, the activation token, or the exception's message.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Fixture provenance (CLAUDE.md §5 "Tests:").</b> Every content record here is built the way a
/// production caller builds it, not a shape invented for the test:
/// <c>MatchNotificationEmail(Direct, null, [item], 1)</c> is <c>BackgroundMatchingJob</c> line-for-line
/// (including the grade label, which comes from <c>NotifiableMatchGrade.Top.ToSwedishLabel()</c> =
/// "Toppmatch"); the digest/follow shapes are <c>DigestDispatchJob</c>'s;
/// <c>EmailConfirmationEmail</c>/<c>EmailChangeConfirmationEmail</c> are
/// <c>RegisterCommandHandler</c>'s and <c>ChangeEmailCommandHandler</c>'s, with a Base64Url token
/// (only <c>[A-Za-z0-9_-]</c>) because that is what
/// <c>IUserAccountService.GenerateEmailConfirmationTokenAsync</c> returns.
/// </para>
///
/// <para>
/// <b>What is NOT asserted, deliberately.</b> No AWS credential reaches
/// <see cref="SesEmailSender"/> at all — the IAM key is consumed by
/// <c>SesClientRegistration.AddSesClient</c> when it constructs the client, so this class has no
/// credential to leak and a "credential never logged" fact here would be vacuous. The secret this
/// class DOES handle is the confirmation/activation token (a bearer secret that grants account
/// access), and that is what the PII facts below assert never reaches the log.
/// </para>
///
/// <para>
/// The production code ignores <c>SendEmailAsync</c>'s return value (it only awaits), so the success
/// path leaves the call unconfigured — NSubstitute returns a completed Task, which <c>await</c>
/// handles. This keeps the tests decoupled from the SDK's response-construction internals.
/// </para>
///
/// <para>
/// <b>This class is the first consumer of <see cref="RecordingLogger{T}"/> against an
/// INFRASTRUCTURE type, and that mattered.</b> <c>Jobbliggaren.Infrastructure</c> resolves
/// <c>Microsoft.Extensions.Telemetry.Abstractions</c> transitively (via
/// <c>Microsoft.Extensions.Resilience</c>) while <c>Jobbliggaren.Application</c> does not, so its
/// <c>[LoggerMessage]</c> methods compile against the R9 generator and hand the logger a POOLED,
/// thread-local <c>LoggerMessageState</c> that is cleared the instant the generated method returns.
/// The recorder used to hold that reference, so every Infrastructure logger's properties read as
/// EMPTY after the fact — measured 2026-08-08 (#1237): Count 2 inside <c>Log</c>, Count 0 one
/// statement later. The recorder now snapshots. If a property assertion here ever starts reading
/// empty again, look at <c>tests/Shared/RecordingLogger.cs</c> first, not at the sender.
/// </para>
/// </summary>
public class SesEmailSenderTests
{
    private const string Recipient = "user@example.com";

    /// <summary>
    /// Base64Url shape (only <c>[A-Za-z0-9_-]</c>) — what ASP.NET Identity's token provider emits
    /// through <c>GenerateEmailConfirmationTokenAsync</c> and what the templates put in the link
    /// unescaped. A bearer secret: opening the link activates or re-points the account.
    /// </summary>
    // Hardcoded TEST fixture, not a real token — no account it could activate exists. The
    // `CfDJ8` prefix is deliberate (it is what ASP.NET Data Protection actually emits, so the
    // fixture exercises the real shape), and it is also why the string's entropy trips the
    // generic-api-key rule. Inline allow rather than a .gitleaksignore fingerprint, per that
    // file's own header: a fingerprint re-breaks on every re-SHA. gitleaks:allow
    private const string UrlSafeToken = "CfDJ8Nr-9xQvT0pLm2Zq_aB3cD4eF5gH6iJ7kL8mN9oP0qR"; // gitleaks:allow

    private static readonly Guid UserId = new("6e6b1f3a-3c2d-4a8f-9b1e-7d0c5a2e4f11");

    private readonly IAmazonSimpleEmailServiceV2 _ses = Substitute.For<IAmazonSimpleEmailServiceV2>();
    private readonly RecordingLogger<SesEmailSender> _logger = new();

    private readonly EmailOptions _options = new()
    {
        Provider = "Ses",
        FromName = "Jobbliggaren",
        FromAddress = "no-reply@jobbliggaren.se",
        BaseUrl = "https://jobbliggaren.se",
    };

    private SesEmailSender CreateSut() => new(_ses, Options.Create(_options), _logger);

    // ---------- fixtures, each mirroring a production call site ----------

    /// <summary>BackgroundMatchingJob's top-direct shape: one Top item, TotalCount 1, no cadence.</summary>
    private static MatchNotificationEmail SampleMatchContent() =>
        new(
            MatchNotificationKind.Direct,
            Cadence: null,
            Items: [new MatchNotificationItem("Backend-utvecklare", "Acme AB", "Toppmatch")],
            TotalCount: 1);

    /// <summary>DigestDispatchJob's follow shape: public ad fields only, no grade, weekly cadence.</summary>
    private static FollowedCompanyNotificationEmail SampleFollowContent() =>
        new(
            DigestCadence.Weekly,
            Items: [new FollowedCompanyAdItem("Backend-utvecklare", "Acme AB")],
            TotalCount: 1);

    private static EmailConfirmationEmail SampleConfirmationContent() =>
        new(UserId, UrlSafeToken);

    private static EmailChangeConfirmationEmail SampleChangeConfirmationContent() =>
        new(UserId, "ny.adress@example.com", UrlSafeToken);

    // ---------- helpers ----------

    private SendEmailRequest CapturedRequest()
    {
        var calls = _ses.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IAmazonSimpleEmailServiceV2.SendEmailAsync))
            .ToList();
        calls.Count.ShouldBe(1, "the sender must issue exactly one SES send per port call");
        return calls[0].GetArguments().OfType<SendEmailRequest>().Single();
    }

    private static string SubjectOf(SendEmailRequest request) =>
        request.Content.Simple.Subject.Data;

    private static string BodyOf(SendEmailRequest request) =>
        request.Content.Simple.Body.Text.Data;

    /// <summary>
    /// Everything a structured sink would persist for this run: the formatted message plus every
    /// property NAME and VALUE. Seq indexes the properties, not only the rendered string, so a leak
    /// that lived only in a property would be invisible to a message-only assertion.
    /// </summary>
    private string LoggedSurface()
    {
        _logger.Records.ShouldNotBeEmpty(
            "the sender must emit at least one record, otherwise the no-leak assertions below are "
            + "vacuously true");

        return string.Join(
            "\n",
            _logger.Records.Select(r =>
                r.Message + "|" + string.Join("|", r.Properties.Select(p => $"{p.Key}={p.Value}"))));
    }

    // ---------- message composition (carried over from the Resend suite) ----------

    [Fact]
    public async Task SesEmailSender_SendsAnEmailConfirmation_ComposesFromDestinationSubjectAndBody()
    {
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        var request = CapturedRequest();
        request.FromEmailAddress.ShouldBe("Jobbliggaren <no-reply@jobbliggaren.se>");
        request.Destination.ToAddresses.ShouldContain(Recipient);
        SubjectOf(request).ShouldBe("Bekräfta din e-postadress");
        // The activation link the EmailConfirmation template builds — dashed 'D' uid (#981) and the
        // Base64Url token unescaped.
        BodyOf(request).ShouldContain(
            $"{_options.BaseUrl}/bekrafta-konto?uid={UserId:D}&token={UrlSafeToken}");
    }

    [Fact]
    public async Task SesEmailSender_SendsAnEmailConfirmation_PutsExactlyOneRecipientInTheDestination()
    {
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        var request = CapturedRequest();
        // AWS SDK v4 leaves request collections NULL by default, so a non-null ToAddresses also
        // proves the production code ASSIGNED the list rather than .Add()-ing onto an assumed-empty
        // one. Cc/Bcc are asserted null-OR-empty rather than null: whether an unset collection
        // materialises is AWSConfigs.InitializeCollections' business (process-global, and this repo
        // deliberately does not set it), while "nobody else receives a copy of a transactional mail"
        // is ours.
        request.Destination.ToAddresses.ShouldNotBeNull().Count.ShouldBe(1);
        (request.Destination.CcAddresses ?? []).ShouldBeEmpty();
        (request.Destination.BccAddresses ?? []).ShouldBeEmpty();
    }

    [Fact]
    public async Task SesEmailSender_SendsAnEmailConfirmation_DoesNotEchoTheRecipientIntoSubjectOrBody()
    {
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        var request = CapturedRequest();
        SubjectOf(request).ShouldNotContain(Recipient);
        BodyOf(request).ShouldNotContain(Recipient);
    }

    // ---------- SES-specific: explicit UTF-8 charset on BOTH content fields ----------

    [Fact]
    public async Task SesEmailSender_SendsAnEmailConfirmation_SetsUtf8CharsetOnSubjectAndBody()
    {
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        var request = CapturedRequest();
        request.Content.Simple.Subject.Charset.ShouldBe("UTF-8");
        request.Content.Simple.Body.Text.Charset.ShouldBe("UTF-8");
    }

    [Fact]
    public async Task SesEmailSender_SendsAnEmailConfirmation_CarriesNonAsciiInBothFieldsTheCharsetCovers()
    {
        // The counterfactual that makes the charset fact above non-vacuous: SES defaults to 7-bit
        // ASCII, so the declaration only matters if the content it covers is actually outside ASCII.
        // Both fields are — the subject is "Bekräfta …" and the sign-off is "Vänliga hälsningar".
        // If a template is ever rewritten to pure ASCII this fact fails FIRST, and that is the
        // signal: the charset assertion has stopped proving anything, not that the copy is wrong.
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        var request = CapturedRequest();
        // Note: Shouldly's string ShouldContain is case-INSENSITIVE by default, which is harmless
        // here — every assertion below is about which BYTES survive, not about casing.
        SubjectOf(request).ShouldContain("ä");
        SubjectOf(request).ShouldContain("Bekräfta");
        BodyOf(request).ShouldContain("Vänliga hälsningar");
    }

    [Fact]
    public async Task SesEmailSender_SendsAMatchNotification_SetsUtf8CharsetOnSubjectAndBody()
    {
        // Second arm of the charset fact: the six methods share one private SendAsync, but a future
        // refactor that inlines composition per method would break exactly one of them silently.
        var sut = CreateSut();

        await sut.SendMatchNotificationEmailAsync(
            Recipient, SampleMatchContent(), CancellationToken.None);

        var request = CapturedRequest();
        request.Content.Simple.Subject.Charset.ShouldBe("UTF-8");
        request.Content.Simple.Body.Text.Charset.ShouldBe("UTF-8");
        SubjectOf(request).ShouldContain("å");   // "Ny toppmatchning på Jobbliggaren"
        BodyOf(request).ShouldContain("Öppna");
    }

    // ---------- the six-way template mapping ----------

    [Fact]
    public async Task SesEmailSender_SendsAMatchNotification_SelectsTheMatchNotificationTemplate()
    {
        var sut = CreateSut();

        await sut.SendMatchNotificationEmailAsync(
            Recipient, SampleMatchContent(), CancellationToken.None);

        var request = CapturedRequest();
        SubjectOf(request).ShouldBe("Ny toppmatchning på Jobbliggaren");
        BodyOf(request).ShouldContain($"{_options.BaseUrl}/matchningar");
        // GDPR Art. 7(3): the settings/unsubscribe link is mandatory in every notification mail.
        BodyOf(request).ShouldContain($"{_options.BaseUrl}/installningar");
        LoggedSurface().ShouldContain("EmailKind=match-notification");
    }

    [Fact]
    public async Task SesEmailSender_SendsAFollowedCompanyNotification_SelectsTheFollowedCompanyTemplate()
    {
        var sut = CreateSut();

        await sut.SendFollowedCompanyNotificationEmailAsync(
            Recipient, SampleFollowContent(), CancellationToken.None);

        var request = CapturedRequest();
        SubjectOf(request).ShouldBe("Nya annonser från företag du följer");
        BodyOf(request).ShouldContain($"{_options.BaseUrl}/jobb");
        BodyOf(request).ShouldContain($"{_options.BaseUrl}/installningar");
        LoggedSurface().ShouldContain("EmailKind=followed-company-notification");
    }

    [Fact]
    public async Task SesEmailSender_SendsAnEmailChangeConfirmation_SelectsTheEmailChangeConfirmationTemplate()
    {
        var sut = CreateSut();

        await sut.SendEmailChangeConfirmationAsync(
            Recipient, SampleChangeConfirmationContent(), CancellationToken.None);

        var request = CapturedRequest();
        // One word apart from the registration confirmation's subject — ShouldBe, never ShouldContain.
        SubjectOf(request).ShouldBe("Bekräfta din nya e-postadress");
        BodyOf(request).ShouldContain($"{_options.BaseUrl}/bekrafta-epost?uid={UserId:D}");
        LoggedSurface().ShouldContain("EmailKind=email-change-confirmation");
    }

    [Fact]
    public async Task SesEmailSender_SendsAnEmailChangedNotification_SelectsTheEmailChangedNoticeTemplate()
    {
        var sut = CreateSut();

        await sut.SendEmailChangedNotificationAsync(Recipient, CancellationToken.None);

        var request = CapturedRequest();
        SubjectOf(request).ShouldBe("Din e-postadress har ändrats");
        BodyOf(request).ShouldContain($"{_options.BaseUrl}/hjalpcenter");
        // CTO-bind #4 (#679): the security notice to the OLD address carries no token and no link
        // that grants anything.
        BodyOf(request).ShouldNotContain(UrlSafeToken);
        LoggedSurface().ShouldContain("EmailKind=email-changed-notification");
    }

    [Fact]
    public async Task SesEmailSender_SendsAnEmailConfirmation_SelectsTheEmailConfirmationTemplate()
    {
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        var request = CapturedRequest();
        SubjectOf(request).ShouldBe("Bekräfta din e-postadress");
        BodyOf(request).ShouldContain($"{_options.BaseUrl}/bekrafta-konto?uid={UserId:D}");
        LoggedSurface().ShouldContain("EmailKind=email-confirmation");
    }

    [Fact]
    public async Task SesEmailSender_SendsAnAccountExistsNotice_SelectsTheAccountExistsTemplate()
    {
        var sut = CreateSut();

        await sut.SendAccountExistsNoticeAsync(Recipient, CancellationToken.None);

        var request = CapturedRequest();
        SubjectOf(request).ShouldBe("Du har redan ett konto hos Jobbliggaren");
        BodyOf(request).ShouldContain($"{_options.BaseUrl}/logga-in");
        // #714: the out-of-band notice to a TAKEN address grants nothing — no token, no reset link.
        BodyOf(request).ShouldNotContain(UrlSafeToken);
        LoggedSurface().ShouldContain("EmailKind=account-exists-notice");
    }

    // ---------- CancellationToken propagation ----------

    [Fact]
    public async Task SesEmailSender_SendsAnEmailConfirmation_ForwardsTheCancellationTokenToTheSdk()
    {
        using var cts = new CancellationTokenSource();
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(Recipient, SampleConfirmationContent(), cts.Token);

        await _ses.Received(1).SendEmailAsync(Arg.Any<SendEmailRequest>(), cts.Token);
    }

    [Fact]
    public async Task SesEmailSender_SendsAMatchNotification_ForwardsTheCancellationTokenToTheSdk()
    {
        using var cts = new CancellationTokenSource();
        var sut = CreateSut();

        await sut.SendMatchNotificationEmailAsync(Recipient, SampleMatchContent(), cts.Token);

        await _ses.Received(1).SendEmailAsync(Arg.Any<SendEmailRequest>(), cts.Token);
    }

    // ---------- failure path: rethrow, single attempt, no fan-out ----------

    [Fact]
    public async Task SesEmailSender_SesRejectsTheMessage_RethrowsToTheCaller()
    {
        // MessageRejectedException is a real SES v2 SendEmail error (Amazon.SimpleEmailV2.Model) —
        // the sender must not swallow it into a silent success. Caller isolation (the dispatch jobs'
        // per-user try/catch, the Api pipeline) is the design.
        _ses.SendEmailAsync(Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MessageRejectedException("Email address is not verified."));
        var sut = CreateSut();

        var act = async () => await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        await act.ShouldThrowAsync<MessageRejectedException>();
    }

    [Fact]
    public async Task SesEmailSender_SesRejectsTheMessage_CallsTheSdkExactlyOnce()
    {
        // No retry loop in the sender. The transport's own retry is closed separately by
        // MaxErrorRetry = 0 (SesClientRegistration); a re-send would be a duplicate delivery, because
        // SES v2 SendEmail has no idempotency parameter (ADR 0124), and every attempt re-emits the
        // recipient + body.
        _ses.SendEmailAsync(Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MessageRejectedException("Email address is not verified."));
        var sut = CreateSut();

        await Should.ThrowAsync<MessageRejectedException>(async () =>
            await sut.SendEmailConfirmationAsync(
                Recipient, SampleConfirmationContent(), CancellationToken.None));

        await _ses.Received(1).SendEmailAsync(
            Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>());
    }

    // ---------- PII discipline (CLAUDE.md §5) ----------

    [Fact]
    public async Task SesEmailSender_SendSucceeds_LogsTheKindAndNothingElse()
    {
        // Positive control for the two no-leak facts below: prove the sender DOES log, at the level
        // and event id it claims, so "no record contains the recipient" is a measurement and not an
        // artefact of an empty record list.
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        _logger.Records.Count.ShouldBe(1);
        _logger.Latest.Level.ShouldBe(LogLevel.Information);
        _logger.Latest.EventId.Id.ShouldBe(3005);
        _logger.Latest.Properties.ShouldContain(p =>
            p.Key == "EmailKind" && Equals(p.Value, "email-confirmation"));
    }

    [Fact]
    public async Task SesEmailSender_SendSucceeds_LeaksNoRecipientSubjectBodyOrTokenToTheLog()
    {
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        var request = CapturedRequest();
        var logged = LoggedSurface();

        logged.ShouldNotContain(Recipient);
        logged.ShouldNotContain(UrlSafeToken);
        logged.ShouldNotContain(UserId.ToString());
        logged.ShouldNotContain(SubjectOf(request));
        logged.ShouldNotContain(BodyOf(request));
        // The SES MessageId is deliberately not captured either — it joins to a recipient inside the
        // SES console, so it is a correlation id for PII, not a neutral trace id.
        logged.ShouldNotContain("MessageId");
    }

    [Fact]
    public async Task SesEmailSender_SendFails_LogsTheErrorTypeButNeverTheExceptionMessage()
    {
        // The exception MESSAGE never reaches the logger. This is asserted against a sentinel rather
        // than against a claim about what AWS puts in its messages: the production comment's warning
        // ("AWS exception MESSAGES can embed request context") is the REASON the rule exists, but the
        // rule this test pins is the stronger and directly measurable one — no message text at all,
        // whatever it contains. The error TYPE is logged, because a log that says only "failed" is
        // not actionable.
        const string SentinelMessage = "SES-SENTINEL-do-not-log-me";
        _ses.SendEmailAsync(Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MessageRejectedException(SentinelMessage));
        var sut = CreateSut();

        await Should.ThrowAsync<MessageRejectedException>(async () =>
            await sut.SendEmailConfirmationAsync(
                Recipient, SampleConfirmationContent(), CancellationToken.None));

        _logger.Records.Count.ShouldBe(1);
        _logger.Latest.Level.ShouldBe(LogLevel.Error);
        _logger.Latest.EventId.Id.ShouldBe(3006);
        _logger.Latest.Properties.ShouldContain(p =>
            p.Key == "ErrorType" && Equals(p.Value, nameof(MessageRejectedException)));
        LoggedSurface().ShouldNotContain(SentinelMessage);
    }

    [Fact]
    public async Task SesEmailSender_SendFails_LeaksNoRecipientSubjectBodyOrTokenToTheLog()
    {
        // The failure path is the one that historically grows "just enough context to debug it".
        _ses.SendEmailAsync(Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MessageRejectedException("Email address is not verified."));
        var sut = CreateSut();

        await Should.ThrowAsync<MessageRejectedException>(async () =>
            await sut.SendEmailConfirmationAsync(
                Recipient, SampleConfirmationContent(), CancellationToken.None));

        var request = CapturedRequest();
        var logged = LoggedSurface();

        logged.ShouldNotContain(Recipient);
        logged.ShouldNotContain(UrlSafeToken);
        logged.ShouldNotContain(UserId.ToString());
        logged.ShouldNotContain(SubjectOf(request));
        logged.ShouldNotContain(BodyOf(request));
    }

    [Fact]
    public async Task SesEmailSender_SendsAnEmailChangeConfirmation_LeaksNeitherAddressToTheLog()
    {
        // The change-email path is the only one carrying TWO addresses: the recipient (the NEW
        // address, in toEmail) and content.NewEmail. Both are PII and neither may be logged.
        var content = SampleChangeConfirmationContent();
        var sut = CreateSut();

        await sut.SendEmailChangeConfirmationAsync(Recipient, content, CancellationToken.None);

        var logged = LoggedSurface();
        logged.ShouldNotContain(Recipient);
        logged.ShouldNotContain(content.NewEmail);
        logged.ShouldNotContain(content.UrlSafeToken);
    }
}
