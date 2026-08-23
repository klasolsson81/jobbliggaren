using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1463 — the radius premise under <c>RecruiterErasureMatchQuery</c>'s constructor.
/// </summary>
/// <remarks>
/// That port raises its own <c>CommandTimeout</c> to a reviewed ceiling in its CONSTRUCTOR, which
/// is right only while the premise its comment states is true: the port is <c>AddScoped</c> and
/// injected by the erasure handler alone, so the raised ceiling reaches exactly one Art. 17
/// request. Nothing enforced that. Inject the port into a second, hot-path handler tomorrow and
/// every command in THAT request silently inherits a multi-minute ceiling — precisely the "wider
/// and UNDECLARED radius" the comment argues against, introduced by the argument's own blind spot.
/// <para>
/// This pins the premise rather than the prose (<c>test-writer</c>, 2026-08-23). It is deliberately
/// a constructor-injection scan and not a mention scan: <c>ErasureCascadeRegistry</c> names the
/// port's METHODS via <c>nameof</c> and must keep doing so.
/// </para>
/// </remarks>
public class ErasurePortInjectionRadiusTests
{
    [Fact]
    public void ErasurePortIsInjectedOnlyByTheErasureHandler()
    {
        var consumers = typeof(Jobbliggaren.Application.AssemblyMarker).Assembly
            .GetTypes()
            .Where(t => t.GetConstructors()
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
