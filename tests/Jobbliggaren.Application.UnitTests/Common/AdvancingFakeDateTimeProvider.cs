using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.UnitTests.Common;

/// <summary>
/// Scripted, advancing fake clock — each <see cref="UtcNow"/> read returns
/// <paramref name="start"/> on the FIRST read, then advances by
/// <paramref name="stepPerRead"/> on every subsequent read.
///
/// <para>
/// A plain frozen <see cref="FakeDateTimeProvider"/> makes any
/// <c>(completedAt - startedAt)</c> duration compute to exactly zero, because
/// both reads return the same instant. That is the wrong fixture for testing
/// duration-derived code (e.g. <c>IngestionThroughputReporter</c> callers) —
/// it would silently mask the very <c>durationSec == 0</c> edge case those
/// call sites need to be exercised AROUND, not accidentally always hit (CTO
/// bind #754 Q3(ii) — "the throughput tests need an advancing/scripted fake
/// clock, not a frozen one").
/// </para>
/// </summary>
internal sealed class AdvancingFakeDateTimeProvider(DateTimeOffset start, TimeSpan stepPerRead) : IDateTimeProvider
{
    private DateTimeOffset _current = start;
    private bool _firstRead = true;

    public DateTimeOffset UtcNow
    {
        get
        {
            if (_firstRead)
            {
                _firstRead = false;
                return _current;
            }

            _current += stepPerRead;
            return _current;
        }
    }
}
