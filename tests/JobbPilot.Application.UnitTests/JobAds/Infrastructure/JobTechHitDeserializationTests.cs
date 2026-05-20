using System.Text.Json;
using JobbPilot.Infrastructure.JobSources.Platsbanken;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Infrastructure;

/// <summary>
/// F6 P4 sök-infrastruktur-fix 2026-05-20 — regressions-grind för
/// rotorsaken till filter-bugg. JobTechHit-POCO saknade tidigare
/// Occupation/WorkplaceAddress/OccupationGroup/OccupationField → 51k+
/// rader fick NULL ssyk_concept_id/region_concept_id i raw_payload →
/// ssyk/region-filter alltid 0 träffar. Dessa tester verifierar att
/// klassifikations-fälten både deserialiseras från wire-format OCH
/// round-trippas via JsonSerializer.Serialize (vägen som
/// PlatsbankenJobSource.cs:184 använder för att producera raw_payload).
/// </summary>
public class JobTechHitDeserializationTests
{
    [Fact]
    public void Deserialize_PopulatesOccupationConceptId()
    {
        const string wireJson = """
        {
            "id": "31063032",
            "headline": "Backend Developer",
            "occupation": {
                "concept_id": "fg7B_yov_smw",
                "label": "Systemutvecklare/Programmerare",
                "legacy_ams_taxonomy_id": "2512"
            }
        }
        """;

        var hit = JsonSerializer.Deserialize<JobTechHit>(wireJson);

        hit.ShouldNotBeNull();
        hit.Occupation.ShouldNotBeNull();
        hit.Occupation.ConceptId.ShouldBe("fg7B_yov_smw");
        hit.Occupation.Label.ShouldBe("Systemutvecklare/Programmerare");
        hit.Occupation.LegacyAmsTaxonomyId.ShouldBe("2512");
    }

    [Fact]
    public void Deserialize_PopulatesWorkplaceAddressRegionConceptId()
    {
        const string wireJson = """
        {
            "id": "31063032",
            "workplace_address": {
                "region_concept_id": "CifL_Rzy_Mku",
                "municipality_concept_id": "AvNB_uwa_6n6",
                "country_concept_id": "i46j_HmG_v64",
                "region": "Stockholms län",
                "municipality": "Stockholm",
                "country": "Sverige"
            }
        }
        """;

        var hit = JsonSerializer.Deserialize<JobTechHit>(wireJson);

        hit.ShouldNotBeNull();
        hit.WorkplaceAddress.ShouldNotBeNull();
        hit.WorkplaceAddress.RegionConceptId.ShouldBe("CifL_Rzy_Mku");
        hit.WorkplaceAddress.MunicipalityConceptId.ShouldBe("AvNB_uwa_6n6");
        hit.WorkplaceAddress.CountryConceptId.ShouldBe("i46j_HmG_v64");
        hit.WorkplaceAddress.Region.ShouldBe("Stockholms län");
        hit.WorkplaceAddress.Municipality.ShouldBe("Stockholm");
        hit.WorkplaceAddress.Country.ShouldBe("Sverige");
    }

    [Fact]
    public void Deserialize_PopulatesOccupationGroupAndField()
    {
        const string wireJson = """
        {
            "id": "31063032",
            "occupation_group": {
                "concept_id": "DJh5_yyF_hEM",
                "label": "Mjukvaru- och systemutvecklare m.fl."
            },
            "occupation_field": {
                "concept_id": "apaJ_2ja_LuF",
                "label": "Data/IT"
            }
        }
        """;

        var hit = JsonSerializer.Deserialize<JobTechHit>(wireJson);

        hit.ShouldNotBeNull();
        hit.OccupationGroup.ShouldNotBeNull();
        hit.OccupationGroup.ConceptId.ShouldBe("DJh5_yyF_hEM");
        hit.OccupationField.ShouldNotBeNull();
        hit.OccupationField.ConceptId.ShouldBe("apaJ_2ja_LuF");
    }

    [Fact]
    public void Deserialize_GracefullyHandlesMissingClassification()
    {
        // Defense-in-depth: gamla payloads / partial JobTech-hits utan
        // klassifikation ska ge null-properties, inte krascha.
        const string wireJson = """
        {
            "id": "31063032",
            "headline": "Backend Developer"
        }
        """;

        var hit = JsonSerializer.Deserialize<JobTechHit>(wireJson);

        hit.ShouldNotBeNull();
        hit.Occupation.ShouldBeNull();
        hit.WorkplaceAddress.ShouldBeNull();
        hit.OccupationGroup.ShouldBeNull();
        hit.OccupationField.ShouldBeNull();
    }

    [Fact]
    public void RoundTripSerialize_PreservesClassificationJsonPaths()
    {
        // ROTORSAKS-REGRESSIONS-GRIND: PlatsbankenJobSource.cs:184 gör
        // JsonSerializer.Serialize(hit) → JobTechPayloadSanitizer →
        // raw_payload. Generated columns konsumerar raw_payload->'occupation'
        // ->>'concept_id' + raw_payload->'workplace_address'->>'region_
        // concept_id'. Detta test verifierar att JsonPropertyName-attributen
        // producerar EXAKT samma JSON-paths som generated columns förväntar
        // sig — annars förblir filter-bugg odetekterad.
        var hit = new JobTechHit
        {
            Id = "31063032",
            Occupation = new JobTechOccupation
            {
                ConceptId = "fg7B_yov_smw",
                Label = "Systemutvecklare/Programmerare",
            },
            WorkplaceAddress = new JobTechWorkplaceAddress
            {
                RegionConceptId = "CifL_Rzy_Mku",
                Municipality = "Stockholm",
            },
        };

        var json = JsonSerializer.Serialize(hit);

        // Generated column SQL: raw_payload->'occupation'->>'concept_id'
        json.ShouldContain("\"occupation\":{");
        json.ShouldContain("\"concept_id\":\"fg7B_yov_smw\"");

        // Generated column SQL: raw_payload->'workplace_address'->>'region_concept_id'
        json.ShouldContain("\"workplace_address\":{");
        json.ShouldContain("\"region_concept_id\":\"CifL_Rzy_Mku\"");

        // Roundtrip-bekräftelse — deserialisering av egen output ger samma värden.
        var roundTripped = JsonSerializer.Deserialize<JobTechHit>(json);
        roundTripped.ShouldNotBeNull();
        roundTripped.Occupation!.ConceptId.ShouldBe("fg7B_yov_smw");
        roundTripped.WorkplaceAddress!.RegionConceptId.ShouldBe("CifL_Rzy_Mku");
    }

    [Fact]
    public void RoundTripThroughSanitizer_PreservesClassificationForGeneratedColumns()
    {
        // End-to-end-flödet PlatsbankenJobSource.cs:184-185:
        //   rawJson = JsonSerializer.Serialize(hit);
        //   sanitized = JobTechPayloadSanitizer.SanitizeForStorage(rawJson);
        // Sanitizer allowlist:ar redan occupation/workplace_address/concept_id/
        // region_concept_id (JobTechPayloadSanitizer.cs:42-50) → no-op-passering.
        // Detta är hela vägen från in-memory hit till det som blir raw_payload
        // i job_ads-tabellen — om det går sönder här bryts ssyk/region-filter.
        var hit = new JobTechHit
        {
            Id = "31063032",
            Headline = "Backend Developer",
            Occupation = new JobTechOccupation { ConceptId = "fg7B_yov_smw" },
            WorkplaceAddress = new JobTechWorkplaceAddress { RegionConceptId = "CifL_Rzy_Mku" },
        };

        var rawJson = JsonSerializer.Serialize(hit);
        var sanitized = JobTechPayloadSanitizer.SanitizeForStorage(rawJson);

        // Verifierar att JSON-paths som generated columns konsumerar existerar
        // (sibling-properties kan vara null mellan keys; vi behöver bara veta att
        // occupation/concept_id + workplace_address/region_concept_id nås).
        sanitized.ShouldContain("\"occupation\":{");
        sanitized.ShouldContain("\"concept_id\":\"fg7B_yov_smw\"");
        sanitized.ShouldContain("\"workplace_address\":{");
        sanitized.ShouldContain("\"region_concept_id\":\"CifL_Rzy_Mku\"");
    }
}
