using System.Text;
using Jobbliggaren.Application.Admin.Backup;
using Jobbliggaren.Infrastructure.Admin.HostBridge;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Backup;

/// <summary>
/// The reader and the backup adapter against real files in a real temporary directory. The success paths
/// read the golden files the host sampler publishes; the refusals are bytes the sampler never writes, which
/// is the point of a refusal.
/// </summary>
public sealed class BackupSampleSourceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jbl-hostbridge-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingLogger<HostBridgeFileReader> _log = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public BackupSampleSourceTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private BackupSampleSource SourceFor(string? directory) =>
        new(new HostBridgeFileReader(Options.Create(new HostBridgeOptions { Directory = directory }), _log));

    private string BackupFile => Path.Combine(_directory, BackupSampleSource.FileName);

    private void Publish(string content) => File.WriteAllText(BackupFile, content, new UTF8Encoding(false));

    private void PublishGolden(string fixture) =>
        File.Copy(Path.Combine(AppContext.BaseDirectory, "HostBridgeFixtures", fixture), BackupFile, overwrite: true);

    private async Task<BackupSampleRead> ReadAsync() => await SourceFor(_directory).ReadAsync(Ct);

    private static readonly DateTimeOffset Sampled = new(2026, 10, 10, 13, 41, 2, TimeSpan.Zero);

    // ---- the golden files, one per state the sampler can publish ----

    [Fact]
    public async Task ReadAsync_ShouldReadARecordedStampAndAScheduledTimer()
    {
        PublishGolden("backup-recorded-scheduled.json");

        var read = await ReadAsync();

        read.Status.ShouldBe(BackupStatus.Observed);
        read.Sample.ShouldBe(new BackupSample(
            Sampled,
            new BackupStampSample.Recorded(
                new DateTimeOffset(2026, 10, 10, 2, 19, 7, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 10, 2, 15, 41, TimeSpan.Zero)),
            new BackupTimerSample.Scheduled(new DateTimeOffset(2026, 10, 10, 13, 49, 41, TimeSpan.Zero))));
    }

    [Fact]
    public async Task ReadAsync_ShouldReadTheStateTheBoxIsInUntilTheBackupIsSwitchedOn()
    {
        PublishGolden("backup-missing-inactive.json");

        var read = await ReadAsync();

        read.Sample.ShouldBe(new BackupSample(
            Sampled,
            new BackupStampSample.NotRecorded(BackupLastSuccessState.Missing),
            new BackupTimerSample.NotScheduled(BackupTimerState.Inactive)));
    }

    [Theory]
    [InlineData("backup-invalid-scheduled.json", BackupLastSuccessState.Invalid, BackupTimerState.Scheduled)]
    [InlineData("backup-unreadable-stamp.json", BackupLastSuccessState.Unreadable, BackupTimerState.Scheduled)]
    [InlineData("backup-recorded-notinstalled.json", BackupLastSuccessState.Recorded, BackupTimerState.NotInstalled)]
    [InlineData("backup-recorded-timer-unknown.json", BackupLastSuccessState.Recorded, BackupTimerState.Unknown)]
    [InlineData("backup-future-stamp.json", BackupLastSuccessState.Recorded, BackupTimerState.Scheduled)]
    public async Task ReadAsync_ShouldReadEveryOtherState_TheSamplerCanPublish(
        string fixture, BackupLastSuccessState stamp, BackupTimerState timer)
    {
        PublishGolden(fixture);

        var read = await ReadAsync();

        read.Status.ShouldBe(BackupStatus.Observed, fixture);
        read.Sample!.LastSuccess.State.ShouldBe(stamp, fixture);
        read.Sample.Timer.State.ShouldBe(timer, fixture);
    }

    [Fact]
    public async Task ReadAsync_ShouldReadASamplerErrorAsAFailure_WithTheSamplersOwnTime()
    {
        PublishGolden("backup-error.json");

        var read = await ReadAsync();

        read.Status.ShouldBe(BackupStatus.Failed);
        read.Reason.ShouldBe(BackupStatusReason.SamplerError);
        read.Sample.ShouldBeNull();
        read.SampledAt.ShouldBe(Sampled);
    }

    [Fact]
    public async Task ReadAsync_ShouldParseEveryGoldenFile_SoNoneIsLeftUntested()
    {
        var shipped = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "HostBridgeFixtures"), "backup-*.json")
            .Select(Path.GetFileName).Order().ToArray();

        foreach (var fixture in shipped)
        {
            PublishGolden(fixture!);
            var read = await ReadAsync();
            read.Reason.ShouldNotBe(BackupStatusReason.InvalidFormat, $"{fixture} is a golden file; it must parse");
            read.Reason.ShouldNotBe(BackupStatusReason.TooLarge, fixture);
        }

        shipped.Length.ShouldBeGreaterThanOrEqualTo(8);
    }

    // ---- no file ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/dir")]
    public async Task ReadAsync_ShouldReportNotConfigured_ForAnUnsetOrRelativeDirectory(string? directory)
    {
        var read = await SourceFor(directory).ReadAsync(Ct);

        read.Status.ShouldBe(BackupStatus.NotObserved);
        read.Reason.ShouldBe(BackupStatusReason.NotConfigured);
    }

    [Fact]
    public async Task ReadAsync_ShouldReportNotSampledYet_WhenTheFileIsAbsent()
    {
        var read = await ReadAsync();

        read.Status.ShouldBe(BackupStatus.NotObserved);
        read.Reason.ShouldBe(BackupStatusReason.NotSampledYet);
    }

    [Fact]
    public async Task ReadAsync_ShouldReportNotSampledYet_WhenTheDirectoryIsAbsent()
    {
        var read = await SourceFor(Path.Combine(_directory, "not-there")).ReadAsync(Ct);

        read.Reason.ShouldBe(BackupStatusReason.NotSampledYet);
    }

    [Fact]
    public async Task ReadAsync_ShouldIgnoreEveryFileButItsOne()
    {
        File.WriteAllText(Path.Combine(_directory, ".backup.Ab12Cd"), "{not json");
        File.WriteAllText(Path.Combine(_directory, "host.json"), "{not json");

        var read = await ReadAsync();

        read.Reason.ShouldBe(BackupStatusReason.NotSampledYet);
    }

    [Fact]
    public async Task ReadAsync_ShouldRefuseADirectoryWhereTheFileShouldBe()
    {
        Directory.CreateDirectory(BackupFile);

        var read = await ReadAsync();

        read.Status.ShouldBe(BackupStatus.Failed);
        read.Reason.ShouldBe(BackupStatusReason.NotARegularFile);
    }

    [Fact]
    public async Task ReadAsync_ShouldRefuseASymbolicLink()
    {
        var target = Path.Combine(_directory, "elsewhere.txt");
        File.WriteAllText(target, "{}");
        try
        {
            File.CreateSymbolicLink(BackupFile, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip("This host cannot create a symbolic link without privilege; the Linux CI run does.");
        }

        var read = await ReadAsync();

        read.Reason.ShouldBe(BackupStatusReason.NotARegularFile);
    }

    // ---- size ----

    [Fact]
    public async Task ReadAsync_ShouldRefuseAFileOverTheCap_AndAcceptOneAtIt()
    {
        var golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "HostBridgeFixtures", "backup-missing-inactive.json")).TrimEnd();

        Publish(golden + new string(' ', HostBridgeFileReader.MaxFileBytes - golden.Length));
        new FileInfo(BackupFile).Length.ShouldBe(HostBridgeFileReader.MaxFileBytes);
        (await ReadAsync()).Status.ShouldBe(BackupStatus.Observed);

        Publish(golden + new string(' ', HostBridgeFileReader.MaxFileBytes - golden.Length + 1));
        var read = await ReadAsync();

        read.Status.ShouldBe(BackupStatus.Failed);
        read.Reason.ShouldBe(BackupStatusReason.TooLarge);
    }

    // ---- syntax ----

    // JSON is written with apostrophes here and converted, so no case needs an escape.
    private static string J(string singleQuoted) => singleQuoted.Replace('\'', '"');

    private const string Head = "{'sch\u0065ma':1,'source':'backup','sampledAt':'2026-10-10T13:41:02Z',";
    private const string Good = "'data':{'lastSuccess':{'state':'miss\u0069ng'},'timer':{'state':'inactive'}}}";

    public static TheoryData<string, string> Refusals => new()
    {
        { "not json", "this is not json" },
        { "empty file", "" },
        { "truncated", J(Head + "'data':{'lastSuccess':{'state':'mis") },
        { "a list", "[]" },
        { "trailing garbage", J(Head + Good + " x") },
        { "trailing comma", J(Head + "'data':{'lastSuccess':{'state':'miss\u0069ng'},'timer':{'state':'inactive'},}}") },
        { "a comment", J(Head + Good + " // note") },
        { "duplicate key", J("{'sch\u0065ma':1,'sch\u0065ma':1,'source':'backup','sampledAt':'2026-10-10T13:41:02Z'," + Good) },
        { "duplicate nested key", J(Head + "'data':{'lastSuccess':{'state':'miss\u0069ng','state':'miss\u0069ng'},'timer':{'state':'inactive'}}}") },
        { "unknown top-level key", J("{'sch\u0065ma':1,'source':'backup','sampledAt':'2026-10-10T13:41:02Z','extra':1," + Good) },
        { "unknown nested key", J(Head + "'data':{'lastSuccess':{'state':'miss\u0069ng','path':'/var/lib/x'},'timer':{'state':'inactive'}}}") },
        { "data and error", J(Head + "'error':'collector-failed'," + Good) },
        { "neither data nor error", J(Head.TrimEnd(',') + "}") },
        { "schema 2", J("{'sch\u0065ma':2,'source':'backup','sampledAt':'2026-10-10T13:41:02Z'," + Good) },
        { "schema as text", J("{'sch\u0065ma':'1','source':'backup','sampledAt':'2026-10-10T13:41:02Z'," + Good) },
        { "schema 1.0", J("{'sch\u0065ma':1.0,'source':'backup','sampledAt':'2026-10-10T13:41:02Z'," + Good) },
        { "schema true", J("{'sch\u0065ma':true,'source':'backup','sampledAt':'2026-10-10T13:41:02Z'," + Good) },
        { "another source", J("{'sch\u0065ma':1,'source':'host','sampledAt':'2026-10-10T13:41:02Z'," + Good) },
        { "source in other case", J("{'sch\u0065ma':1,'source':'Backup','sampledAt':'2026-10-10T13:41:02Z'," + Good) },
        { "sampledAt with decimals", J("{'sch\u0065ma':1,'source':'backup','sampledAt':'2026-10-10T13:41:02.000Z'," + Good) },
        { "sampledAt with an offset", J("{'sch\u0065ma':1,'source':'backup','sampledAt':'2026-10-10T15:41:02+02:00'," + Good) },
        { "sampledAt with a plus-zero offset", J("{'sch\u0065ma':1,'source':'backup','sampledAt':'2026-10-10T13:41:02+00:00'," + Good) },
        { "sampledAt with a space", J("{'sch\u0065ma':1,'source':'backup','sampledAt':'2026-10-10 13:41:02Z'," + Good) },
        { "sampledAt padded", J("{'sch\u0065ma':1,'source':'backup','sampledAt':' 2026-10-10T13:41:02Z'," + Good) },
        { "sampledAt not a date", J("{'sch\u0065ma':1,'source':'backup','sampledAt':'2026-13-40T25:61:61Z'," + Good) },
        { "sampledAt a number", J("{'sch\u0065ma':1,'source':'backup','sampledAt':1760103662," + Good) },
        { "state in the wrong case", J(Head + "'data':{'lastSuccess':{'state':'Missing'},'timer':{'state':'inactive'}}}") },
        { "state as a number", J(Head + "'data':{'lastSuccess':{'state':1},'timer':{'state':'inactive'}}}") },
        { "state as a list", J(Head + "'data':{'lastSuccess':{'state':'missing,recorded'},'timer':{'state':'inactive'}}}") },
        { "unknown timer state", J(Head + "'data':{'lastSuccess':{'state':'miss\u0069ng'},'timer':{'state':'stopped'}}}") },
        { "recorded without a start", J(Head + "'data':{'lastSuccess':{'state':'recorded','completedAt':'2026-10-10T02:19:07Z'},'timer':{'state':'inactive'}}}") },
        { "recorded with a bad time", J(Head + "'data':{'lastSuccess':{'state':'recorded','completedAt':'2026-10-10T02:19:07','startedAt':'2026-10-10T02:15:41Z'},'timer':{'state':'inactive'}}}") },
        { "scheduled without a time", J(Head + "'data':{'lastSuccess':{'state':'miss\u0069ng'},'timer':{'state':'scheduled'}}}") },
        { "a time on a state that has none", J(Head + "'data':{'lastSuccess':{'state':'miss\u0069ng','completedAt':'2026-10-10T02:19:07Z'},'timer':{'state':'inactive'}}}") },
        { "recorded with an extra key", J(Head + "'data':{'lastSuccess':{'state':'recorded','completedAt':'2026-10-10T02:19:07Z','startedAt':'2026-10-10T02:15:41Z','x':1},'timer':{'state':'inactive'}}}") },
        { "scheduled with an extra key", J(Head + "'data':{'lastSuccess':{'state':'miss\u0069ng'},'timer':{'state':'scheduled','nextRunAt':'2026-10-10T13:49:41Z','x':1}}}") },
        { "unreadable with an extra key", J(Head + "'data':{'lastSuccess':{'state':'unreadable','x':1},'timer':{'state':'inactive'}}}") },
        { "invalid with an extra key", J(Head + "'data':{'lastSuccess':{'state':'invalid','x':1},'timer':{'state':'inactive'}}}") },
        { "inactive with an extra key", J(Head + "'data':{'lastSuccess':{'state':'missing'},'timer':{'state':'inactive','x':1}}}") },
        { "not installed with an extra key", J(Head + "'data':{'lastSuccess':{'state':'missing'},'timer':{'state':'notInstalled','x':1}}}") },
        { "unknown with an extra key", J(Head + "'data':{'lastSuccess':{'state':'missing'},'timer':{'state':'unknown','x':1}}}") },
        { "data with an extra key", J(Head + "'data':{'lastSuccess':{'state':'missing'},'timer':{'state':'inactive'},'x':1}}") },
        { "data missing the timer", J(Head + "'data':{'lastSuccess':{'state':'miss\u0069ng'}}}") },
        { "data as text", J(Head + "'data':'miss\u0069ng'}") },
        { "error as an object", J(Head + "'error':{'code':'collector-failed'}}") },
        { "an unknown error token", J(Head + "'error':'disk on fire'}") },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task ReadAsync_ShouldRefuseAFile_ThatBreaksTheContract(string why, string content)
    {
        Publish(content);

        var read = await ReadAsync();

        read.Status.ShouldBe(BackupStatus.Failed, why);
        read.Reason.ShouldBe(BackupStatusReason.InvalidFormat, why);
        read.Sample.ShouldBeNull(why);
    }

    [Fact]
    public async Task ReadAsync_ShouldRefuseInvalidUtf8()
    {
        var bytes = Encoding.UTF8.GetBytes(J(Head + Good));
        bytes[Head.Length + 2] = 0xFF; // inside the word "data"
        File.WriteAllBytes(BackupFile, bytes);

        var read = await ReadAsync();

        read.Reason.ShouldBe(BackupStatusReason.InvalidFormat);
    }

    [Fact]
    public async Task ReadAsync_ShouldRefuseAByteOrderMark()
    {
        File.WriteAllBytes(BackupFile, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(J(Head + Good))]);

        var read = await ReadAsync();

        read.Reason.ShouldBe(BackupStatusReason.InvalidFormat);
    }

    // ---- escapes: the sampler never writes a backslash, so the reader reads none ----

    public static TheoryData<string, string> Escapes => new()
    {
        { "a lone escaped surrogate in a value", Head + "'data':{'lastSuccess':{'state':'missing'},'timer':{'state':'inactive'}},'x':'\\ud800'}" },
        { "a lone escaped surrogate in a name", Head + "'data':{'lastSuccess':{'state':'missing'},'timer':{'state':'inactive'}},'\\udc00':1}" },
        { "an escaped spelling of a key", "{'sch\\u0065ma':1,'source':'backup','sampledAt':'2026-10-10T13:41:02Z'," + Good },
        { "an escaped spelling of a token", Head + "'data':{'lastSuccess':{'state':'miss\\u0069ng'},'timer':{'state':'inactive'}}}" },
        { "a valid file with one harmless escape", Head + "'data':{'lastSuccess':{'state':'missing'},'timer':{'state':'inactive'}},'k':'\\n'}" },
    };

    [Theory]
    [MemberData(nameof(Escapes))]
    public async Task ReadAsync_ShouldRefuseAnyEscape_WithoutAnExceptionOrAMessageInTheLog(string why, string content)
    {
        Publish(J(content));

        var read = await ReadAsync();

        read.Status.ShouldBe(BackupStatus.Failed, why);
        read.Reason.ShouldBe(BackupStatusReason.InvalidFormat, why);
        _log.Records.ShouldAllBe(record => record.Level != LogLevel.Error && !record.Message.Contains("surrogate"), why);
    }

    // ---- what only a Unix filesystem can make ----

    [Fact]
    public async Task ReadAsync_ShouldRefuseAFifo_WithoutBlockingOnIt()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("A FIFO needs a Unix filesystem; the Linux CI run makes one.");
        }

        using var made = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("mkfifo", $"\"{BackupFile}\"")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        });
        made!.WaitForExit();
        if (made.ExitCode != 0)
        {
            Assert.Skip("mkfifo is not available on this host.");
        }

        var read = await ReadAsync().WaitAsync(TimeSpan.FromSeconds(10), Ct);

        read.Status.ShouldBe(BackupStatus.Failed);
        read.Reason.ShouldBe(BackupStatusReason.InvalidFormat);
    }

    [Fact]
    public async Task ReadAsync_ShouldReportAFileItCannotOpenAsUnreadable_AndLogOnlyTheExceptionType()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            Assert.Skip("Needs a Unix filesystem and a user a mode-000 file stops; the Linux CI runner is one.");
        }

        PublishGolden("backup-recorded-scheduled.json");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(BackupFile, UnixFileMode.None);
        }

        var read = await ReadAsync();

        read.Status.ShouldBe(BackupStatus.Failed);
        read.Reason.ShouldBe(BackupStatusReason.Unreadable);
        _log.Records.ShouldHaveSingleItem().Message.ShouldBe($"Host bridge file {BackupSampleSource.FileName} could not be read (UnauthorizedAccessException)");
    }

    // ---- what the log may carry ----

    [Fact]
    public async Task ReadAsync_ShouldLogNeitherTheFilesContentNorAnExceptionMessage()
    {
        const string secret = "SECRET-VALUE-4711";
        Publish("{\"" + secret + "\": nope");

        await ReadAsync();

        _log.Records.ShouldNotBeEmpty("a refused file is logged, with its reason");
        foreach (var record in _log.Records)
        {
            record.Message.ShouldNotContain(secret);
            record.Properties.Select(p => p.Value?.ToString()).ShouldAllBe(value => value == null || !value.Contains(secret));
        }
    }

    [Fact]
    public async Task ReadAsync_ShouldNotLog_ForTheExpectedAbsenceOfTheFile()
    {
        await ReadAsync();
        await SourceFor(null).ReadAsync(Ct);

        _log.Records.ShouldBeEmpty();
    }

    // ---- cancellation ----

    [Fact]
    public async Task ReadAsync_ShouldPropagateCancellation_NotReportItAsAFailure()
    {
        PublishGolden("backup-recorded-scheduled.json");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        OperationCanceledException? thrown = null;
        try
        {
            await SourceFor(_directory).ReadAsync(cancelled.Token);
        }
        catch (OperationCanceledException exception)
        {
            thrown = exception;
        }

        thrown.ShouldNotBeNull();
    }
}
