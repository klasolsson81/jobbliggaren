using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Waitlist;

namespace Jobbliggaren.Application.UnitTests.Common;

/// <summary>
/// Hjälpfabrik för WaitlistEntry-instansiering i tester. Bär default-värden
/// för Name/Motivation/Acceptance så testfall som inte fokuserar på dessa
/// fält förblir koncisa.
/// </summary>
internal static class TestWaitlistFactory
{
    private const string DefaultName = "Klas Testperson";
    private const string DefaultMotivation =
        "Jag vill testa Jobbliggaren för att hantera mina jobbansökningar.";

    public static WaitlistEntry CreatePending(
        string email,
        IDateTimeProvider clock,
        string? name = null,
        string? motivation = null) =>
        WaitlistEntry.Request(
            email,
            name ?? DefaultName,
            motivation ?? DefaultMotivation,
            DefaultAcceptance(clock),
            clock).Value;

    public static AcceptanceSnapshot DefaultAcceptance(IDateTimeProvider clock) =>
        new(
            MarketingEmailAccepted: false,
            AcceptedAt: clock.UtcNow,
            PrivacyPolicyVersion: "1.0");
}
