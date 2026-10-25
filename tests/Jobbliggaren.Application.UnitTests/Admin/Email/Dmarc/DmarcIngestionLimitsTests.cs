using Jobbliggaren.Application.Admin.Email.Dmarc;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email.Dmarc;

public sealed class DmarcIngestionLimitsTests
{
    // DmarcReportIngestionWorker's [DisableConcurrentExecution] takes ConcurrencyTimeoutSeconds: a second run must wait
    // out a whole first one rather than give up while that one is still inside its deadline.
    [Fact]
    public void ASecondRun_WaitsAtLeastTheRunDeadline()
    {
        TimeSpan.FromSeconds(DmarcIngestionLimits.ConcurrencyTimeoutSeconds)
            .ShouldBeGreaterThanOrEqualTo(DmarcIngestionLimits.RunDeadline);
    }
}
