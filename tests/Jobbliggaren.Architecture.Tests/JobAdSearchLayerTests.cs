using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// ADR 0062 anti-regression — FTS-hybrid-sökskiftet respekterar Clean Arch.
/// Sök-kompositionen flyttades Application→Infrastructure eftersom PostgreSQL
/// full-text-search-LINQ (websearch_to_tsquery / @@ / ts_rank) fysiskt ligger
/// i Npgsql.EntityFrameworkCore.PostgreSQL. <c>IJobAdSearchQuery</c>-porten är
/// Application (speglar IJobSource / ITaxonomyReadModel); impl:en är internal
/// i Infrastructure. Det negativa Npgsql-förbudet i Application täcks av
/// <see cref="TaxonomyAclLayerTests.Application_should_not_depend_on_Npgsql_or_EF_relational"/>.
/// </summary>
public class JobAdSearchLayerTests
{
    [Fact]
    public void IJobAdSearchQuery_is_in_Application_layer()
    {
        // ADR 0062 — porten är Application-abstraktion, inte Infra-detalj.
        var port = typeof(Jobbliggaren.Application.JobAds.Abstractions.IJobAdSearchQuery);
        port.Assembly.ShouldBe(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void JobAdSearchCriteria_is_in_Application_layer()
    {
        // Criteria-record är Application-kontrakt (handlers mappar till den).
        var criteria = typeof(Jobbliggaren.Application.JobAds.Abstractions.JobAdSearchCriteria);
        criteria.Assembly.ShouldBe(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void JobAdSearchQuery_impl_is_internal_to_Infrastructure()
    {
        // FTS-kompositionen (Npgsql-bunden) får inte refereras från
        // Application/Api/Worker — endast porten är publik (ACL-isolation,
        // paritet med TaxonomyReadModel / JobTech-wire-typer).
        var impl = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly
            .GetTypes()
            .Single(t => t.Name == "JobAdSearchQuery");

        impl.IsPublic.ShouldBeFalse(
            "JobAdSearchQuery-impl:en ska vara internal — Npgsql-FTS-koden får " +
            "inte läcka utanför Infrastructure.");
    }
}
