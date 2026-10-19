using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.CompanyWatches;

/// <summary>
/// ADR 0175 D5 (#1663) — invariants for <see cref="CompanyWatchCriterionAdHit"/>: the owner comes from the
/// criterion, the clock is read per hit, and the bell's flags start false with nothing in the type able to set
/// them (ADR 0169 V20).
/// </summary>
public class CompanyWatchCriterionAdHitTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    private static CompanyWatchCriterion Criterion(Guid userId) =>
        CompanyWatchCriterion.Create(
            userId, CompanyWatchCriteriaSpec.Create(["62010"], ["0180"]).Value, null, Clock).Value;

    [Fact]
    public void Create_TakesTheOwnerAndTheCriterionIdFromTheCriterion()
    {
        var owner = Guid.NewGuid();
        var criterion = Criterion(owner);
        var jobAdId = JobAdId.New();

        var result = CompanyWatchCriterionAdHit.Create(criterion, jobAdId, Clock);

        result.IsSuccess.ShouldBeTrue();
        var hit = result.Value;
        hit.UserId.ShouldBe(owner);
        hit.CompanyWatchCriterionId.ShouldBe(criterion.Id);
        hit.JobAdId.ShouldBe(jobAdId);
        hit.CreatedAt.ShouldBe(Clock.UtcNow);
        hit.Id.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Create_ReadsTheClockOnEveryCall()
    {
        var criterion = Criterion(Guid.NewGuid());
        var clock = new SteppingClock(Clock.UtcNow, TimeSpan.FromMilliseconds(1));

        var first = CompanyWatchCriterionAdHit.Create(criterion, JobAdId.New(), clock).Value;
        var second = CompanyWatchCriterionAdHit.Create(criterion, JobAdId.New(), clock).Value;

        second.CreatedAt.ShouldBeGreaterThan(first.CreatedAt);
        clock.Reads.ShouldBe(2);
    }

    [Fact]
    public void Create_WithNullCriterion_Fails()
    {
        var result = CompanyWatchCriterionAdHit.Create(null!, JobAdId.New(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriterionAdHit.CriterionRequired");
    }

    [Fact]
    public void Create_WithDefaultJobAdId_Fails()
    {
        var result = CompanyWatchCriterionAdHit.Create(Criterion(Guid.NewGuid()), default, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriterionAdHit.JobAdIdRequired");
    }

    [Fact]
    public void Create_StartsUnreadAndNotDismissed()
    {
        var hit = CompanyWatchCriterionAdHit.Create(Criterion(Guid.NewGuid()), JobAdId.New(), Clock).Value;

        hit.ReadInNotificationFeed.ShouldBeFalse();
        hit.DismissedFromNotificationFeed.ShouldBeFalse();
    }

    [Fact]
    public void Type_ExposesNoPublicInstanceMethod_SoNoBellFlagHasAWriter()
    {
        // B6 / ADR 0169 V20: the only writer of either flag is the user's click, which arrives with the bell
        // source in PR 3. Until then the type exposes no way to set them.
        var writers = typeof(CompanyWatchCriterionAdHit)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name);

        writers.ShouldBeEmpty();
    }

    [Fact]
    public void Create_RaisesNoDomainEvents()
    {
        var hit = CompanyWatchCriterionAdHit.Create(Criterion(Guid.NewGuid()), JobAdId.New(), Clock).Value;

        hit.DomainEvents.ShouldBeEmpty();
    }

    private sealed class SteppingClock(DateTimeOffset start, TimeSpan step) : IDateTimeProvider
    {
        private DateTimeOffset _next = start;

        public int Reads { get; private set; }

        public DateTimeOffset UtcNow
        {
            get
            {
                Reads++;
                var value = _next;
                _next += step;
                return value;
            }
        }
    }
}
