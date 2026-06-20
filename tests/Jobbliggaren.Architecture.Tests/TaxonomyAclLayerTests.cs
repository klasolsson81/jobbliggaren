using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// ADR 0043 anti-regression — taxonomi-ACL respekterar Clean Arch:
/// ITaxonomyReadModel-porten är Application (speglar IJobSource); snapshot-
/// entitet/seeder/Npgsql stannar i Infrastructure; Domain RÖRS INTE (ingen
/// ny Domain-typ — SearchCriteria oförändrad, ADR 0043 Beslut E).
/// </summary>
public class TaxonomyAclLayerTests
{
    [Fact]
    public void ITaxonomyReadModel_is_in_Application_layer()
    {
        // ADR 0043 §2 — porten är Application-abstraktion, inte Infra-detalj.
        var port = typeof(Jobbliggaren.Application.JobAds.Abstractions.ITaxonomyReadModel);
        port.Assembly.ShouldBe(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void Application_should_not_depend_on_Npgsql_or_EF_relational()
    {
        // Taxonomi-ACL får inte läcka in databasprovider i Application.
        // ADR 0062 — vakthund även mot FTS-typer: NpgsqlTsVector ligger i
        // NpgsqlTypes-namespace (Npgsql-assemblyn). "Npgsql" prefix-matchar
        // redan NpgsqlTypes.*, men "NpgsqlTypes" listas explicit för
        // självdokumentation (FTS-LINQ får bara förekomma i Infrastructure-
        // impl:en JobAdSearchQuery, ej i Application).
        var result = Types.InAssembly(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Npgsql",
                "NpgsqlTypes",
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                "Microsoft.EntityFrameworkCore.Relational")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Application läcker mot Npgsql/EF-relational: " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Taxonomy_snapshot_types_are_internal_to_Infrastructure()
    {
        // TaxonomyConcept / TaxonomyConceptKind / seeder / file-form / meta
        // ska vara internal — EF-entitet + wire-form får inte refereras från
        // Application/Api/Worker (ACL-isolation, Evans kap. 14).
        var infrastructureAsm = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

        var publicTaxonomyTypes = infrastructureAsm.GetTypes()
            .Where(t => t.Namespace == "Jobbliggaren.Infrastructure.Taxonomy"
                        && (t.IsPublic || (t.IsNested && t.IsNestedPublic)))
            .Select(t => t.FullName)
            .ToList();

        publicTaxonomyTypes.ShouldBeEmpty(
            "Taxonomi-snapshot-typer ska vara internal (ACL-isolation, ADR 0043). " +
            $"Public: {string.Join(", ", publicTaxonomyTypes!)}");
    }

    [Fact]
    public void Domain_should_not_contain_any_Taxonomy_type()
    {
        // ADR 0043 — taxonomi är INTE Jobbliggarens ubiquitous language. Ingen
        // ny Domain-typ skapas (SearchCriteria orörd). Vakthund mot framtida
        // drift där någon råkar lägga en Taxonomy*-typ i Domain.
        var domainAsm = typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly;

        var taxonomyDomainTypes = domainAsm.GetTypes()
            .Where(t => t.Name.Contains("Taxonomy", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        taxonomyDomainTypes.ShouldBeEmpty(
            "Domain ska inte innehålla Taxonomy-typer (ACL utanför Domain, " +
            $"Evans kap. 14). Hittade: {string.Join(", ", taxonomyDomainTypes!)}");
    }

    [Fact]
    public void Only_query_handlers_consume_ITaxonomyReadModel_in_Application()
    {
        // Konsumentlista: porten ska bara injiceras i query-handlare som
        // gör reverse-lookup/picker-träd (tunna ACL-konsumenter) — inte
        // spridas in i command-/write-use-cases.
        //
        // ADR 0043-utvidgning (CTO 2026-05-17, Approach A):
        // ListSavedSearchesQueryHandler är en LEGITIM tredje konsument —
        // den berikar /sokningar-listan med namn via samma
        // ResolveLabelsAsync-port (Application-port i Application-handler,
        // samma mönster som handlern redan har mot IAppDbContext; ingen
        // Clean Arch-brott — porten är Application-ägd, CLAUDE.md §2.1).
        // Allowlisten utökas ADDITIVT — inte öppnas upp.
        var port = typeof(Jobbliggaren.Application.JobAds.Abstractions.ITaxonomyReadModel);
        var appAsm = typeof(Jobbliggaren.Application.AssemblyMarker).Assembly;

        var consumers = appAsm.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetConstructors()
                .Any(c => c.GetParameters().Any(p => p.ParameterType == port)))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        // NB: jämförelsen är ordnings-känslig och `consumers` är OrderBy(namn) →
        // listan nedan MÅSTE vara alfabetisk. Allowlisten utökas ADDITIVT.
        consumers.ShouldBe(
        [
            // F4-16 (2026-06-20) — sjätte legitim konsument: jobbmodalens match-detalj
            // resolvar SSYK/region/anställningsform-membership-evidensens RÅA concept-id
            // till människo-labels (ADR 0043 ACL — ett concept-id når aldrig användaren;
            // CLAUDE.md §5 — ett opakt id är motsatsen till förklarbart). Tunn query-
            // handler, samma ResolveLabelsAsync-mönster; ingen Clean Arch-brott (§2.1).
            // (Sorterar före GetTaxonomyTree: "GetJ" < "GetT".)
            nameof(Jobbliggaren.Application.Matching.Queries.GetJobAdMatchDetail.GetJobAdMatchDetailQueryHandler),
            nameof(Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree.GetTaxonomyTreeQueryHandler),
            // ADR 0060 (2026-05-20) — fjärde legitim konsument: RecentJobSearches-
            // listans label-berikning, identiskt mönster med SavedSearch-listan.
            nameof(Jobbliggaren.Application.RecentJobSearches.Queries.ListRecentSearches.ListRecentSearchesQueryHandler),
            nameof(Jobbliggaren.Application.SavedSearches.Queries.ListSavedSearches.ListSavedSearchesQueryHandler),
            nameof(Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree.ResolveTaxonomyLabelsQueryHandler),
            // ADR 0067 Beslut 5a (2026-06-10) — femte legitim konsument: utökad
            // typeahead-suggest unionar taxonomi-snapshot-prefix (SuggestByPrefixAsync)
            // med job_ads-titel-prefix. Tunn query-handler, samma Application-port-
            // mönster; ingen Clean Arch-brott (porten är Application-ägd, §2.1).
            nameof(Jobbliggaren.Application.JobAds.Queries.SuggestJobAdTerms.SuggestJobAdTermsQueryHandler),
        ]);
    }
}
