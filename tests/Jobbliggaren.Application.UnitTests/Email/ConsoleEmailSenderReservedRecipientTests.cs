using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Email;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #1208 — the dev-sink body invariant, pinned at its writer instead of asserted in prose.
///
/// <para>
/// <b>Why this file exists.</b> CLAUDE.md §11 accepted dev-Seq holding whole email bodies on two
/// conditions, and the second — "it holds no real-user PII" — was a property of what the sink
/// HOLDS. That can only be answered by reading the sink, which makes every answer true until the
/// next registration; a corpus reading taken on 2026-08-23 found zero real recipients and could
/// not have found tomorrow's. <see cref="ConsoleEmailSender"/> routes every send through one
/// private choke point, so the invariant is enforceable at the writer, where it holds for every
/// future write. This file is what turns that from a claim into a measurement: it runs on every
/// CI build, and a regression fails it rather than sitting in the sink unread.
/// </para>
///
/// <para>
/// <b>The non-reserved addresses below are all under the project's own domain, deliberately.</b>
/// A test that needs a NON-reserved recipient cannot use RFC 2606/6761 by definition, and any
/// other choice would be a name a third party can register — the exact hazard the gate exists to
/// close. <c>jobbliggaren.se</c> is the controller's own, nothing is ever sent, and the point of
/// each case is a classification, never a mailbox.
/// </para>
/// </summary>
public class ConsoleEmailSenderReservedRecipientTests
{
    // Planted in the content so the assertion can name what must NOT appear. Every template
    // renders its content into the plain-text body, so an ordinary string is a sufficient probe;
    // it does not have to look like a token to prove a token would have leaked.
    private const string BodyProbe = "PROBE-VALUE-THAT-MUST-NOT-REACH-THE-SINK";

    private const string ReservedRecipient = "user@example.com";
    private const string NonReservedRecipient = "probe@jobbliggaren.se";

    private sealed record SendCase(
        string MethodName,
        string Kind,
        Func<IEmailSender, string, Task> Invoke,
        bool CarriesProbe);

    private static readonly SendCase[] Cases =
    [
        new(nameof(IEmailSender.SendMatchNotificationEmailAsync), "match-notification",
            (s, to) => s.SendMatchNotificationEmailAsync(
                to,
                new MatchNotificationEmail(
                    MatchNotificationKind.Direct,
                    Cadence: null,
                    Items: [new MatchNotificationItem(BodyProbe, "Acme AB", "Toppmatch")],
                    TotalCount: 1),
                CancellationToken.None),
            CarriesProbe: true),

        new(nameof(IEmailSender.SendFollowedCompanyNotificationEmailAsync),
            "followed-company-notification",
            (s, to) => s.SendFollowedCompanyNotificationEmailAsync(
                to,
                new FollowedCompanyNotificationEmail(
                    DigestCadence.Weekly,
                    Items: [new FollowedCompanyAdItem(BodyProbe, "Acme AB")],
                    TotalCount: 1),
                CancellationToken.None),
            CarriesProbe: true),

        new(nameof(IEmailSender.SendEmailChangeConfirmationAsync), "email-change-confirmation",
            (s, to) => s.SendEmailChangeConfirmationAsync(
                to,
                new EmailChangeConfirmationEmail(Guid.Empty, to, BodyProbe),
                CancellationToken.None),
            CarriesProbe: true),

        new(nameof(IEmailSender.SendEmailChangedNotificationAsync), "email-changed-notification",
            (s, to) => s.SendEmailChangedNotificationAsync(to, CancellationToken.None),
            CarriesProbe: false),

        new(nameof(IEmailSender.SendEmailConfirmationAsync), "email-confirmation",
            (s, to) => s.SendEmailConfirmationAsync(
                to,
                new EmailConfirmationEmail(Guid.Empty, BodyProbe),
                CancellationToken.None),
            CarriesProbe: true),

        new(nameof(IEmailSender.SendAccountExistsNoticeAsync), "account-exists-notice",
            (s, to) => s.SendAccountExistsNoticeAsync(to, CancellationToken.None),
            CarriesProbe: false),

        new(nameof(IEmailSender.SendPasswordResetAsync), "password-reset",
            (s, to) => s.SendPasswordResetAsync(
                to,
                new PasswordResetEmail(Guid.Empty, BodyProbe),
                CancellationToken.None),
            CarriesProbe: true),

        new(nameof(IEmailSender.SendPasswordChangedNoticeAsync), "password-changed-notice",
            (s, to) => s.SendPasswordChangedNoticeAsync(to, CancellationToken.None),
            CarriesProbe: false),
    ];

    public static TheoryData<string> AllKinds()
    {
        var data = new TheoryData<string>();
        foreach (var c in Cases)
            data.Add(c.Kind);
        return data;
    }

    private static (ConsoleEmailSender Sender, RecordingLogger<ConsoleEmailSender> Log) Create()
    {
        var log = new RecordingLogger<ConsoleEmailSender>();
        var options = Options.Create(new EmailOptions { BaseUrl = "https://jobbliggaren.se" });
        return (new ConsoleEmailSender(log, options), log);
    }

    private static SendCase CaseFor(string kind) => Cases.Single(c => c.Kind == kind);

    // ---------------------------------------------------------------------------------------
    // 1. The property, both arms. The suppressed arm asserts the ABSENCE of the body — not the
    //    presence of the warning. A guard that only checks the new line passes unchanged while
    //    the old one still fires beside it.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task ReservedRecipient_LogsTheWholeBody_AtInformation(string kind)
    {
        var (sender, log) = Create();

        await CaseFor(kind).Invoke(sender, ReservedRecipient);

        log.Records.Count.ShouldBe(1);
        var (level, eventId, message, properties) = log.Latest;
        level.ShouldBe(LogLevel.Information);
        eventId.Id.ShouldBe(3001);
        message.ShouldContain(ReservedRecipient);
        properties.ShouldContain(p => p.Key == "Body");

        if (CaseFor(kind).CarriesProbe)
            message.ShouldContain(BodyProbe);
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task NonReservedRecipient_WithholdsBodyAndRecipient_AtWarning(string kind)
    {
        var (sender, log) = Create();

        await CaseFor(kind).Invoke(sender, NonReservedRecipient);

        log.Records.Count.ShouldBe(1);
        var (level, eventId, message, properties) = log.Latest;

        // The absence assertions are the test. They read BOTH the rendered message and the
        // structured properties, because [LoggerMessage] promotes every parameter to a property
        // whether or not the template renders it — a recipient added to the signature "for
        // debuggability" would be invisible to an assertion over the message alone, and Seq
        // persists exactly that.
        var emitted = message + " " + string.Join(" ", properties.Select(p => $"{p.Key}={p.Value}"));
        emitted.ShouldNotContain(NonReservedRecipient);
        emitted.ShouldNotContain(BodyProbe);

        // Kind only: no recipient, no subject, no body — not even masked. Parity with
        // NullEmailSender.LogSuppressedConsequential, whose doc states the invariant.
        properties.Select(p => p.Key)
            .Where(k => k != "{OriginalFormat}")
            .ShouldBe(["EmailKind"]);
        properties.ShouldContain(p => p.Key == "EmailKind" && (string?)p.Value == kind);

        level.ShouldBe(LogLevel.Warning);
        eventId.Id.ShouldBe(3008);
    }

    // ---------------------------------------------------------------------------------------
    // 2. Growth of the surface. A ninth IEmailSender method added outside the gate must fail a
    //    test rather than pass silently: a guard that closes today's members does not close
    //    tomorrow's.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void EveryEmailSenderMethod_HasACaseInThisFile()
    {
        // !IsSpecialName drops the CanDeliver getter without depending on a Send* naming
        // convention a ninth method need not follow.
        var declared = typeof(IEmailSender).GetMethods()
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Cases.Select(c => c.MethodName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(declared);
    }

    [Fact]
    public void EveryKind_IsDistinct()
        => Cases.Select(c => c.Kind).Distinct().Count().ShouldBe(Cases.Length);

    // ---------------------------------------------------------------------------------------
    // 3. Growth of the allow-list. Pinned as an exact set, so adding a live consumer-mail domain
    //    fails a named test instead of merely looking wrong in review. The authority is an RFC,
    //    which is why the expected values are written out here rather than read back from the
    //    production members.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ReservedTopLevelDomains_AreExactlyRfc6761s()
        => ConsoleEmailSender.ReservedTopLevelDomains
            .ShouldBe([".test", ".example", ".invalid", ".localhost"]);

    [Fact]
    public void ReservedSecondLevelDomains_AreExactlyRfc2606s()
        => ConsoleEmailSender.ReservedSecondLevelDomains
            .OrderBy(d => d, StringComparer.Ordinal)
            .ShouldBe(["example.com", "example.net", "example.org"]);

    // ---------------------------------------------------------------------------------------
    // The predicate itself, including the shapes that a bare suffix match would wave through.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("USER@EXAMPLE.COM")]
    [InlineData("user@example.net")]
    [InlineData("user@example.org")]
    [InlineData("user@mail.example.com")]
    [InlineData("user@example.com.")]
    [InlineData("klas@jobbliggaren.test")]
    [InlineData("test-e2e-1@e2e.jobbliggaren.test")]
    [InlineData("user@host.example")]
    [InlineData("user@host.invalid")]
    [InlineData("user@host.localhost")]
    public void IsReservedRecipient_IsTrue_ForRfcReservedDomains(string address)
        => ConsoleEmailSender.IsReservedRecipient(address).ShouldBeTrue();

    [Theory]
    // The live domain the gate exists for.
    [InlineData("probe@jobbliggaren.se")]
    // Label-boundary traps: each ENDS with a reserved name while belonging to another owner, so a
    // bare EndsWith would wave them through.
    [InlineData("probe@jobbliggaren-example.com")]
    [InlineData("probe@notexample.org")]
    // …and the mirror shape: a reserved name as a LEFT-hand label of a live domain.
    [InlineData("probe@example.com.jobbliggaren.se")]
    // Fail-closed on anything unparseable.
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("trailing@")]
    [InlineData("user@.")]
    public void IsReservedRecipient_IsFalse_ForEverythingElse(string address)
        => ConsoleEmailSender.IsReservedRecipient(address).ShouldBeFalse();

    [Fact]
    public void IsReservedRecipient_IsFalse_ForNull()
        => ConsoleEmailSender.IsReservedRecipient(null).ShouldBeFalse();
}
