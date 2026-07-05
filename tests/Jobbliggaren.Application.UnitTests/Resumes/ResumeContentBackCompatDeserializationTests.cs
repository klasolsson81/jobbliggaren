using System.Text.Json;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Security;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes;

/// <summary>
/// Fas 4b AppCopy superset (#651, ADR 0093 D1 / LRM ADR 0095) — Form B expand read-tolerance
/// (ADR 0049 Beslut 5). A legacy <c>content_enc</c> blob written before the superset existed
/// carries only the four original section keys (no <c>languages</c>/<c>skillGroups</c>/
/// <c>sections</c>). Deserialising it through the PRODUCTION serialization SPOT
/// (<see cref="EncryptedFieldRegistry.ContentJsonOptions"/> — referenced directly, not mirrored,
/// so there is zero drift) must yield a <see cref="ResumeContent"/> whose new collections are
/// EMPTY (never null, never a throw) and whose old fields are intact. This is a FAST unit test
/// (pure STJ, no Postgres); the encrypted-at-rest round-trip is pinned separately against real
/// Postgres in <c>ResumeContentEncryptionTests</c>.
/// </summary>
public class ResumeContentBackCompatDeserializationTests
{
    // A pre-superset payload: only personalInfo/experiences/educations/skills/summary keys,
    // camelCase (the production naming policy). Deliberately omits the three superset keys.
    private const string LegacyJson =
        """
        {
          "personalInfo": {
            "fullName": "Anna Andersson",
            "email": "anna@example.com",
            "phone": "0701234567",
            "location": "Stockholm"
          },
          "experiences": [
            {
              "company": "Beta AB",
              "role": "Backend-utvecklare",
              "startDate": "2021-01-01",
              "endDate": "2024-06-30",
              "description": "Byggde betaltjänster."
            }
          ],
          "educations": [
            {
              "institution": "KTH",
              "degree": "Civilingenjör",
              "startDate": "2013-09-01",
              "endDate": "2018-06-01"
            }
          ],
          "skills": [
            { "name": "C#", "yearsExperience": 8 },
            { "name": "PostgreSQL", "yearsExperience": null }
          ],
          "summary": "Erfaren backend-utvecklare."
        }
        """;

    [Fact]
    public void Deserialize_LegacyContentWithoutSupersetKeys_YieldsEmptySupersetCollections()
    {
        var content = JsonSerializer.Deserialize<ResumeContent>(
            LegacyJson, EncryptedFieldRegistry.ContentJsonOptions);

        content.ShouldNotBeNull();
        // Read-tolerance: absent keys → empty collections, not null, no throw.
        content.Languages.ShouldNotBeNull();
        content.Languages.ShouldBeEmpty();
        content.SkillGroups.ShouldNotBeNull();
        content.SkillGroups.ShouldBeEmpty();
        content.Sections.ShouldNotBeNull();
        content.Sections.ShouldBeEmpty();
    }

    [Fact]
    public void Deserialize_LegacyContentWithoutSupersetKeys_KeepsOriginalFieldsIntact()
    {
        var content = JsonSerializer.Deserialize<ResumeContent>(
            LegacyJson, EncryptedFieldRegistry.ContentJsonOptions);

        content.ShouldNotBeNull();
        content.PersonalInfo.FullName.ShouldBe("Anna Andersson");
        content.PersonalInfo.Email.ShouldBe("anna@example.com");
        content.Summary.ShouldBe("Erfaren backend-utvecklare.");

        var exp = content.Experiences.ShouldHaveSingleItem();
        exp.Company.ShouldBe("Beta AB");
        exp.Role.ShouldBe("Backend-utvecklare");
        exp.StartDate.ShouldBe(new DateOnly(2021, 1, 1));
        exp.EndDate.ShouldBe(new DateOnly(2024, 6, 30));

        content.Educations.ShouldHaveSingleItem().Institution.ShouldBe("KTH");

        content.Skills.Count.ShouldBe(2);
        content.Skills[0].Name.ShouldBe("C#");
        content.Skills[0].YearsExperience.ShouldBe(8);
        content.Skills[1].Name.ShouldBe("PostgreSQL");
        content.Skills[1].YearsExperience.ShouldBeNull();
    }

    [Fact]
    public void Deserialize_ContentWithExplicitNullSupersetKeys_YieldsEmptyCollections()
    {
        // Edge: a payload that carries the superset keys but with an explicit null value must
        // coalesce to empty exactly like an absent key (the ctor's null-coalescing default).
        const string explicitNullJson =
            """
            {
              "personalInfo": { "fullName": "Bo Bengtsson", "email": null, "phone": null, "location": null },
              "experiences": [],
              "educations": [],
              "skills": [],
              "summary": null,
              "languages": null,
              "skillGroups": null,
              "sections": null
            }
            """;

        var content = JsonSerializer.Deserialize<ResumeContent>(
            explicitNullJson, EncryptedFieldRegistry.ContentJsonOptions);

        content.ShouldNotBeNull();
        content.Languages.ShouldBeEmpty();
        content.SkillGroups.ShouldBeEmpty();
        content.Sections.ShouldBeEmpty();
    }

    [Fact]
    public void Deserialize_ContentWithUnknownFutureKey_IsIgnored_RollbackDirectionPinned()
    {
        // The ROLLBACK direction of the Form B expand/contract (ADR 0095 D-D): if newer code
        // wrote a blob with a key this code version does not know, deserialization must skip
        // it, not throw — pins that ContentJsonOptions keeps STJ's default unmapped-member
        // handling (Skip). A future switch to JsonUnmappedMemberHandling.Disallow on the SPOT
        // would break every rolled-back read and must fail here first
        // (db-migration-writer 2026-07-05).
        const string futureJson =
            """
            {
              "personalInfo": { "fullName": "Cia Ceder", "email": null, "phone": null, "location": null },
              "experiences": [],
              "educations": [],
              "skills": [],
              "summary": null,
              "someFutureKey": { "nested": [1, 2, 3] }
            }
            """;

        var content = JsonSerializer.Deserialize<ResumeContent>(
            futureJson, EncryptedFieldRegistry.ContentJsonOptions);

        content.ShouldNotBeNull();
        content.PersonalInfo.FullName.ShouldBe("Cia Ceder");
    }
}
