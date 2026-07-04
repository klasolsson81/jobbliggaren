using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #560 (ADR 0091) — unit test for the orchestrator's Enabled=false no-op path (DB-free). The
/// happy-path wiring (filter → upsert → sweep → audit + the timestamp coupling + relative-floor)
/// needs real Postgres and is covered in <c>ScbCompanyRegisterRefresherIntegrationTests</c>.
/// </summary>
public class ScbCompanyRegisterRefresherTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 4, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RefreshAsync_NoOps_WhenDisabled_WithoutTouchingSourceOrScope()
    {
        var source = Substitute.For<IScbCompanyRegisterSource>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);

        var sut = new ScbCompanyRegisterRefresher(
            source, scopeFactory, clock,
            Options.Create(new ScbRegisterOptions { Enabled = false }),
            NullLogger<ScbCompanyRegisterRefresher>.Instance);

        var result = await sut.RefreshAsync(TestContext.Current.CancellationToken);

        result.SweepApplied.ShouldBeFalse();
        result.SweepSkipReason.ShouldBe("disabled");
        result.RowsUpserted.ShouldBe(0);
        result.TotalRowsFetched.ShouldBe(0);
        // No SCB call and no DB scope when disabled → the certificate is never touched.
        source.DidNotReceive().StreamLegalEntitiesAsync(Arg.Any<ScbSyncOutcome>(), Arg.Any<CancellationToken>());
        scopeFactory.DidNotReceive().CreateScope();
    }
}
