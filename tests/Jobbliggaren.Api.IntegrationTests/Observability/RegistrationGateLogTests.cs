using Jobbliggaren.Api.Observability;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Observability;

/// <summary>
/// #1191 — the boot announcement over all four inputs. OPEN outside Development is the posture that has to be
/// alertable, so it alone is the Warning (4301); the other three are the routine line (4300). Both carry
/// <c>{RegistrationGateState}</c>, so a query keyed on that property finds either.
/// </summary>
public class RegistrationGateLogTests
{
    [Theory]
    [InlineData(false, false, 4300, LogLevel.Information, "CLOSED")]
    [InlineData(false, true, 4300, LogLevel.Information, "CLOSED")]
    [InlineData(true, true, 4300, LogLevel.Information, "OPEN")]
    [InlineData(true, false, 4301, LogLevel.Warning, "OPEN")]
    public void AnnounceGate_LogsAtTheLevelItsPostureCallsFor(
        bool registrationsOpen, bool isDevelopment, int eventId, LogLevel level, string state)
    {
        var logger = new RecordingLogger<RegistrationGateLogTests>();

        RegistrationGateLog.AnnounceGate(logger, registrationsOpen, isDevelopment);

        var record = logger.Records.ShouldHaveSingleItem();
        record.EventId.Id.ShouldBe(eventId);
        record.Level.ShouldBe(level);
        record.Properties.ShouldContain(p => p.Key == "RegistrationGateState" && Equals(p.Value, state));
    }
}
