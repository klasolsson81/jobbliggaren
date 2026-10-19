using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// ADR 0175 D2, D11 (#1663 PR 2) — the Branschbevakning scan's port stays Worker-facing, and the Worker host makes the
/// registration the Worker integration test resolves the job through.
/// </summary>
public class CompanyWatchCriterionScanWiringTests
{
    [Fact]
    public void Only_the_scan_job_its_implementation_and_the_composition_root_reference_the_scan_port()
    {
        // A new consumer of the port (an Api handler, say) is a new reader of the member set with no breadth or
        // grade review; it fails here until it has one.
        var consumers = ProductionTypeSweep.OwnersReferencing(typeof(ICompanyWatchCriterionAdScan).FullName!);

        consumers.ShouldBe(
            [
                "Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchCriterionScan.CompanyWatchCriterionScanJob",
                "Jobbliggaren.Infrastructure.CompanyRegister.CompanyWatchBrowseQuery",
                "Jobbliggaren.Infrastructure.DependencyInjection",
            ],
            ignoreOrder: true,
            $"Types found: {string.Join(", ", consumers)}");
    }

    [Fact]
    public void The_Worker_host_calls_the_registration_its_integration_test_resolves_through()
    {
        // WorkerTestFixture builds no host, so it can only show that AddCompanyWatchCriterionScan registers a
        // resolvable wrapper; this is what ties that registration to the host.
        var program = File.ReadAllText(Path.Combine(RepoSourceText.Root(), "src", "Jobbliggaren.Worker", "Program.cs"));

        program.ShouldContain("builder.Services.AddCompanyWatchCriterionScan(builder.Configuration);");
    }
}
