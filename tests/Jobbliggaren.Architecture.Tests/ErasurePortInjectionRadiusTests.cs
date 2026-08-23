using System.Reflection;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1463 — the radius premise under <c>RecruiterErasureMatchQuery</c>'s constructor.
/// </summary>
/// <remarks>
/// That port raises its own <c>CommandTimeout</c> to a reviewed ceiling in its CONSTRUCTOR, which
/// is right only while the raised ceiling reaches exactly one Art. 17 request. Nothing enforced
/// that. Inject the port into a second, hot-path handler tomorrow and every command in THAT request
/// silently inherits a multi-minute ceiling — precisely the "wider and UNDECLARED radius" the
/// comment argues against, introduced by the argument's own blind spot.
/// <para>
/// This pins the premise rather than the prose (<c>test-writer</c>, 2026-08-23). It is deliberately
/// a constructor-injection scan and not a mention scan: <c>ErasureCascadeRegistry</c> names the
/// port's METHODS via <c>nameof</c> and must keep doing so. A minimal-API delegate parameter is a
/// second injection form that no constructor scan can see; the method name is scoped to what is
/// actually covered rather than claiming more.
/// </para>
/// </remarks>
public class ErasurePortInjectionRadiusTests
{
    /// <summary>
    /// Every assembly that can resolve the port, not just the one that declares it.
    /// <see cref="IRecruiterErasureMatchQuery"/> is <c>public</c> and registered in
    /// <c>AddPersistence</c>, which <c>Jobbliggaren.Worker</c>'s composition root calls too — so a
    /// Hangfire job constructor could take it and hand that whole job scope a multi-minute ceiling.
    /// Scanning Application alone would pass green forever while missing exactly that
    /// (<c>dotnet-architect</c>, 2026-08-23). Same shape as
    /// <c>OrgNrRecordLoggingGuardTests.OwnedAssemblies</c>; Domain is excluded because it may not
    /// reference an Application port at all (§2.1).
    /// </summary>
    private static readonly Assembly[] OwnedAssemblies =
    [
        typeof(Jobbliggaren.Application.AssemblyMarker).Assembly,
        typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly,
        typeof(Jobbliggaren.Api.Endpoints.AdminJobAdsEndpoints).Assembly,
        typeof(Jobbliggaren.Worker.Auditing.WorkerSystemUser).Assembly,
    ];

    [Fact]
    public void ErasurePortIsConstructorInjectedOnlyByTheErasureHandler()
    {
        var consumers = OwnedAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(c => c.GetParameters()
                    .Any(p => p.ParameterType == typeof(IRecruiterErasureMatchQuery))))
            .Select(t => t.FullName!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        consumers.ShouldBe([typeof(EraseRecruiterAdsCommandHandler).FullName!],
            "RecruiterErasureMatchQuery raises the command ceiling in its constructor, and that is "
            + "only defensible while the erasure handler is its sole consumer — the scoped "
            + "AppDbContext it mutates is shared with everything else in the same request. A second "
            + "consumer means a second request shape silently running under a multi-minute ceiling; "
            + "re-derive the placement before adding one (#1463). Found: "
            + string.Join(", ", consumers));
    }
}
