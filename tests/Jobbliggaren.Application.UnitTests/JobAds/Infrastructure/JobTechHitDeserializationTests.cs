using System.Text.Json;
using Jobbliggaren.Infrastructure.JobSources.Platsbanken;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Infrastructure;

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

    // ── Fas B2 (Platsbanken sök-paritet, Klass 2) ─────────────────────────────
    // Två nya concept-id-dimensioner (CTO + architect 2026-06-08, scope_of_work
    // avvisat): anställningsform (employment_type) + omfattning (working_hours_type).
    // POCO-grinden: om JobTechHit inte deserialiserar dessa keys producerar
    // JsonSerializer.Serialize(hit) en payload UTAN dem → generated columns
    // employment_type_concept_id / worktime_extent_concept_id blir NULL på alla
    // rader (samma rotorsak som F6 P4-filter-buggen). Dessa tester är
    // regressions-grinden CTO flaggade — utan dem förblir NULL-bugg odetekterad.

    [Fact]
    public void Deserialize_PopulatesEmploymentTypeConceptId()
    {
        // employment_type är TOP-LEVEL i JobTech v2-payloaden (speglar
        // occupation_group-mönstret), EJ nested under conditions/employment.
        const string wireJson = """
        {
            "id": "31063032",
            "employment_type": {
                "concept_id": "PFZr_Syz_cUq",
                "label": "Tillsvidareanställning",
                "legacy_ams_taxonomy_id": "1"
            }
        }
        """;

        var hit = JsonSerializer.Deserialize<JobTechHit>(wireJson);

        hit.ShouldNotBeNull();
        hit.EmploymentType.ShouldNotBeNull();
        hit.EmploymentType.ConceptId.ShouldBe("PFZr_Syz_cUq");
        hit.EmploymentType.Label.ShouldBe("Tillsvidareanställning");
        hit.EmploymentType.LegacyAmsTaxonomyId.ShouldBe("1");
    }

    [Fact]
    public void Deserialize_PopulatesWorkingHoursTypeConceptId()
    {
        // NAMNGLAPP-fälla (KÄRNAN i B2): payload-key=working_hours_type, men
        // taxonomi/kolumn heter worktime_extent. POCO-propertyn binder mot
        // payload-keyn via [JsonPropertyName("working_hours_type")]. Om attributet
        // råkar bli "worktime_extent" deserialiserar den aldrig (wire-keyn är
        // working_hours_type) → ConceptId förblir null → kolumnen blir NULL.
        const string wireJson = """
        {
            "id": "31063032",
            "working_hours_type": {
                "concept_id": "6YE1_gAC_R2G",
                "label": "Heltid",
                "legacy_ams_taxonomy_id": "1"
            }
        }
        """;

        var hit = JsonSerializer.Deserialize<JobTechHit>(wireJson);

        hit.ShouldNotBeNull();
        hit.WorkingHoursType.ShouldNotBeNull();
        hit.WorkingHoursType.ConceptId.ShouldBe("6YE1_gAC_R2G");
        hit.WorkingHoursType.Label.ShouldBe("Heltid");
        hit.WorkingHoursType.LegacyAmsTaxonomyId.ShouldBe("1");
    }

    [Fact]
    public void Deserialize_GracefullyHandlesMissingEmploymentAndWorkingHours()
    {
        // B1-erans payload-form (gamla rader): saknar employment_type +
        // working_hours_type → null-properties, ingen krasch. Speglar
        // grace-degraderingen som migration F6P7 förlitar sig på (0-rad-backfill).
        const string wireJson = """
        {
            "id": "31063032",
            "headline": "Backend Developer"
        }
        """;

        var hit = JsonSerializer.Deserialize<JobTechHit>(wireJson);

        hit.ShouldNotBeNull();
        hit.EmploymentType.ShouldBeNull();
        hit.WorkingHoursType.ShouldBeNull();
    }

    [Fact]
    public void RoundTripSerialize_PreservesEmploymentAndWorkingHoursJsonPaths()
    {
        // ROTORSAKS-REGRESSIONS-GRIND för B2: PlatsbankenJobSource.cs:184 gör
        // JsonSerializer.Serialize(hit) → raw_payload. Generated columns
        // konsumerar:
        //   employment_type_concept_id ← raw_payload->'employment_type'->>'concept_id'
        //   worktime_extent_concept_id ← raw_payload->'working_hours_type'->>'concept_id'
        // Verifierar att JsonPropertyName-attributen producerar EXAKT samma
        // wire-keys (working_hours_type, INTE worktime_extent) som SQL förväntar.
        var hit = new JobTechHit
        {
            Id = "31063032",
            EmploymentType = new JobTechEmploymentType { ConceptId = "PFZr_Syz_cUq" },
            WorkingHoursType = new JobTechWorkingHoursType { ConceptId = "6YE1_gAC_R2G" },
        };

        var json = JsonSerializer.Serialize(hit);

        // Generated column SQL: raw_payload->'employment_type'->>'concept_id'
        json.ShouldContain("\"employment_type\":{");
        json.ShouldContain("\"concept_id\":\"PFZr_Syz_cUq\"");

        // Generated column SQL: raw_payload->'working_hours_type'->>'concept_id'
        // (namnglapp — wire-keyn är working_hours_type, kolumnen worktime_extent).
        json.ShouldContain("\"working_hours_type\":{");
        json.ShouldContain("\"concept_id\":\"6YE1_gAC_R2G\"");

        // Negativ kontroll: payloaden får INTE bära worktime_extent som key —
        // det är kolumn-/taxonomi-namnet, aldrig wire-format-keyn.
        json.ShouldNotContain("worktime_extent");

        // Roundtrip-bekräftelse.
        var roundTripped = JsonSerializer.Deserialize<JobTechHit>(json);
        roundTripped.ShouldNotBeNull();
        roundTripped.EmploymentType!.ConceptId.ShouldBe("PFZr_Syz_cUq");
        roundTripped.WorkingHoursType!.ConceptId.ShouldBe("6YE1_gAC_R2G");
    }

    [Fact]
    public void RoundTripThroughSanitizer_PreservesEmploymentAndWorkingHoursForGeneratedColumns()
    {
        // End-to-end PlatsbankenJobSource.cs:184-185. Sanitizer-allowlist:ar
        // redan employment_type + working_hours_type + concept_id (JobTech-
        // PayloadSanitizer.cs:44,54) → no-op-passering. Detta är hela vägen från
        // in-memory hit till raw_payload i job_ads — om en key droppas här blir
        // motsvarande generated column NULL.
        var hit = new JobTechHit
        {
            Id = "31063032",
            Headline = "Backend Developer",
            EmploymentType = new JobTechEmploymentType { ConceptId = "PFZr_Syz_cUq" },
            WorkingHoursType = new JobTechWorkingHoursType { ConceptId = "6YE1_gAC_R2G" },
        };

        var rawJson = JsonSerializer.Serialize(hit);
        var sanitized = JobTechPayloadSanitizer.SanitizeForStorage(rawJson);

        sanitized.ShouldContain("\"employment_type\":{");
        sanitized.ShouldContain("\"concept_id\":\"PFZr_Syz_cUq\"");
        sanitized.ShouldContain("\"working_hours_type\":{");
        sanitized.ShouldContain("\"concept_id\":\"6YE1_gAC_R2G\"");
        sanitized.ShouldNotContain("worktime_extent");
    }

    // ── Fas 4 STEG 4b (F4-4b — employer requirement extraction) ───────────────
    // POCO-grinden för must_have/nice_to_have. Om JobTechHit inte deserialiserar
    // dessa keys producerar JsonSerializer.Serialize(hit) en payload UTAN dem →
    // (a) ACL-mappningen ser inga requirements, och (b) re-ingest-predikatet
    // (!JsonExists(raw_payload,'must_have')) flippar aldrig till skip (samma
    // POCO-rotorsaks-mönster som B2 employment_type/working_hours_type).
    // Sanitizer-allowlist:ar redan must_have/nice_to_have/skills/.../weight
    // (JobTechPayloadSanitizer.cs:58-60) → no-op-passering.

    [Fact]
    public void Deserialize_PopulatesMustHaveAndNiceToHaveSkills()
    {
        // must_have/nice_to_have är TOP-LEVEL objekt med fem sub-arrayer; varje
        // element delar shape {weight, concept_id, label, legacy_ams_taxonomy_id}.
        // weight kan vara null (live-verifierat i sample) → POCO-property int?.
        const string wireJson = """
        {
            "id": "31063032",
            "must_have": {
                "skills": [
                    { "weight": 10, "concept_id": "Rq01_must_aaa", "label": "C#", "legacy_ams_taxonomy_id": "1" },
                    { "weight": null, "concept_id": "Rq02_must_bbb", "label": "Java", "legacy_ams_taxonomy_id": "2" }
                ]
            },
            "nice_to_have": {
                "skills": [
                    { "weight": 5, "concept_id": "Rq03_nice_ccc", "label": "Azure", "legacy_ams_taxonomy_id": "3" }
                ]
            }
        }
        """;

        var hit = JsonSerializer.Deserialize<JobTechHit>(wireJson);

        hit.ShouldNotBeNull();
        hit.MustHave.ShouldNotBeNull();
        hit.MustHave.Skills.ShouldNotBeNull();
        hit.MustHave.Skills.Count.ShouldBe(2);
        hit.MustHave.Skills[0].ConceptId.ShouldBe("Rq01_must_aaa");
        hit.MustHave.Skills[0].Label.ShouldBe("C#");
        hit.MustHave.Skills[0].Weight.ShouldBe(10);
        // weight: null → POCO Weight is null (the ACL maps it to the 0.0 floor later).
        hit.MustHave.Skills[1].Weight.ShouldBeNull();

        hit.NiceToHave.ShouldNotBeNull();
        hit.NiceToHave.Skills.ShouldNotBeNull();
        hit.NiceToHave.Skills.ShouldHaveSingleItem().ConceptId.ShouldBe("Rq03_nice_ccc");
    }

    [Fact]
    public void Deserialize_GracefullyHandlesMissingMustHaveAndNiceToHave()
    {
        // Pre-F4-4b payload-form (alla importerade rader): saknar must_have +
        // nice_to_have → null-properties, ingen krasch. Detta är raderna
        // re-ingest-backfillen plockar (raw_payload saknar must_have).
        const string wireJson = """
        {
            "id": "31063032",
            "headline": "Backend Developer"
        }
        """;

        var hit = JsonSerializer.Deserialize<JobTechHit>(wireJson);

        hit.ShouldNotBeNull();
        hit.MustHave.ShouldBeNull();
        hit.NiceToHave.ShouldBeNull();
    }

    [Fact]
    public void RoundTripSerialize_PreservesMustHaveJsonKey()
    {
        // ROTORSAKS-REGRESSIONS-GRIND för F4-4b: PlatsbankenJobSource gör
        // JsonSerializer.Serialize(hit) → raw_payload. Re-ingest-predikatet
        // !JsonExists(raw_payload,'must_have') förlitar sig på att JsonPropertyName-
        // attributen producerar EXAKT wire-keyn "must_have". Verifierar att en hit med
        // must_have re-emitterar nyckeln (annars förblir raden "saknar must_have" för
        // alltid → re-hämtas varje körning).
        var hit = new JobTechHit
        {
            Id = "31063032",
            MustHave = new JobTechRequirements
            {
                Skills = [new JobTechRequirementConcept { ConceptId = "Rq01_must_aaa", Label = "C#", Weight = 10 }],
            },
            NiceToHave = new JobTechRequirements
            {
                Skills = [new JobTechRequirementConcept { ConceptId = "Rq03_nice_ccc", Label = "Azure", Weight = 5 }],
            },
        };

        var json = JsonSerializer.Serialize(hit);

        json.ShouldContain("\"must_have\":{");
        json.ShouldContain("\"nice_to_have\":{");
        json.ShouldContain("\"skills\":[");
        json.ShouldContain("\"concept_id\":\"Rq01_must_aaa\"");

        // Roundtrip-bekräftelse.
        var roundTripped = JsonSerializer.Deserialize<JobTechHit>(json);
        roundTripped.ShouldNotBeNull();
        roundTripped.MustHave!.Skills.ShouldHaveSingleItem().ConceptId.ShouldBe("Rq01_must_aaa");
    }

    [Fact]
    public void RoundTripThroughSanitizer_PreservesMustHaveForReIngestPredicate()
    {
        // End-to-end PlatsbankenJobSource → JsonSerializer.Serialize(hit) →
        // JobTechPayloadSanitizer. Sanitizer-allowlist:ar redan must_have/nice_to_have/
        // skills/weight (JobTechPayloadSanitizer.cs:58-60) → must_have-nyckeln överlever
        // till raw_payload. Detta är hela vägen som gör att re-ingest-predikatet
        // (!JsonExists(raw_payload,'must_have')) korrekt skippar en re-ingestad rad.
        var hit = new JobTechHit
        {
            Id = "31063032",
            Headline = "Backend Developer",
            MustHave = new JobTechRequirements
            {
                Skills = [new JobTechRequirementConcept { ConceptId = "Rq01_must_aaa", Label = "C#", Weight = 10 }],
            },
        };

        var rawJson = JsonSerializer.Serialize(hit);
        var sanitized = JobTechPayloadSanitizer.SanitizeForStorage(rawJson);

        sanitized.ShouldContain("\"must_have\":{");
        sanitized.ShouldContain("\"skills\":[");
        sanitized.ShouldContain("\"concept_id\":\"Rq01_must_aaa\"");
    }
}
