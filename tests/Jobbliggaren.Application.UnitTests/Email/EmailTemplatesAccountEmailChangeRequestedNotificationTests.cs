using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #1975 (ADR 0153) — the notice an account's CURRENT address gets when an administrator starts a change of it. It is
/// what turns the delay into prevention: its holder learns of the change while it still waits. What is pinned: it says
/// when the change can complete at the earliest and until when the code works, in Swedish time, and how to object; and
/// it carries what <see cref="EmailTemplates.EmailChangedNotification"/> does not: no code, no site link and no address
/// but ours.
/// </summary>
public sealed class EmailTemplatesAccountEmailChangeRequestedNotificationTests
{
    // 12:30 UTC is 14:30 in Stockholm while summer time holds (it ends on 2026-10-25).
    private static readonly DateTimeOffset CompletableFrom = new(2026, 10, 8, 12, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAt = CompletableFrom.AddHours(24);

    private static EmailTemplates.EmailContent Render() =>
        EmailTemplates.AccountEmailChangeRequestedNotification(CompletableFrom, ExpiresAt);

    [Fact]
    public void It_names_the_earliest_completion_and_the_codes_end_in_swedish_time_in_both_parts()
    {
        var rendered = Render();

        foreach (var paragraphs in new[]
                 {
                     MailText.PlainParagraphs(rendered.PlainTextBody), MailText.HtmlParagraphs(rendered.HtmlBody),
                 })
        {
            paragraphs.ShouldContain(
                "Bytet kan göras tidigast 2026-10-08 kl 14:30, och bara med koden som har skickats till den nya "
                + "adressen. Koden gäller till 2026-10-09 kl 14:30.");
        }
    }

    [Fact]
    public void An_instant_in_winter_time_is_read_in_winter_time()
    {
        var rendered = EmailTemplates.AccountEmailChangeRequestedNotification(
            new DateTimeOffset(2026, 10, 26, 12, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 27, 12, 30, 0, TimeSpan.Zero));

        MailText.Unwrapped(rendered.PlainTextBody).ShouldContain("tidigast 2026-10-26 kl 13:30");
    }

    [Fact]
    public void It_tells_the_holder_how_to_object_as_one_whole_paragraph_of_both_parts()
    {
        const string objection =
            "Om du inte känner igen begäran, skriv till oss så snart du kan: " + EmailTemplates.ContactAddress;
        var rendered = Render();

        rendered.Subject.ShouldBe("Begäran om att byta e-postadress på ditt konto");
        MailText.PlainParagraphs(rendered.PlainTextBody).ShouldContain(objection);
        MailText.HtmlParagraphs(rendered.HtmlBody).ShouldContain(objection);
        rendered.HtmlBody.ShouldContain($"mailto:{EmailTemplates.ContactAddress}");
    }

    [Fact]
    public void It_carries_no_code_no_site_link_and_no_address_but_ours()
    {
        // The template is handed no code and no address, so neither can reach it; the site link is the property
        // EmailChangedNotification keeps for the same reason, since the notice reaches an address that may not be
        // the one asking.
        var rendered = Render();

        rendered.PlainTextBody.ShouldNotContain("https://");
        rendered.HtmlBody.ShouldNotContain("href=\"https://");
        foreach (var token in rendered.PlainTextBody.Split(
                     [' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Contains('@', StringComparison.Ordinal))
                token.Trim('.', ',', ':', ')').ShouldBe(EmailTemplates.ContactAddress);
        }

        typeof(EmailTemplates).GetMethod(nameof(EmailTemplates.AccountEmailChangeRequestedNotification))!
            .GetParameters().Select(parameter => parameter.ParameterType)
            .ShouldBe([typeof(DateTimeOffset), typeof(DateTimeOffset)]);
    }

    [Fact]
    public void It_keeps_the_civic_tone()
    {
        var rendered = Render();

        foreach (var part in new[] { rendered.Subject, rendered.PlainTextBody })
        {
            part.ShouldNotContain("!");
            part.ShouldNotContain("—");
        }
    }
}
