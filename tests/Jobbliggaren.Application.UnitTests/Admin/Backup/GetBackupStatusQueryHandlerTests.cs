using System.Text.Json;
using Jobbliggaren.Application.Admin.Backup;
using Jobbliggaren.Application.Admin.Backup.Queries.GetBackupStatus;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.Common.Behaviors;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Backup;

public class GetBackupStatusQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 13, 41, 30, TimeSpan.Zero);
    private static readonly DateTimeOffset Sampled = new(2026, 10, 10, 13, 41, 2, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IDateTimeProvider ClockAt(DateTimeOffset now)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        return clock;
    }

    private static BackupSample AnySample() => new(
        Sampled,
        new BackupStampSample(BackupLastSuccessState.Missing),
        new BackupTimerSample(BackupTimerState.Inactive));

    private sealed class FakeSource(Func<CancellationToken, ValueTask<BackupSampleRead>> read) : IBackupSampleSource
    {
        public int Reads { get; private set; }

        public ValueTask<BackupSampleRead> ReadAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return read(cancellationToken);
        }
    }

    private static FakeSource Returning(BackupSampleRead read) => new(_ => ValueTask.FromResult(read));

    [Fact]
    public async Task Handle_ShouldJudgeTheSampleAgainstTheInjectedClock()
    {
        var source = Returning(BackupSampleRead.Sampled(AnySample()));

        var fresh = await new GetBackupStatusQueryHandler(source, ClockAt(Now)).Handle(new GetBackupStatusQuery(), Ct);
        var old = await new GetBackupStatusQueryHandler(source, ClockAt(Now.AddHours(3))).Handle(new GetBackupStatusQuery(), Ct);

        fresh.Stale.ShouldBe(false);
        old.Stale.ShouldBe(true);
        old.ObservedAt.ShouldBe(Sampled);
    }

    [Fact]
    public async Task Handle_ShouldPassTheCallersCancellationToken_ToTheSource()
    {
        CancellationToken seen = default;
        var source = new FakeSource(token =>
        {
            seen = token;
            return ValueTask.FromResult(BackupSampleRead.NotObserved(BackupStatusReason.NotSampledYet));
        });
        using var cancellation = new CancellationTokenSource();

        await new GetBackupStatusQueryHandler(source, ClockAt(Now)).Handle(new GetBackupStatusQuery(), cancellation.Token);

        seen.ShouldBe(cancellation.Token);
    }

    [Fact]
    public async Task Handle_ShouldPropagateCancellation_FromTheSource()
    {
        var source = new FakeSource(token => throw new OperationCanceledException(token));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        OperationCanceledException? thrown = null;
        try
        {
            await new GetBackupStatusQueryHandler(source, ClockAt(Now)).Handle(new GetBackupStatusQuery(), cancelled.Token);
        }
        catch (OperationCanceledException exception)
        {
            thrown = exception;
        }

        thrown.ShouldNotBeNull();
    }

    [Fact]
    public async Task Handle_ShouldNotSwallowAnUnexpectedFailure_OfTheSource()
    {
        var failure = new InvalidOperationException("boom");
        var source = new FakeSource(_ => throw failure);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await new GetBackupStatusQueryHandler(source, ClockAt(Now)).Handle(new GetBackupStatusQuery(), Ct));

        thrown.ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task Authorization_ShouldRefuseAnOrdinaryCaller_BeforeTheSourceIsRead()
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsInRole(Roles.Admin).Returns(false);
        var source = Returning(BackupSampleRead.NotObserved(BackupStatusReason.NotSampledYet));
        var handler = new GetBackupStatusQueryHandler(source, ClockAt(Now));
        var behavior = new AdminAuthorizationBehavior<GetBackupStatusQuery, BackupStatusDto>(user);

        await Should.ThrowAsync<ForbiddenException>(async () =>
            await behavior.Handle(new GetBackupStatusQuery(), handler.Handle, Ct));

        source.Reads.ShouldBe(0);
    }

    // The browser receives exactly these keys and these tokens. A new field is a deliberate change to the
    // contract and to this test; startedAt, a path or a message must never appear.
    [Fact]
    public void Dto_ShouldSerialiseToTheClosedKeySetTheBrowserIsPromised()
    {
        var observed = BackupStatusDto.Observed(
            Sampled, false,
            new BackupLastSuccessDto(BackupLastSuccessState.Recorded, Sampled.AddHours(-11), false),
            new BackupTimerDto(BackupTimerState.Scheduled, Sampled.AddHours(12)));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(observed, JsonSerializerOptions.Web));
        var root = document.RootElement;

        Keys(root).ShouldBe(["lastSuccess", "observedAt", "reason", "stale", "status", "timer"]);
        Keys(root.GetProperty("lastSuccess")).ShouldBe(["completedAt", "overdue", "state"]);
        Keys(root.GetProperty("timer")).ShouldBe(["nextRunAt", "state"]);
        root.GetProperty("status").GetString().ShouldBe("Observed");
        root.GetProperty("lastSuccess").GetProperty("state").GetString().ShouldBe("Recorded");
        root.GetProperty("timer").GetProperty("state").GetString().ShouldBe("Scheduled");
        root.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void Dto_ShouldSerialiseTheReasonAsAToken_ForEveryStatusThatHasOne()
    {
        foreach (var dto in new[]
        {
            BackupStatusDto.NotObserved(BackupStatusReason.NotSampledYet),
            BackupStatusDto.Failed(BackupStatusReason.InvalidFormat),
        })
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(dto, JsonSerializerOptions.Web));
            Keys(document.RootElement).ShouldBe(["lastSuccess", "observedAt", "reason", "stale", "status", "timer"]);
            document.RootElement.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.String);
            document.RootElement.GetProperty("lastSuccess").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }

    private static string[] Keys(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
}
