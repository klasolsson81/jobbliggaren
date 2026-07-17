using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Fas 4 STEG 4 (F4-4, ADR 0071/0074 Path C) anti-regression — the deterministic
/// per-job-ad keyword/skill extractor respects Clean Architecture. Mirrors
/// <see cref="OccupationCodeDeriverLayerTests"/> (F4-3) with ONE deliberate
/// divergence (dotnet-architect Variant A, ADR 0075):
/// <list type="bullet">
/// <item>The <b>port</b> <c>IJobAdKeywordExtractor</c> + the input DTO
/// (<c>JobAdExtractionInput</c>) are Application abstractions — exactly like F4-3's
/// <c>IOccupationCodeDeriver</c>.</item>
/// <item>The VALUE OBJECT + enums (<c>ExtractedTerms</c>, <c>ExtractedTerm</c>,
/// <c>ExtractedTermKind</c>, <c>ExtractedTermSource</c>) live in <b>Domain</b>, NOT
/// Application — because they are <b>persisted aggregate state</b> (jsonb on
/// <c>job_ads</c>, a nullable property on the <c>JobAd</c> aggregate root), unlike
/// F4-3's never-persisted <c>OccupationCandidate</c> read projection. Same layering
/// rule ("the concept lives where its lifecycle lives"), different answer for a
/// different lifecycle (ADR 0075).</item>
/// <item>The impl <c>JobAdKeywordExtractor</c> + the loader helper
/// <c>JobAdSkillTaxonomyLoader</c> are <c>internal sealed</c>/<c>internal static</c>
/// in Infrastructure.</item>
/// </list>
/// The extractor consumes the F4-2 local NLP tier (Snowball via
/// <c>ITextAnalyzer</c>/<c>IStemmer</c>) + the committed skill-taxonomy asset, but
/// Application/Domain MUST NOT gain any NLP/Npgsql/EF dependency through it (the
/// port surface stays BCL-only, exactly like ITaxonomyReadModel / the F4-2 NLP
/// ports). Domain stays NLP- and Application-free.
///
/// RED until the F4-4 production types ship (they do now).
/// </summary>
public class JobAdKeywordExtractorLayerTests
{
    // The impl + loader namespace (Infrastructure). Probed by name so the RED
    // state requires the types to exist AND be non-public.
    private const string ExtractorNamespace = "Jobbliggaren.Infrastructure.Taxonomy";

    // ===============================================================
    // 1. Port + transport DTOs live in the Application assembly
    // ===============================================================

    [Fact]
    public void IJobAdKeywordExtractor_is_in_Application_layer()
    {
        var port = typeof(Jobbliggaren.Application.JobAds.Abstractions.IJobAdKeywordExtractor);
        port.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void JobAdExtractionInput_is_in_Application_layer()
    {
        // The extractor's INPUT is an Application-layer transport record (public ad
        // text only — no PII, no domain identity). Mirrors F4-3's port DTOs.
        var input = typeof(Jobbliggaren.Application.JobAds.Abstractions.JobAdExtractionInput);
        input.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    // ===============================================================
    // 2. THE VARIANT A DIVERGENCE — the VO + enums live in Domain,
    //    NOT Application (persisted aggregate state, ADR 0075)
    // ===============================================================

    [Fact]
    public void ExtractedTerms_value_object_is_in_Domain_layer()
    {
        // dotnet-architect Variant A / ADR 0075: UNLIKE F4-3's OccupationCandidate
        // (a never-persisted read projection living beside its port in Application),
        // ExtractedTerms is persisted aggregate state — a jsonb VO on a nullable
        // JobAd property. The concept lives where its lifecycle lives: Domain.
        var vo = typeof(Jobbliggaren.Domain.JobAds.ExtractedTerms);
        vo.Assembly.ShouldBe(
            typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly,
            "ExtractedTerms är persisterad aggregat-state (jsonb-VO på JobAd) → " +
            "den hör hemma i Domain, INTE Application (dotnet-architect Variant A, " +
            "ADR 0075). Skillnaden mot F4-3:s OccupationCandidate (aldrig persisterad " +
            "read-projektion i Application) är medveten: samma lager-regel, olika svar " +
            "för en annan livscykel.");
    }

    [Fact]
    public void ExtractedTerm_record_is_in_Domain_layer()
    {
        var term = typeof(Jobbliggaren.Domain.JobAds.ExtractedTerm);
        term.Assembly.ShouldBe(
            typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly,
            "ExtractedTerm är en del av den persisterade jsonb-VO:n → Domain (ADR 0075).");
    }

    [Fact]
    public void ExtractedTermKind_enum_is_in_Domain_layer()
    {
        var kind = typeof(Jobbliggaren.Domain.JobAds.ExtractedTermKind);
        kind.Assembly.ShouldBe(
            typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly,
            "ExtractedTermKind är jsonb-state-enum (stabila enum-värden = jsonb-kontrakt; " +
            "sort-prioritet via ExtractedTerms.SortRank, F4-4b) → Domain (ADR 0075).");
    }

    [Fact]
    public void ExtractedTermSource_enum_is_in_Domain_layer()
    {
        var source = typeof(Jobbliggaren.Domain.JobAds.ExtractedTermSource);
        source.Assembly.ShouldBe(
            typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly,
            "ExtractedTermSource är jsonb-state (cited evidence-provenance) → Domain (ADR 0075).");
    }

    // ===============================================================
    // 3. The port does NOT live in Infrastructure (Application abstraction)
    // ===============================================================

    [Fact]
    public void IJobAdKeywordExtractor_is_not_in_Infrastructure_assembly()
    {
        var port = typeof(Jobbliggaren.Application.JobAds.Abstractions.IJobAdKeywordExtractor);
        port.Assembly.ShouldNotBe(
            typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly);
    }

    // ===============================================================
    // 4. The impl (+ the skill-taxonomy loader helper) are internal in
    //    Infrastructure — proven BY NAME so RED requires the types to exist
    //    and be non-public (parity TaxonomyReadModel / OccupationCodeDeriver).
    // ===============================================================

    [Fact]
    public void JobAdKeywordExtractor_and_loader_helpers_are_internal_to_Infrastructure()
    {
        var infrastructureAsm = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

        var extractorTypes = infrastructureAsm.GetTypes()
            .Where(t => t.Namespace == ExtractorNamespace
                        && (t.Name.Contains("JobAdKeywordExtractor", StringComparison.Ordinal)
                            || t.Name.Contains("JobAdSkillTaxonomy", StringComparison.Ordinal)))
            .ToList();

        // The extractor type must exist (else RED — production not written yet).
        extractorTypes.ShouldContain(
            t => t.Name.Contains("JobAdKeywordExtractor", StringComparison.Ordinal),
            "JobAdKeywordExtractor saknas i Jobbliggaren.Infrastructure.Taxonomy " +
            "(F4-4 production-impl ej skriven än — väntad RED).");

        // The skill-taxonomy loader helper must exist and be non-public.
        extractorTypes.ShouldContain(
            t => t.Name.Contains("JobAdSkillTaxonomy", StringComparison.Ordinal),
            "JobAdSkillTaxonomyLoader saknas i Jobbliggaren.Infrastructure.Taxonomy " +
            "(F4-4 embedded-asset-loader ej skriven än — väntad RED).");

        var publicExtractorTypes = extractorTypes
            .Where(t => t.IsPublic || (t.IsNested && t.IsNestedPublic))
            .Select(t => t.FullName)
            .ToList();

        publicExtractorTypes.ShouldBeEmpty(
            "JobAdKeywordExtractor + JobAdSkillTaxonomyLoader ska vara internal " +
            "(ACL-isolation, ADR 0043; paritet TaxonomyReadModel/OccupationCodeDeriver). " +
            $"Public: {string.Join(", ", publicExtractorTypes!)}");
    }

    // ===============================================================
    // 5. Application MUST NOT depend on the NLP libraries through the port
    //    (Snowball/WeCantSpell live only in Infrastructure)
    // ===============================================================

    [Fact]
    public void Application_should_not_depend_on_NLP_libraries()
    {
        // Surfacing the skill index + the stemmed extraction behind
        // IJobAdKeywordExtractor must NOT drag Snowball/WeCantSpell across the
        // Application boundary — the extractor impl (in Infrastructure) is the only
        // thing that touches the Snowball-bound ITextAnalyzer/IStemmer impls.
        var result = Types.InAssembly(
                typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Snowball", "WeCantSpell.Hunspell", "WeCantSpell")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Application läcker mot NLP-bibliotek (Snowball/WeCantSpell) — F4-4 " +
            "extractor-porten ska vara BCL-only: " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    // ===============================================================
    // 6. Application MUST NOT depend on Npgsql/EF through the port
    // ===============================================================

    [Fact]
    public void Application_should_not_depend_on_EfCore_or_Npgsql()
    {
        // Paritet ADR 0062 + CLAUDE.md §2.1. The jsonb persistence (converter,
        // generated extracted_lexemes column, GIN) lives in Infrastructure; no
        // EF/Npgsql type may cross into Application via the extractor port.
        var result = Types.InAssembly(
                typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Npgsql",
                "NpgsqlTypes",
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                "Microsoft.EntityFrameworkCore.Relational")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Application läcker mot Npgsql/EF-relational (Clean Arch, CLAUDE.md §2.1): " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    // ===============================================================
    // 7. Domain MUST NOT depend on Application / the NLP libraries — even
    //    though the ExtractedTerms VO now LIVES in Domain (Variant A)
    // ===============================================================

    [Fact]
    public void Domain_should_not_depend_on_Application_or_NLP_libraries()
    {
        // The whole point of Variant A is that the VO can live in Domain WITHOUT
        // dragging the extraction machinery in: the extractor (Application port +
        // Infrastructure impl) builds the VO, the VO knows nothing about how it was
        // built. Domain stays NLP-free and Application-free (incl. the personnummer
        // guard in Domain/Privacy).
        var result = Types.InAssembly(
                typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Jobbliggaren.Application", "Snowball", "WeCantSpell.Hunspell", "WeCantSpell")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Domain läcker mot Application/NLP-bibliotek: " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void ExtractedTerms_does_not_reference_the_NLP_port_types()
    {
        // Sharper than the assembly-level rule above: the VO itself must not
        // reference ITextAnalyzer / TextLanguage / IStemmer. The extraction PIPELINE
        // (which lexemizes via the NLP tier) is an Application/Infrastructure
        // concern; the persisted VO carries only canonical lexemes/labels. If the VO
        // referenced TextLanguage it would couple persisted state to the NLP tier.
        var result = Types.InAssembly(
                typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly)
            .That()
            .HaveNameStartingWith("ExtractedTerm")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Jobbliggaren.Application.Common.Abstractions.TextAnalysis")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "ExtractedTerms/ExtractedTerm refererar NLP-port-typer (ITextAnalyzer/" +
            "TextLanguage/IStemmer) — den persisterade VO:n ska vara fri från NLP-tier-" +
            $"koppling (ADR 0075): {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
