using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// Pins the lifetime of an address change an administrator starts to ADR 0153's literals (#1975). The numbers are spelled
/// out, as in <see cref="LoginChallengePolicyTests"/>: a test that read the constant back would move with it.
/// </summary>
public sealed class AccountEmailChangePolicyTests
{
    private const string Stated =
        "ADR 0153: D is Klas's decision (2026-10-04) and W is NIST SP 800-63B-4 §4.2.1.2's 24 h; the mails and the "
        + "privacy policy state both, and lapse trigger 5 reads them";

    [Fact]
    public void A_change_completes_no_sooner_than_seventy_two_hours_and_its_code_is_usable_for_twenty_four()
    {
        AccountEmailChangePolicy.Delay.ShouldBe(TimeSpan.FromHours(72), Stated);
        AccountEmailChangePolicy.UsableWindow.ShouldBe(TimeSpan.FromHours(24), Stated);
        AccountEmailChangePolicy.Ttl.ShouldBe(TimeSpan.FromHours(96), Stated);
    }

    [Fact]
    public void The_records_life_is_whole_seconds_because_the_volatile_acl_grants_expire_and_never_pexpire()
    {
        (AccountEmailChangePolicy.Ttl.Ticks % TimeSpan.TicksPerSecond).ShouldBe(0);
    }
}
