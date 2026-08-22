using System.Net;
using System.Text.Json.Nodes;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Email;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #183 — locks <see cref="ScalewayEmailSender"/>'s message composition, its eight-way template
/// mapping, and its PII discipline against a fake <see cref="HttpMessageHandler"/>. Successor to
/// the deleted <c>SesEmailSenderTests</c>; the invariants that survived the provider swap are
/// carried over and the transport-specific ones are new.
///
/// <para>
/// <b>What is pinned, and why each fact exists:</b>
/// <list type="bullet">
///   <item><b>The request line</b> — POST to <c>{base}/emails</c> resolved against the named
///     client's region-bearing base address, with the <c>X-Auth-Token</c> header carrying the
///     configured secret. A missing or wrong token is a 401, which is total, silent delivery loss,
///     so its presence is measured rather than assumed. (The base address itself is registration's
///     property and is pinned in <c>ScalewayEmailProviderGateTests</c>, where the registration
///     actually runs.)</item>
///   <item><b>UTF-8 across the JSON round trip</b> — asserted by parsing the bytes that left the
///     adapter and comparing the decoded strings to the Swedish originals, NOT by asserting a
///     charset field. <see cref="System.Text.Json"/> escapes non-ASCII to <c>\uXXXX</c>, so a
///     charset-shaped assertion would pass over a payload that had lost its åäö in either
///     direction. The round-trip facts deliberately ALSO assert the content is non-ASCII in the
///     first place — a UTF-8 claim over pure-ASCII content pins nothing (CLAUDE.md §10).</item>
///   <item><b>Reply-To via <c>additional_headers</c></b> — Scaleway has no first-class Reply-To
///     field, so the header travels in the generic list. That it survives that route is the
///     condition on which security-auditor Major 2 (2026-08-12) stays closed across the provider
///     swap.</item>
///   <item><b>The eight-way template mapping</b> — one fact per port method, asserting the exact
///     subject AND a body substring unique to that template AND the kebab email-kind that reaches
///     the log. This is where a copy-paste bug lands, and two of the eight subjects differ by a
///     single word ("Bekräfta din e-postadress" vs "Bekräfta din nya e-postadress"), so the subject
///     assertions are <c>ShouldBe</c>, never <c>ShouldContain</c>. The predecessor suite covered
///     six of eight while claiming one per port method; the two account-lifecycle mails added by
///     #1171 are covered here.</item>
///   <item><b>Exactly one POST</b>, on the success path and on the failure path. There is no retry
///     in the sender and none on the transport (<see cref="ScalewayClientRegistration"/>); a retry
///     fan-out would re-emit recipient + body per attempt AND duplicate a delivery, since the send
///     endpoint carries no idempotency parameter.</item>
///   <item><b>PII discipline (CLAUDE.md §5)</b> — the strongest facts in the file. A real
///     <see cref="RecordingLogger{T}"/> oracle over the FORMATTED message and the STRUCTURED
///     property values proves that neither path emits the recipient address, the subject, the body,
///     the activation token, the API secret or the project id. The error-response fixture carries a
///     sentinel IN ITS BODY, which is where a provider echoes the address it rejected — the sender
///     never reads the response body, and that is what the sentinel measures.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>THE ONE BEHAVIOUR THAT COULD NOT BE PORTED VERBATIM, and it is a defect class rather than a
/// style choice.</b> The retired arm contained everything except an
/// <see cref="OperationCanceledException"/>, because an SDK client-side timeout surfaced as
/// <see cref="TimeoutException"/> — not a cancellation — so the plain filter separated the two
/// correctly. <see cref="HttpClient"/> inverts that: its own timeout throws
/// <see cref="TaskCanceledException"/>, which IS an <see cref="OperationCanceledException"/>. The
/// same filter, copied across unchanged, would have let every provider timeout escape as a
/// cancellation, where the callers' own <c>when (ex is not OperationCanceledException)</c> filters
/// swallow it as a host shutdown — one user's failed mail disappearing as "we were stopping
/// anyway". The sender therefore discriminates on the CALLER'S token, which is the semantic
/// question ("did the caller ask to stop?") rather than an exception-shape guess.
/// <see cref="ScalewayEmailSender_SendTimesOut_ContainsItRatherThanTreatingItAsCancellation"/> and
/// <see cref="ScalewayEmailSender_SendIsCancelled_PropagatesTheCancellationRatherThanASendFailure"/>
/// are the pair; either alone is satisfied by a filter that is wrong in the other direction.
/// </para>
///
/// <para>
/// <b>Fixture provenance (CLAUDE.md §5 "Tests:").</b> Every content record here is built the way a
/// production caller builds it, not a shape invented for the test:
/// <c>MatchNotificationEmail(Direct, null, [item], 1)</c> is <c>BackgroundMatchingJob</c>
/// line-for-line (including the grade label, which comes from
/// <c>NotifiableMatchGrade.Top.ToSwedishLabel()</c> = "Toppmatch"); the digest/follow shapes are
/// <c>DigestDispatchJob</c>'s; <c>EmailConfirmationEmail</c>/<c>EmailChangeConfirmationEmail</c> are
/// <c>RegisterCommandHandler</c>'s and <c>ChangeEmailCommandHandler</c>'s, with a Base64Url token
/// (only <c>[A-Za-z0-9_-]</c>) because that is what
/// <c>IUserAccountService.GenerateEmailConfirmationTokenAsync</c> returns.
/// </para>
///
/// <para>
/// <b>On <see cref="RecordingLogger{T}"/> and Infrastructure loggers.</b>
/// <c>Jobbliggaren.Infrastructure</c> resolves <c>Microsoft.Extensions.Telemetry.Abstractions</c>
/// transitively (via <c>Microsoft.Extensions.Resilience</c>) while <c>Jobbliggaren.Application</c>
/// does not, so its <c>[LoggerMessage]</c> methods compile against the R9 generator and hand the
/// logger a POOLED, thread-local state that is cleared the instant the generated method returns.
/// The recorder snapshots for that reason. If a property assertion here ever starts reading empty,
/// look at <c>tests/Shared/RecordingLogger.cs</c> first, not at the sender.
/// </para>
/// </summary>
public sealed class ScalewayEmailSenderTests : IDisposable
{
    private const string Recipient = "user@example.com";
    private const string Region = "fr-par";
    private const string ProjectId = "11111111-2222-3333-4444-555555555555";

    /// <summary>
    /// Stand-in for the Scaleway API secret. Deliberately not shaped like a real key — the arm only
    /// tests <c>IsNullOrWhiteSpace</c>, and a realistic shape would hand the pre-push gitleaks scan
    /// a permanent false positive. It IS asserted never to reach the log, which is why it is a
    /// distinctive literal rather than "x".
    /// </summary>
    private const string SecretKey = "test-scaleway-secret-key";

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

    private static readonly Uri BaseAddress =
        new($"https://api.scaleway.com/transactional-email/v1alpha1/regions/{Region}/");

    private readonly FakeHttpMessageHandler _handler = new();
    private readonly IHttpClientFactory _httpClientFactory = Substitute.For<IHttpClientFactory>();
    private readonly RecordingLogger<ScalewayEmailSender> _logger = new();

    private readonly EmailOptions _options = new()
    {
        Provider = "Scaleway",
        FromName = "Jobbliggaren",
        FromAddress = "no-reply@jobbliggaren.se",
        BaseUrl = "https://jobbliggaren.se",
    };

    private readonly ScalewayEmailOptions _scaleway = new()
    {
        Region = Region,
        SecretKey = SecretKey,
        ProjectId = ProjectId,
    };

    public ScalewayEmailSenderTests() =>
        _httpClientFactory
            .CreateClient(ScalewayClientRegistration.HttpClientName)
            .Returns(_ => new HttpClient(_handler, disposeHandler: false) { BaseAddress = BaseAddress });

    private ScalewayEmailSender CreateSut() =>
        new(_httpClientFactory, Options.Create(_options), Options.Create(_scaleway), _logger);

    /// <summary>
    /// The fake handler is an <see cref="HttpMessageHandler"/> and therefore disposable. xUnit
    /// creates one instance of this class per test, so this runs per test. The
    /// <see cref="HttpClient"/> the factory hands out is constructed with
    /// <c>disposeHandler: false</c> so a disposed client cannot take the handler — and the recorded
    /// requests with it — out from under an assertion.
    /// </summary>
    public void Dispose() => _handler.Dispose();

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

    private static PasswordResetEmail SamplePasswordResetContent() =>
        new(UserId, UrlSafeToken);

    // ---------- helpers ----------

    private FakeHttpMessageHandler.CapturedRequest CapturedRequest()
    {
        _handler.Requests.Count.ShouldBe(
            1, "the sender must issue exactly one Scaleway send per port call");
        return _handler.Requests[0];
    }

    /// <summary>The JSON that actually left the adapter, parsed back from the request bytes.</summary>
    private JsonNode SentPayload() =>
        JsonNode.Parse(CapturedRequest().Body)
        ?? throw new InvalidOperationException("the request body was not JSON");

    private string SubjectSent() => SentPayload()["subject"]!.GetValue<string>();

    private string TextSent() => SentPayload()["text"]!.GetValue<string>();

    private string HtmlSent() => SentPayload()["html"]!.GetValue<string>();

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

    // ---------- the request line ----------

    [Fact]
    public async Task ScalewayEmailSender_SendsAnEmailConfirmation_PostsToTheRegionalEmailsEndpoint()
    {
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        var request = CapturedRequest();
        request.Method.ShouldBe(HttpMethod.Post);
        // The whole absolute URI, not a substring: the relative path "emails" must resolve to a
        // sibling of the region segment, which is what the base address's trailing slash decides.
        request.Uri.AbsoluteUri.ShouldBe(
            $"https://api.scaleway.com/transactional-email/v1alpha1/regions/{Region}/emails");
    }

    [Fact]
    public async Task ScalewayEmailSender_SendsAnEmailConfirmation_AuthenticatesWithTheConfiguredSecretKey()
    {
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        // Without this header the API answers 401 and nothing is delivered — a failure mode that
        // looks like silence, not like an error, from anywhere except the log.
        CapturedRequest().Headers["X-Auth-Token"].ShouldBe([SecretKey]);
    }

    [Fact]
    public async Task ScalewayEmailSender_SendsAnEmailConfirmation_ResolvesTheNamedClientTheRegistrationConfigures()
    {
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        // The name is the whole link between the sender and its configured base address + timeout.
        // A typo here resolves an UNCONFIGURED client — no base address at all — which fails as an
        // InvalidOperationException on a relative URI rather than as anything that names the cause.
        _httpClientFactory.Received(1).CreateClient(ScalewayClientRegistration.HttpClientName);
    }

    [Fact]
    public async Task ScalewayEmailSender_SendsAnEmailConfirmation_SendsJsonWithAnExplicitUtf8Charset()
    {
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        var contentType = CapturedRequest().ContentType.ShouldNotBeNull();
        contentType.MediaType.ShouldBe("application/json");
        contentType.CharSet.ShouldBe("utf-8");
    }

    // ---------- message composition ----------

    [Fact]
    public async Task ScalewayEmailSender_SendsAnEmailConfirmation_ComposesFromDestinationSubjectAndBody()
    {
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        var payload = SentPayload();
        payload["from"]!["name"]!.GetValue<string>().ShouldBe(_options.FromName);
        payload["from"]!["email"]!.GetValue<string>().ShouldBe(_options.FromAddress);
        payload["to"]!.AsArray()[0]!["email"]!.GetValue<string>().ShouldBe(Recipient);
        payload["project_id"]!.GetValue<string>().ShouldBe(ProjectId);
        SubjectSent().ShouldBe("Bekräfta din e-postadress");
        // The activation link the EmailConfirmation template builds — dashed 'D' uid (#981) and the
        // Base64Url token unescaped.
        TextSent().ShouldContain(
            $"{_options.BaseUrl}/bekrafta-konto?uid={UserId:D}&token={UrlSafeToken}");
    }

    [Fact]
    public async Task ScalewayEmailSender_SendsAnEmailConfirmation_RepliesToTheContactAddress()
    {
        // The From stays no-reply@; the REPLY path must reach a human. Three security notices tell
        // people to get in touch, and Reply is what a recipient in that situation actually presses —
        // so without this the reply lands on no-reply@ and is bounced or silently swallowed
        // (security-auditor Major 2, 2026-08-12). Asserted on every send, not only the notices,
        // because the address is set once on the request rather than per template.
        //
        // Scaleway carries it in additional_headers, which is a generic list rather than a typed
        // field — so this asserts the KEY as well as the value. A header that arrived under a
        // misspelled key would be accepted by the API and simply do nothing.
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        var headers = SentPayload()["additional_headers"]!.AsArray();
        headers.Count.ShouldBe(1);
        headers[0]!["key"]!.GetValue<string>().ShouldBe("Reply-To");
        headers[0]!["value"]!.GetValue<string>().ShouldBe(EmailTemplates.ContactAddress);
    }

    [Fact]
    public async Task ScalewayEmailSender_SendsAnEmailConfirmation_PutsExactlyOneRecipientAndNoCarbonCopy()
    {
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        var payload = SentPayload();
        payload["to"]!.AsArray().Count.ShouldBe(1);
        // "Nobody else receives a copy of a transactional mail" — and the payload record has no cc
        // or bcc member at all, so the keys must be absent rather than empty. Absence is the
        // stronger form and is what this asserts.
        payload.AsObject().ContainsKey("cc").ShouldBeFalse();
        payload.AsObject().ContainsKey("bcc").ShouldBeFalse();
    }

    [Fact]
    public async Task ScalewayEmailSender_SendsAnEmailConfirmation_DoesNotEchoTheRecipientIntoSubjectOrBody()
    {
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        SubjectSent().ShouldNotContain(Recipient);
        TextSent().ShouldNotContain(Recipient);
    }

    // ---------- UTF-8 across the JSON round trip ----------

    [Fact]
    public async Task ScalewayEmailSender_SendsAnEmailConfirmation_CarriesSwedishCharactersIntactInBothParts()
    {
        // The real invariant, asserted where it can actually break: the bytes that left the adapter
        // are parsed back and compared to the Swedish originals. System.Text.Json escapes non-ASCII
        // to \uXXXX, so this passes only if the escape decodes to the same characters — which is
        // exactly the property "UTF-8 everywhere, åäö must survive serialization" names.
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        SubjectSent().ShouldBe("Bekräfta din e-postadress");
        TextSent().ShouldContain("Vänliga hälsningar");
        HtmlSent().ShouldContain("Vänliga hälsningar");
    }

    [Fact]
    public async Task ScalewayEmailSender_SendsAnEmailConfirmation_CarriesNonAsciiInEveryFieldTheRoundTripCovers()
    {
        // The counterfactual that makes the round-trip fact above non-vacuous: an encoding assertion
        // only means something if the content it covers is outside ASCII. All three fields are — the
        // subject is "Bekräfta …" and both bodies sign off "Vänliga hälsningar". If a template is
        // ever rewritten to pure ASCII this fact fails FIRST, and that is the signal: the encoding
        // assertion has stopped proving anything, not that the copy is wrong.
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        SubjectSent().ShouldContain("ä");
        TextSent().ShouldContain("ä");
        HtmlSent().ShouldContain("ä");
    }

    [Fact]
    public async Task ScalewayEmailSender_SendsAnEmailConfirmation_PutsBothPartsInTheMessage()
    {
        // The multipart/alternative contract at the seam where the request is actually built: a
        // client picks html and falls back to text, so BOTH must be present. A regression that
        // dropped the text part would be invisible in any HTML-capable mail client — which is every
        // client a developer checks — and would only surface for the plain-text readers this repo
        // cannot observe.
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        TextSent().ShouldNotBeNullOrWhiteSpace();
        HtmlSent().ShouldNotBeNullOrWhiteSpace();
        HtmlSent().ShouldNotBe(TextSent());
    }

    [Fact]
    public async Task ScalewayEmailSender_SendsAnEmailConfirmation_PutsNoRemoteResourceInTheHtmlItSends()
    {
        // GROUND 2 of the Art. 30 register's retention claim, asserted against the bytes that
        // actually leave this adapter rather than against a template rendered in isolation. The
        // breadth of the ground (all eight templates, plus the counterfactuals that prove this
        // detector can fail) lives in `EmailHtmlNoRemoteResourceTests` and
        // `RemoteResourceDetectorTests`; this fact closes the seam, so a sender that wrapped,
        // decorated or rewrote the HTML on its way into the request could not slip a remote resource
        // past the template-level suite.
        //
        // The ground is provider-independent by construction — it is a statement about our own
        // bytes — which is why it survives the move off SES unchanged.
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        RemoteResourceDetector
            .FindRemoteResources(HtmlSent(), _options.BaseUrl)
            .ShouldBeEmpty();
    }

    // ---------- the eight-way template mapping ----------

    [Fact]
    public async Task ScalewayEmailSender_SendsAMatchNotification_SelectsTheMatchNotificationTemplate()
    {
        var sut = CreateSut();

        await sut.SendMatchNotificationEmailAsync(
            Recipient, SampleMatchContent(), CancellationToken.None);

        SubjectSent().ShouldBe("Ny toppmatchning på Jobbliggaren");
        TextSent().ShouldContain($"{_options.BaseUrl}/matchningar");
        // GDPR Art. 7(3): the settings/unsubscribe link is mandatory in every notification mail.
        TextSent().ShouldContain($"{_options.BaseUrl}/installningar");
        LoggedSurface().ShouldContain("EmailKind=match-notification");
    }

    [Fact]
    public async Task ScalewayEmailSender_SendsAFollowedCompanyNotification_SelectsTheFollowedCompanyTemplate()
    {
        var sut = CreateSut();

        await sut.SendFollowedCompanyNotificationEmailAsync(
            Recipient, SampleFollowContent(), CancellationToken.None);

        SubjectSent().ShouldBe("Nya annonser från företag du följer");
        TextSent().ShouldContain($"{_options.BaseUrl}/jobb");
        TextSent().ShouldContain($"{_options.BaseUrl}/installningar");
        LoggedSurface().ShouldContain("EmailKind=followed-company-notification");
    }

    [Fact]
    public async Task ScalewayEmailSender_SendsAnEmailChangeConfirmation_SelectsTheEmailChangeConfirmationTemplate()
    {
        var sut = CreateSut();

        await sut.SendEmailChangeConfirmationAsync(
            Recipient, SampleChangeConfirmationContent(), CancellationToken.None);

        // One word apart from the registration confirmation's subject — ShouldBe, never ShouldContain.
        SubjectSent().ShouldBe("Bekräfta din nya e-postadress");
        TextSent().ShouldContain($"{_options.BaseUrl}/bekrafta-epost?uid={UserId:D}");
        LoggedSurface().ShouldContain("EmailKind=email-change-confirmation");
    }

    [Fact]
    public async Task ScalewayEmailSender_SendsAnEmailChangedNotification_SelectsTheEmailChangedNoticeTemplate()
    {
        var sut = CreateSut();

        await sut.SendEmailChangedNotificationAsync(Recipient, CancellationToken.None);

        SubjectSent().ShouldBe("Din e-postadress har ändrats");
        TextSent().ShouldContain(EmailTemplates.ContactAddress);
        // CTO-bind #4 (#679): the security notice to the OLD address carries no token and no link
        // that grants anything.
        TextSent().ShouldNotContain(UrlSafeToken);
        LoggedSurface().ShouldContain("EmailKind=email-changed-notification");
    }

    [Fact]
    public async Task ScalewayEmailSender_SendsAnEmailConfirmation_SelectsTheEmailConfirmationTemplate()
    {
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        SubjectSent().ShouldBe("Bekräfta din e-postadress");
        TextSent().ShouldContain($"{_options.BaseUrl}/bekrafta-konto?uid={UserId:D}");
        LoggedSurface().ShouldContain("EmailKind=email-confirmation");
    }

    [Fact]
    public async Task ScalewayEmailSender_SendsAnAccountExistsNotice_SelectsTheAccountExistsTemplate()
    {
        var sut = CreateSut();

        await sut.SendAccountExistsNoticeAsync(Recipient, CancellationToken.None);

        SubjectSent().ShouldBe("Din e-postadress är redan registrerad hos Jobbliggaren");
        TextSent().ShouldContain($"{_options.BaseUrl}/logga-in");
        // #714: the out-of-band notice to a TAKEN address grants nothing — no token, no reset link.
        TextSent().ShouldNotContain(UrlSafeToken);
        LoggedSurface().ShouldContain("EmailKind=account-exists-notice");
    }

    /// <summary>
    /// #1171's password-reset mail. Uncovered by the predecessor suite, which claimed one fact per
    /// port method while covering six of eight — the same growth-blindness class the HTML work
    /// measured three times in one PR (#183, 2026-08-12).
    /// </summary>
    [Fact]
    public async Task ScalewayEmailSender_SendsAPasswordReset_SelectsThePasswordResetTemplate()
    {
        var sut = CreateSut();

        await sut.SendPasswordResetAsync(
            Recipient, SamplePasswordResetContent(), CancellationToken.None);

        SubjectSent().ShouldBe("Återställ ditt lösenord");
        TextSent().ShouldContain($"{_options.BaseUrl}/aterstall-losenord?uid={UserId:D}");
        LoggedSurface().ShouldContain("EmailKind=password-reset");
    }

    /// <summary>#1171's password-changed security notice. Also uncovered before this suite.</summary>
    [Fact]
    public async Task ScalewayEmailSender_SendsAPasswordChangedNotice_SelectsThePasswordChangedTemplate()
    {
        var sut = CreateSut();

        await sut.SendPasswordChangedNoticeAsync(Recipient, CancellationToken.None);

        SubjectSent().ShouldBe("Ditt lösenord har ändrats");
        // A security notice grants nothing: no token, no link that changes anything.
        TextSent().ShouldNotContain(UrlSafeToken);
        LoggedSurface().ShouldContain("EmailKind=password-changed-notice");
    }

    // ---------- CancellationToken propagation ----------

    /// <summary>
    /// Cancelling the caller's token must cancel the in-flight request.
    /// <para>
    /// <b>This cannot be asserted by comparing tokens, and an earlier draft of this suite tried.</b>
    /// <see cref="HttpClient"/> does not hand the handler the caller's token: it links that token
    /// with its own timeout source and passes the LINKED one, so the handler observes a different
    /// <see cref="CancellationToken"/> value on every send and an equality assertion fails against
    /// correct code. What is observable — and what actually matters — is the LINKAGE, so this
    /// cancels mid-flight and lets the transport report whether it noticed.
    /// </para>
    /// <para>
    /// The delay inside the handler is BOUNDED rather than infinite on purpose: a sender that
    /// dropped the token and passed <see cref="CancellationToken.None"/> would otherwise hang this
    /// test instead of failing it, and a hang is a worse signal than a failure.
    /// </para>
    /// </summary>
    private async Task AssertCallerCancellationReachesTheTransport(
        Func<ScalewayEmailSender, CancellationToken, Task> send)
    {
        using var cts = new CancellationTokenSource();
        _handler.BlockUntilCancelled();
        var sut = CreateSut();

        var pending = send(sut, cts.Token);
        await _handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await pending);

        _handler.ObservedCancellation.ShouldBeTrue(
            "the transport never saw the cancellation, so the sender did not forward the caller's "
            + "token — a send that outlives a host shutdown, which no assertion on the request body "
            + "would notice");

        // A cancellation is not a send failure and is not logged as one.
        _logger.Records.ShouldBeEmpty();
    }

    [Fact]
    public Task ScalewayEmailSender_SendsAnEmailConfirmation_ForwardsTheCancellationTokenToTheTransport() =>
        AssertCallerCancellationReachesTheTransport((sut, token) =>
            sut.SendEmailConfirmationAsync(Recipient, SampleConfirmationContent(), token));

    [Fact]
    public Task ScalewayEmailSender_SendsAMatchNotification_ForwardsTheCancellationTokenToTheTransport() =>
        // Second arm: the eight methods share one private SendAsync, but a future refactor that
        // inlined composition per method would break exactly one of them silently.
        AssertCallerCancellationReachesTheTransport((sut, token) =>
            sut.SendMatchNotificationEmailAsync(Recipient, SampleMatchContent(), token));

    // ---------- failure path: contain, rethrow, single attempt, no fan-out ----------

    [Fact]
    public async Task ScalewayEmailSender_ProviderRejectsTheMessage_ThrowsAPiiFreeEmailDeliveryException()
    {
        // The fixture body is the shape a provider error actually takes, and it CARRIES A RECIPIENT
        // ADDRESS. This test is the pin: the address must not survive the boundary — not in the
        // message, not through InnerException, which exception formatting would walk.
        _handler.RespondWith(
            HttpStatusCode.BadRequest,
            $$"""{"message":"invalid recipient","details":[{"field":"to","value":"{{Recipient}}"}]}""");
        var sut = CreateSut();

        var act = async () => await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        var ex = await act.ShouldThrowAsync<EmailDeliveryException>();

        ex.EmailKind.ShouldBe("email-confirmation");
        ex.UnderlyingErrorType.ShouldBe(nameof(HttpRequestException));

        // InnerException is EMPTY on purpose: .NET's exception formatting walks the inner chain
        // including messages, so attaching the provider exception would carry the address to the
        // sink through this very wrapper.
        ex.InnerException.ShouldBeNull();

        ex.Message.ShouldNotContain(Recipient);
        ex.ToString().ShouldNotContain(Recipient);
    }

    [Fact]
    public async Task ScalewayEmailSender_ProviderRejectsTheMessage_LogsTheStatusAndNeverTheResponseBody()
    {
        // The status is the whole actionable difference between a bad key (401), a wrong project
        // (403), throttling (429) and an outage (5xx), and a status code is not PII. The BODY is
        // where the address lives, and the sender never reads it — the sentinel measures that
        // outcome rather than the intent.
        const string BodySentinel = "SCALEWAY-BODY-SENTINEL-do-not-log-me";
        _handler.RespondWith(HttpStatusCode.TooManyRequests, $$"""{"message":"{{BodySentinel}}"}""");
        var sut = CreateSut();

        await Should.ThrowAsync<EmailDeliveryException>(async () =>
            await sut.SendEmailConfirmationAsync(
                Recipient, SampleConfirmationContent(), CancellationToken.None));

        _logger.Records.Count.ShouldBe(1);
        _logger.Latest.Level.ShouldBe(LogLevel.Error);
        _logger.Latest.EventId.Id.ShouldBe(3006);
        _logger.Latest.Properties.ShouldContain(p =>
            p.Key == "HttpStatus" && Equals(p.Value, 429));
        LoggedSurface().ShouldNotContain(BodySentinel);
    }

    [Fact]
    public async Task ScalewayEmailSender_TransportFails_LogsZeroStatusRatherThanInventingOne()
    {
        // The other half of the status property, and it is why the failure log carries an int rather
        // than only a type name: a socket/DNS/TLS failure produced no response at all, so there is
        // no status to report. Zero says "no answer", which is a different operational fact from any
        // real status — and HttpRequestException.StatusCode is null in exactly this case.
        _handler.ThrowOnSend(new HttpRequestException("No such host is known."));
        var sut = CreateSut();

        await Should.ThrowAsync<EmailDeliveryException>(async () =>
            await sut.SendEmailConfirmationAsync(
                Recipient, SampleConfirmationContent(), CancellationToken.None));

        _logger.Latest.Properties.ShouldContain(p => p.Key == "HttpStatus" && Equals(p.Value, 0));
        _logger.Latest.Properties.ShouldContain(p =>
            p.Key == "ErrorType" && Equals(p.Value, nameof(HttpRequestException)));
    }

    /// <summary>
    /// A client-side timeout is a genuine per-user send failure and must be CONTAINED — and under
    /// <see cref="HttpClient"/> that is not what the retired arm's filter would have done.
    /// <para>
    /// <see cref="HttpClient"/> raises its own timeout as <see cref="TaskCanceledException"/>, which
    /// IS an <see cref="OperationCanceledException"/>. A filter that excluded every OCE would let it
    /// escape as a cancellation, and the callers' own <c>when (ex is not OperationCanceledException)</c>
    /// filters would then swallow it: <c>DigestDispatchJob</c> promises the opposite in its own doc
    /// — <i>"A cancellation propagates (host shutdown / cron-timeout) — not mis-logged as a user
    /// failure"</i> — and this is that promise read backwards. The sender therefore asks whether the
    /// CALLER cancelled, which is the question that actually distinguishes the two.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ScalewayEmailSender_SendTimesOut_ContainsItRatherThanTreatingItAsCancellation()
    {
        // The exact shape HttpClient produces on its own timeout: a TaskCanceledException wrapping a
        // TimeoutException, raised while the caller's token is NOT cancelled.
        _handler.ThrowOnSend(new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout elapsing.",
            new TimeoutException()));
        var sut = CreateSut();

        var act = async () => await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        var ex = await act.ShouldThrowAsync<EmailDeliveryException>();
        ex.UnderlyingErrorType.ShouldBe(nameof(TaskCanceledException));

        // A timeout IS a send failure, so unlike cancellation it is logged as one — with no status,
        // because no response arrived.
        _logger.Latest.EventId.Id.ShouldBe(3006);
        _logger.Latest.Properties.ShouldContain(p => p.Key == "HttpStatus" && Equals(p.Value, 0));
    }

    /// <summary>
    /// The crossing arm. Same exception TYPE as the timeout above, opposite outcome, and the only
    /// input that differs is whether the caller's token is cancelled — so neither test can be
    /// satisfied by a filter that is simply wrong in one direction.
    /// </summary>
    [Fact]
    public async Task ScalewayEmailSender_SendIsCancelled_PropagatesTheCancellationRatherThanASendFailure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _handler.ThrowOnSend(new TaskCanceledException("A task was canceled."));
        var sut = CreateSut();

        var act = async () => await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), cts.Token);

        var ex = await act.ShouldThrowAsync<TaskCanceledException>();

        // The callers filter on OperationCanceledException, so this is the property that actually
        // decides whether a shutdown is swallowed as a user failure.
        ex.ShouldBeAssignableTo<OperationCanceledException>();

        // And it is not logged as a send failure either — the Error line is for real failures.
        _logger.Records.ShouldBeEmpty();
    }

    /// <summary>
    /// The KNOWN RESIDUAL, pinned as measured behaviour rather than left as a claim (code-reviewer
    /// Minor 1, dotnet-architect residual (ii), PR #1339).
    /// <para>
    /// A provider timeout that coincides with caller cancellation is classified as a cancellation
    /// and propagates — so the dispatch jobs' own filters swallow it as a shutdown, and one user's
    /// mail is lost silently. The filter answers "did the caller cancel?" at CATCH time, and the two
    /// questions only differ when they do not race.
    /// </para>
    /// <para>
    /// <b>This test exists because the obvious repair does not work, and that was MEASURED
    /// 2026-08-15 rather than reasoned about.</b> The proposed fix was a third disjunct
    /// <c>ex.InnerException is TimeoutException</c> — the marker <see cref="HttpClient"/> sets on
    /// its own timeout, and a shape this very suite constructs elsewhere. It was implemented, and
    /// the test asserting the REPAIRED behaviour — containment as
    /// <c>EmailDeliveryException</c> — FAILED, so the disjunct never fired. The assertion below on
    /// <c>InnerException</c> measures the disjunct's own operand directly and settles it a second,
    /// independent way. The fixture hands the transport a timeout-shaped exception, and what arrives
    /// carries no trace of it. That is why the filter has two disjuncts and not three.
    /// </para>
    /// <para>
    /// <b>WHY the marker is lost is NOT stated here, and that is deliberate.</b> Three attempts at
    /// naming the mechanism were each measured wrong or unmeasured (dotnet-architect and
    /// code-reviewer, PR #1339 rounds 3–5), and the last one survived in this summary after being
    /// deleted from the method body — because the closing grep was built from the replacement's
    /// wording instead of from the claim's substance. What is asserted below is the OUTCOME, which
    /// is all the filter's design depends on. Do not add a fourth.
    /// </para>
    /// <para>
    /// The race cannot be built with a PRE-cancelled token — <see cref="HttpClient"/> short-circuits
    /// before the handler runs — so the handler enters, waits for the cancel, and only then raises
    /// the timeout shape.
    /// </para>
    /// <para>
    /// <b>If you are here to close this residual:</b> a wider filter at this seam cannot do it,
    /// because the information is already gone by the time the catch runs. It needs a different
    /// seam — e.g. the sender observing its own deadline separately from the caller's token.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ScalewayEmailSender_TimesOutWhileTheCallerIsAlsoCancelling_PropagatesAsCancellation()
    {
        using var cts = new CancellationTokenSource();
        _handler.ThrowAfterCancellation(new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout elapsing.",
            new TimeoutException()));
        var sut = CreateSut();

        var pending = sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), cts.Token);
        await _handler.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        var ex = await Should.ThrowAsync<TaskCanceledException>(async () => await pending);

        // NON-VACUITY, and it is the whole reason this test can pin anything. What the fixture
        // demonstrates is its own ERASURE, so the outcome is identical whether it fired or not —
        // this flag is the only thing that distinguishes them. Remove the arming from the handler
        // and this line fails; a negated assertion on InnerException did NOT (dotnet-architect
        // Kritiskt / code-reviewer M1, PR #1339). Mutation-verified.
        _handler.ThrewArmedException.ShouldBeTrue(
            "the transport never threw the timeout-shaped exception, so the assertions below "
            + "measure nothing about what happens to it");

        // THE MEASUREMENT (2026-08-15), asserted POSITIVELY because a negated form passes on null,
        // on any other inner type, and on a fixture that never fired. The handler threw a
        // TaskCanceledException CARRYING a TimeoutException; what surfaces carries no trace of it.
        ex.InnerException.ShouldBeNull();
        ex.GetBaseException().ShouldBeOfType<TaskCanceledException>();

        // The surfaced message is the framework's DEFAULT, not the one the transport threw. That is
        // an observation, and this suite makes no claim about which layer produced it — see the
        // summary for why no mechanism is named here.
        //
        // Compared against the framework's own default rather than a literal: SR strings are
        // localizable, and this encodes the claim ("the message is not the fixture's") instead of
        // repeating a copy of it.
        ex.Message.ShouldBe(new TaskCanceledException().Message);

        // And nothing is logged, because the sender read it as a cancellation. That is the cost of
        // the residual, stated so it cannot be mistaken for a send that merely failed loudly.
        _logger.Records.ShouldBeEmpty();
    }

    /// <summary>
    /// The response body is never read — pinned by making reading it IMPOSSIBLE (code-reviewer m4,
    /// PR #1339).
    /// <para>
    /// The sender passes <see cref="HttpCompletionOption.ResponseHeadersRead"/> so the claim holds
    /// at the transport level and not merely as our own discipline. Nothing asserted that: the
    /// sentinel test stays green under either completion option, because the sender never read the
    /// body anyway — so a silent revert to the default would buffer the whole error payload again
    /// while every test passed.
    /// </para>
    /// <para>
    /// This response's content THROWS on read. If the transport buffers it — which is exactly what
    /// <see cref="HttpCompletionOption.ResponseContentRead"/> does, before
    /// <c>EnsureSuccessStatusCode</c> ever runs — the send fails. It succeeding is the proof that
    /// the body was never fetched.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ScalewayEmailSender_SendSucceeds_NeverFetchesTheResponseBody()
    {
        _handler.RespondWithUnreadableBody();
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        _logger.Latest.EventId.Id.ShouldBe(3005);
    }

    [Fact]
    public async Task ScalewayEmailSender_ProviderRejectsTheMessage_PostsExactlyOnce()
    {
        // No retry loop in the sender, and no resilience handler on the transport
        // (ScalewayClientRegistration). A re-send would be a duplicate delivery, because the send
        // endpoint has no idempotency parameter, and every attempt re-emits the recipient + body.
        _handler.RespondWith(HttpStatusCode.InternalServerError, """{"message":"boom"}""");
        var sut = CreateSut();

        await Should.ThrowAsync<EmailDeliveryException>(async () =>
            await sut.SendEmailConfirmationAsync(
                Recipient, SampleConfirmationContent(), CancellationToken.None));

        _handler.Requests.Count.ShouldBe(1);
    }

    // ---------- PII discipline (CLAUDE.md §5) ----------

    [Fact]
    public async Task ScalewayEmailSender_SendSucceeds_LogsTheKindAndNothingElse()
    {
        // Positive control for the no-leak facts below: prove the sender DOES log, at the level and
        // event id it claims, so "no record contains the recipient" is a measurement and not an
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
    public async Task ScalewayEmailSender_SendSucceeds_LeaksNoRecipientSubjectBodyTokenOrCredentialToTheLog()
    {
        var sut = CreateSut();

        await sut.SendEmailConfirmationAsync(
            Recipient, SampleConfirmationContent(), CancellationToken.None);

        var subject = SubjectSent();
        var text = TextSent();
        var logged = LoggedSurface();

        logged.ShouldNotContain(Recipient);
        logged.ShouldNotContain(UrlSafeToken);
        logged.ShouldNotContain(UserId.ToString());
        logged.ShouldNotContain(subject);
        logged.ShouldNotContain(text);

        // NOT vacuous here, unlike in the retired SES suite: this sender HOLDS both the API secret
        // and the project id, because the body carries project_id and the auth header is set at the
        // request. So the credential facts are measurable at this seam and are measured.
        logged.ShouldNotContain(SecretKey);
        logged.ShouldNotContain(ProjectId);

        // Scaleway returns a message id; it is deliberately not captured, because it joins to a
        // recipient inside the provider's console — a correlation id for PII, not a neutral trace id.
        logged.ShouldNotContain("message_id");
    }

    [Fact]
    public async Task ScalewayEmailSender_SendFails_LogsTheErrorTypeButNeverTheExceptionMessage()
    {
        // The exception MESSAGE never reaches the logger. Asserted against a sentinel rather than
        // against a claim about what a provider puts in its messages: the rule this pins is the
        // stronger and directly measurable one — no message text at all, whatever it contains. The
        // error TYPE is logged, because a log that says only "failed" is not actionable.
        const string SentinelMessage = "SCALEWAY-SENTINEL-do-not-log-me";
        _handler.ThrowOnSend(new HttpRequestException(SentinelMessage));
        var sut = CreateSut();

        await Should.ThrowAsync<EmailDeliveryException>(async () =>
            await sut.SendEmailConfirmationAsync(
                Recipient, SampleConfirmationContent(), CancellationToken.None));

        _logger.Records.Count.ShouldBe(1);
        _logger.Latest.Level.ShouldBe(LogLevel.Error);
        _logger.Latest.EventId.Id.ShouldBe(3006);
        _logger.Latest.Properties.ShouldContain(p =>
            p.Key == "ErrorType" && Equals(p.Value, nameof(HttpRequestException)));
        LoggedSurface().ShouldNotContain(SentinelMessage);
    }

    [Fact]
    public async Task ScalewayEmailSender_SendFails_LeaksNoRecipientSubjectBodyTokenOrCredentialToTheLog()
    {
        // The failure path is the one that historically grows "just enough context to debug it".
        _handler.RespondWith(HttpStatusCode.BadRequest, """{"message":"invalid recipient"}""");
        var sut = CreateSut();

        await Should.ThrowAsync<EmailDeliveryException>(async () =>
            await sut.SendEmailConfirmationAsync(
                Recipient, SampleConfirmationContent(), CancellationToken.None));

        var subject = SubjectSent();
        var text = TextSent();
        var logged = LoggedSurface();

        logged.ShouldNotContain(Recipient);
        logged.ShouldNotContain(UrlSafeToken);
        logged.ShouldNotContain(UserId.ToString());
        logged.ShouldNotContain(subject);
        logged.ShouldNotContain(text);
        logged.ShouldNotContain(SecretKey);
        logged.ShouldNotContain(ProjectId);
    }

    [Fact]
    public async Task ScalewayEmailSender_SendsAnEmailChangeConfirmation_LeaksNeitherAddressToTheLog()
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

    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Records what the sender actually put on the wire, and can be told to fail in either of the
    /// two ways that matter (a non-success RESPONSE, or a THROWN transport failure) — the sender
    /// treats those differently and the distinction is what the status property carries.
    /// <para>
    /// Hand-rolled rather than substituted: <see cref="HttpMessageHandler.SendAsync"/> is protected,
    /// so NSubstitute cannot intercept it without a shim, and the request CONTENT must be read
    /// inside the handler — the sender disposes the request as soon as the send returns, taking the
    /// content stream with it.
    /// </para>
    /// </summary>
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _responseBody = """{"message_id":"fake-id"}""";
        private Exception? _throwOnSend;
        private bool _blockUntilCancelled;
        private Exception? _throwAfterCancellation;
        private bool _unreadableBody;

        public List<CapturedRequest> Requests { get; } = [];

        /// <summary>Completes once the handler has been entered, so a test can cancel mid-flight.</summary>
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Whether the token the transport was handed actually reported cancellation.</summary>
        public bool ObservedCancellation { get; private set; }

        /// <summary>
        /// Whether the armed <see cref="ThrowAfterCancellation"/> exception was actually thrown.
        /// <para>
        /// Load-bearing: the exception it arms is ERASED by <see cref="HttpClient"/>, so the outcome
        /// looks identical whether the fixture fired or not. Without this flag the residual test
        /// would pass with the arming removed — i.e. it would pin nothing (dotnet-architect
        /// Kritiskt, PR #1339).
        /// </para>
        /// </summary>
        public bool ThrewArmedException { get; private set; }

        public void RespondWith(HttpStatusCode status, string body)
        {
            _status = status;
            _responseBody = body;
        }

        public void ThrowOnSend(Exception exception) => _throwOnSend = exception;

        /// <summary>
        /// Respond 200 with a body that throws the moment anything reads it. Buffering the response
        /// — which <see cref="HttpCompletionOption.ResponseContentRead"/> does before the caller
        /// sees it — therefore fails the send outright.
        /// </summary>
        public void RespondWithUnreadableBody() => _unreadableBody = true;

        public void BlockUntilCancelled() => _blockUntilCancelled = true;

        /// <summary>
        /// Enter, wait for the caller to cancel, then throw <paramref name="exception"/> instead of
        /// the cancellation. Constructs the RACE — caller cancelled AND a provider timeout — which a
        /// pre-cancelled token cannot reach, because <see cref="HttpClient"/> short-circuits before
        /// the handler runs.
        /// </summary>
        public void ThrowAfterCancellation(Exception exception)
        {
            _blockUntilCancelled = true;
            _throwAfterCancellation = exception;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.Ordinal),
                request.Content?.Headers.ContentType,
                body,
                cancellationToken));

            if (_throwOnSend is not null)
            {
                throw _throwOnSend;
            }

            if (_blockUntilCancelled)
            {
                Entered.TrySetResult();

                // Bounded, not Timeout.InfiniteTimeSpan: if the sender ever stopped forwarding the
                // caller's token, this returns normally and the test FAILS instead of hanging.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ObservedCancellation = true;
                    if (_throwAfterCancellation is not null)
                    {
                        ThrewArmedException = true;
                        throw _throwAfterCancellation;
                    }

                    throw;
                }
            }

            return new HttpResponseMessage(_status)
            {
                Content = _unreadableBody
                    ? new StreamContent(new ThrowOnReadStream())
                    : new StringContent(_responseBody),
            };
        }

        /// <summary>A body that cannot be read. Any attempt to buffer or read it throws.</summary>
        private sealed class ThrowOnReadStream : Stream
        {
            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count) =>
                throw new IOException("the response body must never be read");

            public override ValueTask<int> ReadAsync(
                Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                throw new IOException("the response body must never be read");

            public override Task<int> ReadAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                throw new IOException("the response body must never be read");

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) =>
                throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();
        }

        internal sealed record CapturedRequest(
            HttpMethod Method,
            Uri Uri,
            IReadOnlyDictionary<string, string[]> Headers,
            System.Net.Http.Headers.MediaTypeHeaderValue? ContentType,
            string Body,
            CancellationToken Token);
    }
}
