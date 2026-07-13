using Jobbliggaren.Application.Common.Telemetry;
using Jobbliggaren.Application.UnitTests.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Telemetry;

/// <summary>
/// ADR 0045 Beslut 3 — Worker memory trend sampler. Verifies the edge-triggered
/// warn/recovery state machine against the REAL 512 MiB shipped default (CTO
/// bind #754 Q2 — "hand the fake probe 600 MiB against the real 512 MiB cap",
/// not a lowered test-only threshold), plus the non-negotiable stability
/// guard (a probe failure must never propagate — CTO bind #754 Q1).
/// </summary>
public class WorkerMemoryTrendSamplerTests
{
    private static WorkerMemoryTrendSampler CreateSampler(
        ScriptedProcessMemoryProbe probe, RecordingLogger<WorkerMemoryTrendSampler>? recorder = null) =>
        new(probe, Options.Create(new WorkerMemoryTrendOptions()), recorder ?? new RecordingLogger<WorkerMemoryTrendSampler>());

    [Fact]
    public void Defaults_MatchAdr0045Beslut3ShippedCap()
    {
        // Guards against a silent drift of the shipped default — every test
        // below runs against THIS value, never a lowered one.
        var opts = new WorkerMemoryTrendOptions();

        opts.SoftCapMiB.ShouldBe(512);
        opts.SampleIntervalSeconds.ShouldBe(60);
    }

    [Fact]
    public void Sample_SequenceCrossingCapTwice_WarnsExactlyOnceAndRecoversExactlyOnce()
    {
        var probe = new ScriptedProcessMemoryProbe(
            ScriptedProcessMemoryProbe.AtMiB(100),
            ScriptedProcessMemoryProbe.AtMiB(600),
            ScriptedProcessMemoryProbe.AtMiB(700),
            ScriptedProcessMemoryProbe.AtMiB(100));
        var recorder = new RecordingLogger<WorkerMemoryTrendSampler>();
        var sampler = CreateSampler(probe, recorder);

        sampler.Sample(); // 100 MiB — below cap
        sampler.Sample(); // 600 MiB — crosses ABOVE → warn
        sampler.Sample(); // 700 MiB — still above → no additional warn (edge-triggered)
        sampler.Sample(); // 100 MiB — crosses BELOW → recovery

        recorder.Records.Count(r => r.EventId.Id == 6211).ShouldBe(1, "exactly one WorkerMemoryAboveSoftCap warn expected");
        recorder.Records.Count(r => r.EventId.Id == 6212).ShouldBe(1, "exactly one WorkerMemoryBackWithinSoftCap recovery expected");
    }

    [Fact]
    public void Sample_EveryTick_EmitsInformationTrendRegardlessOfCapState()
    {
        var probe = new ScriptedProcessMemoryProbe(
            ScriptedProcessMemoryProbe.AtMiB(100),
            ScriptedProcessMemoryProbe.AtMiB(600),
            ScriptedProcessMemoryProbe.AtMiB(700));
        var recorder = new RecordingLogger<WorkerMemoryTrendSampler>();
        var sampler = CreateSampler(probe, recorder);

        sampler.Sample();
        sampler.Sample();
        sampler.Sample();

        recorder.Records.Count(r => r.EventId.Id == 6210).ShouldBe(3,
            "the per-tick trend event must fire on every tick, above or below the cap");
    }

    [Fact]
    public void Sample_FirstSampleAlreadyAboveCap_Warns()
    {
        // Edge state initialises false — a first sample already above the cap
        // is a false→true transition and MUST fire (CTO bind #754 Q4).
        var probe = new ScriptedProcessMemoryProbe(ScriptedProcessMemoryProbe.AtMiB(600));
        var recorder = new RecordingLogger<WorkerMemoryTrendSampler>();
        var sampler = CreateSampler(probe, recorder);

        sampler.Sample();

        recorder.Records.Count(r => r.EventId.Id == 6211).ShouldBe(1);
    }

    [Fact]
    public void Sample_AtExactlyTheCap_DoesNotWarn()
    {
        // Boundary: the cap is a ceiling — exactly AT the cap is not yet a
        // breach (strict >, not >=).
        var probe = new ScriptedProcessMemoryProbe(ScriptedProcessMemoryProbe.AtMiB(512));
        var recorder = new RecordingLogger<WorkerMemoryTrendSampler>();
        var sampler = CreateSampler(probe, recorder);

        sampler.Sample();

        recorder.Records.Count(r => r.EventId.Id == 6211).ShouldBe(0);
    }

    [Fact]
    public void Sample_JustBelowThenJustAboveCap_StillWarns()
    {
        // Fine-grained boundary check, distinct from the round-number 100/600
        // sequence above.
        var probe = new ScriptedProcessMemoryProbe(
            ScriptedProcessMemoryProbe.AtMiB(511.9),
            ScriptedProcessMemoryProbe.AtMiB(512.1));
        var recorder = new RecordingLogger<WorkerMemoryTrendSampler>();
        var sampler = CreateSampler(probe, recorder);

        sampler.Sample();
        sampler.Sample();

        recorder.Records.Count(r => r.EventId.Id == 6211).ShouldBe(1);
    }

    [Fact]
    public void Sample_ProbeThrows_DoesNotPropagate_AndLogsWarning()
    {
        // Stability invariant (CTO bind #754 Q1, Nygard Release It! 2nd 2018):
        // a telemetry component must never fault the process it monitors.
        var probe = new ScriptedProcessMemoryProbe { ThrowOnNextSample = new InvalidOperationException("probe boom") };
        var recorder = new RecordingLogger<WorkerMemoryTrendSampler>();
        var sampler = CreateSampler(probe, recorder);

        Should.NotThrow(() => sampler.Sample());

        recorder.Records.ShouldContain(r => r.Level == LogLevel.Warning && r.EventId.Id == 6213);
    }

    [Fact]
    public void Sample_ProbeThrowsThenRecovers_ResumesNormalSamplingOnNextTick()
    {
        // A single bad tick must not wedge the sampler — the next tick should
        // sample normally again.
        var probe = new ScriptedProcessMemoryProbe(ScriptedProcessMemoryProbe.AtMiB(100))
        {
            ThrowOnNextSample = new InvalidOperationException("transient probe boom"),
        };
        var recorder = new RecordingLogger<WorkerMemoryTrendSampler>();
        var sampler = CreateSampler(probe, recorder);

        sampler.Sample(); // throws internally, swallowed
        sampler.Sample(); // normal 100 MiB sample

        recorder.Records.Count(r => r.EventId.Id == 6210).ShouldBe(1, "the tick AFTER the failure must still produce a trend event");
    }
}
