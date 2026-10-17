using Jobbliggaren.Infrastructure.Auth.Activity;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth.Activity;

/// <summary>
/// #2161 (ADR 0172 D4) — the per-process claim on (account, UTC minute) that bounds the request path to one write per
/// account per minute. The minutes are the ones <see cref="AccountActivityClaims.MinuteOf"/> derives from the clock,
/// which is what <c>SqlAccountActivityRecorder</c> passes in.
/// </summary>
public sealed class AccountActivityClaimsTests
{
    private static readonly DateTimeOffset Minute = new(2026, 10, 17, 9, 41, 0, TimeSpan.Zero);
    private readonly AccountActivityClaims _claims = new();
    private readonly Guid _account = Guid.NewGuid();

    [Fact]
    public void MinuteOf_truncates_to_the_utc_minute_whatever_the_offset()
    {
        var local = new DateTimeOffset(2026, 10, 17, 11, 41, 59, 999, TimeSpan.FromHours(2));

        var minute = AccountActivityClaims.MinuteOf(local);

        minute.ShouldBe(Minute);
        minute.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void The_first_request_of_a_minute_claims_it_and_the_rest_of_that_minute_does_not()
    {
        _claims.TryClaim(_account, Minute).ShouldBeTrue();

        _claims.TryClaim(_account, Minute).ShouldBeFalse();
        _claims.TryClaim(_account, AccountActivityClaims.MinuteOf(Minute.AddSeconds(59))).ShouldBeFalse();
    }

    [Fact]
    public void The_next_minute_is_claimed_again()
    {
        _claims.TryClaim(_account, Minute).ShouldBeTrue();

        _claims.TryClaim(_account, Minute.AddMinutes(1)).ShouldBeTrue();
        _claims.TryClaim(_account, Minute.AddMinutes(1)).ShouldBeFalse();
    }

    [Fact]
    public void An_earlier_minute_after_a_later_one_is_not_claimed()
    {
        _claims.TryClaim(_account, Minute.AddMinutes(1)).ShouldBeTrue();

        _claims.TryClaim(_account, Minute).ShouldBeFalse();
    }

    [Fact]
    public void Each_account_claims_its_own_minute()
    {
        var other = Guid.NewGuid();
        _claims.TryClaim(_account, Minute).ShouldBeTrue();

        _claims.TryClaim(other, Minute).ShouldBeTrue();
    }

    [Fact]
    public void A_login_primes_its_minute_so_the_requests_of_that_minute_write_nothing()
    {
        _claims.Prime(_account, Minute);

        _claims.TryClaim(_account, Minute).ShouldBeFalse();
        _claims.TryClaim(_account, Minute.AddMinutes(1)).ShouldBeTrue();
    }

    [Fact]
    public void Priming_an_earlier_minute_does_not_reopen_a_later_claim()
    {
        _claims.TryClaim(_account, Minute.AddMinutes(1)).ShouldBeTrue();

        _claims.Prime(_account, Minute);

        _claims.TryClaim(_account, Minute.AddMinutes(1)).ShouldBeFalse();
    }

    [Fact]
    public async Task Concurrent_requests_of_one_account_claim_a_minute_exactly_once()
    {
        using var start = new ManualResetEventSlim();
        var attempts = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() =>
            {
                start.Wait(TestContext.Current.CancellationToken);
                return Enumerable.Range(0, 100).Count(_ => _claims.TryClaim(_account, Minute));
            }))
            .ToList();

        start.Set();
        var claimed = await Task.WhenAll(attempts);

        claimed.Sum().ShouldBe(1);
    }

    [Fact]
    public void Pruning_a_new_minute_keeps_the_claims_made_in_it()
    {
        var other = Guid.NewGuid();
        _claims.TryClaim(_account, Minute).ShouldBeTrue();

        // The other account's first claim of the next minute prunes the earlier entries.
        _claims.TryClaim(other, Minute.AddMinutes(1)).ShouldBeTrue();

        _claims.TryClaim(_account, Minute.AddMinutes(1)).ShouldBeTrue();
        _claims.TryClaim(_account, Minute.AddMinutes(1)).ShouldBeFalse();
        _claims.TryClaim(other, Minute.AddMinutes(1)).ShouldBeFalse();
    }
}
