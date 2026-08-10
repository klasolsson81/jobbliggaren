using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #1087 — the suppression log's LEVEL is a security control, so it is pinned rather than described.
/// <para>
/// <b>Why this file exists, stated because the absence of it is what produced the defect.</b> Until
/// 2026-08-09 every kind logged at <c>Debug</c> while the class docstring claimed ops could see the
/// drop. That claim was false in every configuration where <see cref="NullEmailSender"/> is
/// registered — <c>Logging:LogLevel:Default</c> is <c>Information</c> in every committed
/// <c>appsettings*.json</c> and the class runs only outside Development/Test — and it survived for
/// months because nothing pinned the level. Landing the repair with the same absence of a pin would
/// recreate the mechanism exactly: a later "make the log levels consistent" tidy-up would undo a
/// security-auditor Major without turning a single line red (code-reviewer Major D1, 2026-08-09).
/// </para>
/// <para>
/// <b>The pair is the test.</b> A lone Warning assertion passes against a class that put all six
/// kinds at Warning, and a lone Debug assertion passes against one that put all six at Debug. Only
/// asserting both sides of the split proves there IS a split.
/// </para>
/// </summary>
public class NullEmailSenderSuppressionLogTests
{
    private sealed record Entry(
        LogLevel Level,
        EventId EventId,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> State);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<Entry> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // Deliberately always enabled: this fake records what the class ASKS for. Whether a sink
        // would keep it is the floor question, and the floor is what made the old level useless.
        public bool IsEnabled(LogLevel logLevel) => true;

        // STATE is captured, not just the rendered message, and that distinction is the whole
        // point of the payload test below. [LoggerMessage] exposes every method parameter as a
        // structured property, while only the TEMPLATED tokens reach the rendered text — so a
        // recipient added as a parameter without a matching token lands in the state, is persisted
        // durably by Seq, and is invisible to any assertion over Message alone (code-reviewer
        // Minor D3, 2026-08-09).
        //
        // COPIED here, not retained: the runtime state is Microsoft.Extensions.Logging
        // .LoggerMessageState, which is POOLED and reset once Log returns. Holding the reference
        // and reading it afterwards yields an EMPTY list, which would make every assertion over it
        // vacuously pass. Measured — the vacuity guard in the payload test caught exactly that on
        // the first attempt.
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Records.Add(new Entry(
                logLevel,
                eventId,
                formatter(state, exception),
                state is IReadOnlyList<KeyValuePair<string, object?>> kvps
                    ? kvps.ToArray()
                    : []));
    }

    private static (NullEmailSender Sender, RecordingLogger<NullEmailSender> Log) Create()
    {
        var log = new RecordingLogger<NullEmailSender>();
        return (new NullEmailSender(log), log);
    }

    [Fact]
    public async Task Suppression_SplitsLevelByConsequence_NotUniformly()
    {
        var (sender, log) = Create();
        var ct = CancellationToken.None;

        // One consequential kind and one notification kind, through the real methods rather than
        // the private log helpers — a pin on the helper would not catch a call site wired to the
        // wrong one, which is the mistake this split makes possible.
        await sender.SendEmailConfirmationAsync(
            "user@example.com", new EmailConfirmationEmail(Guid.NewGuid(), "tok"), ct);
        await sender.SendMatchNotificationEmailAsync(
            "user@example.com",
            new MatchNotificationEmail(MatchNotificationKind.Direct, null, [], 0),
            ct);

        var stranding = log.Records.Where(r => r.EventId.Id == 3007).ShouldHaveSingleItem();
        stranding.Level.ShouldBe(
            LogLevel.Warning,
            "Debug is filtered out in every environment where this sender is registered, so a "
            + "stranding drop logged below Information reaches no operator at all");

        var convenience = log.Records.Where(r => r.EventId.Id == 3002).ShouldHaveSingleItem();
        convenience.Level.ShouldBe(
            LogLevel.Debug,
            "a missed notification strands nobody; raising it would drown the signal above");
    }

    [Theory]
    [InlineData("email-confirmation")]
    [InlineData("email-changed-notification")]
    [InlineData("account-exists-notice")]
    [InlineData("email-change-confirmation")]
    [InlineData("password-reset")]
    [InlineData("password-changed-notice")]
    public async Task EveryAccountLifecycleKind_LogsAtWarning(string expectedKind)
    {
        var (sender, log) = Create();
        var ct = CancellationToken.None;
        var userId = Guid.NewGuid();

        // All six, so the mapping is pinned kind by kind rather than by one representative. Three are
        // UNREACHABLE in production, but by TWO different mechanisms and the distinction matters:
        //   · email-change-confirmation and password-reset — their callers READ CanDeliver and refuse
        //     before minting or sending (#1087, #1171).
        //   · password-changed-notice — its caller has NO CanDeliver branch. It is unreachable
        //     INDIRECTLY: no reset token can be minted while the sender cannot deliver, so the event
        //     this notice reports cannot occur (the same trigger-unreachability argument
        //     security-auditor accepted 2026-08-09 for the old-address notice).
        // All three are raised at Warning anyway: if one ever fires, an invariant broke, which is a
        // louder event than a missing provider, not a quieter one.
        await sender.SendEmailConfirmationAsync(
            "user@example.com", new EmailConfirmationEmail(userId, "tok"), ct);
        await sender.SendEmailChangedNotificationAsync("old@example.com", ct);
        await sender.SendAccountExistsNoticeAsync("taken@example.com", ct);
        await sender.SendEmailChangeConfirmationAsync(
            "new@example.com", new EmailChangeConfirmationEmail(userId, "new@example.com", "tok"), ct);
        await sender.SendPasswordResetAsync(
            "user@example.com", new PasswordResetEmail(userId, "tok"), ct);
        await sender.SendPasswordChangedNoticeAsync("user@example.com", ct);

        var record = log.Records
            .Where(r => r.Message.Contains(expectedKind, StringComparison.Ordinal))
            .ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.EventId.Id.ShouldBe(3007);
    }

    [Fact]
    public async Task SuppressionLog_CarriesTheKindAndNothingElse()
    {
        // Warning reaches a durable sink, which is exactly when a recipient added later "for
        // debuggability" becomes durable PII (CLAUDE.md §11, #1208).
        //
        // Asserted over the STRUCTURED STATE as well as the rendered message, because the message
        // alone cannot see the dangerous case: [LoggerMessage] promotes every method parameter to a
        // structured property, but only TEMPLATED tokens reach the text. Adding a `toEmail`
        // parameter without touching the template would put an address in Seq while leaving the
        // rendered line spotless — the variant hardest to catch in review, and the one an
        // assertion over Message would wave through (code-reviewer Minor D3, 2026-08-09).
        var (sender, log) = Create();
        const string Recipient = "stranded.person@example.com";
        const string Token = "opaque-url-safe-token"; // gitleaks:allow
        var userId = Guid.NewGuid();

        await sender.SendEmailConfirmationAsync(
            Recipient, new EmailConfirmationEmail(userId, Token), CancellationToken.None);

        var record = log.Records.ShouldHaveSingleItem();
        record.Message.ShouldNotContain(Recipient);
        record.Message.ShouldNotContain(Token);
        record.Message.ShouldNotContain(userId.ToString());
        record.Message.ShouldContain("email-confirmation");

        // Every structured value, not only the ones the template happens to render.
        var values = record.State.Select(kv => kv.Value?.ToString() ?? string.Empty).ToList();
        values.ShouldNotContain(v => v.Contains(Recipient, StringComparison.Ordinal));
        values.ShouldNotContain(v => v.Contains(Token, StringComparison.Ordinal));
        values.ShouldNotContain(v => v.Contains(userId.ToString(), StringComparison.Ordinal));

        // The state must actually carry something, or the three assertions above are vacuous
        // against an empty list — the failure mode a `state as ... ?? []` fallback introduces.
        record.State.ShouldContain(kv => kv.Key == "EmailKind" && Equals(kv.Value, "email-confirmation"));
    }
}
