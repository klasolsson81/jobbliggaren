using Jobbliggaren.Infrastructure.JobSources.Platsbanken;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Infrastructure;

/// <summary>
/// Verifierar PII-stripping per ADR 0032 §8-amendment + TD-73. Sanitizer
/// är allowlist-baserad — okända keys droppas (default-deny per
/// Saltzer/Schroeder 1975).
/// </summary>
public class JobTechPayloadSanitizerTests
{
    [Fact]
    public void SanitizeForStorage_StripsRecruiterContactFields()
    {
        const string rawJson = """
        {
            "id": "12345",
            "headline": "Backend Developer",
            "employer": {
                "name": "Klarna",
                "contact_email": "rekryterare@klarna.com",
                "contact_name": "Anna Andersson",
                "phone_number": "+46701234567"
            }
        }
        """;

        var sanitized = JobTechPayloadSanitizer.SanitizeForStorage(rawJson);

        sanitized.ShouldContain("\"id\":\"12345\"");
        sanitized.ShouldContain("\"name\":\"Klarna\"");
        sanitized.ShouldNotContain("rekryterare@klarna.com");
        sanitized.ShouldNotContain("Anna Andersson");
        sanitized.ShouldNotContain("+46701234567");
        sanitized.ShouldNotContain("contact_email");
        sanitized.ShouldNotContain("contact_name");
        sanitized.ShouldNotContain("phone_number");
    }

    [Fact]
    public void SanitizeForStorage_PreservesPublicMetadata()
    {
        const string rawJson = """
        {
            "id": "12345",
            "headline": "Backend Developer",
            "description": { "text": "Vi söker en utvecklare." },
            "occupation": { "concept_id": "abc123" },
            "workplace_address": {
                "municipality": "Stockholm",
                "country_code": "SE"
            },
            "publication_date": "2026-05-12T10:00:00Z"
        }
        """;

        var sanitized = JobTechPayloadSanitizer.SanitizeForStorage(rawJson);

        sanitized.ShouldContain("\"headline\":\"Backend Developer\"");
        sanitized.ShouldContain("Vi s\\u00F6ker en utvecklare.");
        sanitized.ShouldContain("\"municipality\":\"Stockholm\"");
        sanitized.ShouldContain("\"country_code\":\"SE\"");
        sanitized.ShouldContain("\"concept_id\":\"abc123\"");
    }

    [Fact]
    public void SanitizeForStorage_DropsUnknownTopLevelKeys()
    {
        const string rawJson = """
        {
            "id": "12345",
            "headline": "X",
            "secret_internal_score": 42,
            "internal_recruiter_notes": "Hot lead"
        }
        """;

        var sanitized = JobTechPayloadSanitizer.SanitizeForStorage(rawJson);

        sanitized.ShouldNotContain("secret_internal_score");
        sanitized.ShouldNotContain("internal_recruiter_notes");
        sanitized.ShouldNotContain("Hot lead");
    }

    [Fact]
    public void SanitizeForStorage_HandlesNestedRecruiterContactInsideEmployer()
    {
        const string rawJson = """
        {
            "id": "1",
            "employer": {
                "name": "Acme AB",
                "organization_number": "556677-8899",
                "contact_email": "ceo@acme.se"
            }
        }
        """;

        var sanitized = JobTechPayloadSanitizer.SanitizeForStorage(rawJson);

        sanitized.ShouldContain("\"organization_number\":\"556677-8899\"");
        sanitized.ShouldNotContain("ceo@acme.se");
    }

    [Fact]
    public void SanitizeForStorage_HandlesArrays()
    {
        const string rawJson = """
        {
            "id": "1",
            "source_links": [
                { "label": "Annons", "url": "https://example.com/1" },
                { "label": "Secondary", "url": "https://example.com/2", "tracking_pixel": "evil" }
            ]
        }
        """;

        var sanitized = JobTechPayloadSanitizer.SanitizeForStorage(rawJson);

        sanitized.ShouldContain("https://example.com/1");
        sanitized.ShouldContain("https://example.com/2");
        sanitized.ShouldNotContain("tracking_pixel");
    }

    [Fact]
    public void SanitizeForStorage_WithInvalidJson_ReturnsEmptyObject()
    {
        var sanitized = JobTechPayloadSanitizer.SanitizeForStorage("{not-json");

        sanitized.ShouldBe("{}");
    }

    [Fact]
    public void SanitizeForStorage_WithNullOrEmpty_ReturnsEmptyObject()
    {
        JobTechPayloadSanitizer.SanitizeForStorage(string.Empty).ShouldBe("{}");
        JobTechPayloadSanitizer.SanitizeForStorage("   ").ShouldBe("{}");
    }

    [Fact]
    public void SanitizeForStorage_DefaultDeny_BlocksApplicationDetailsUrl()
    {
        // application_details är inte i allowlist eftersom det ofta innehåller
        // PII (rekryterar-email-mailto:-länkar). hela noden droppas.
        const string rawJson = """
        {
            "id": "1",
            "application_details": {
                "url": "mailto:rekryterare@acme.se?subject=Job"
            }
        }
        """;

        var sanitized = JobTechPayloadSanitizer.SanitizeForStorage(rawJson);

        sanitized.ShouldNotContain("application_details");
        sanitized.ShouldNotContain("mailto:");
    }

    [Fact]
    public void SanitizeForStorage_V2Shape_PreservesPublicV2Fields()
    {
        // v2-shape per JobStream 2.1.1 (web-verifierat 2026-05-13 mot riktig API).
        // Publika v2-fields ska bevaras.
        const string rawJson = """
        {
            "id": "31032467",
            "webpage_url": "https://arbetsformedlingen.se/platsbanken/annonser/31032467",
            "logo_url": "https://arbetsformedlingen.se/rest/employer-logo-api/api/v1/arbetsplatser/82482499/logotyper/logo.png",
            "number_of_vacancies": 1,
            "source_type": "VIA_ANNONSERA",
            "timestamp": 1778640565676,
            "identified_language": "sv",
            "description": {
                "text": "Job desc",
                "text_formatted": "<p>Job desc</p>"
            }
        }
        """;

        var sanitized = JobTechPayloadSanitizer.SanitizeForStorage(rawJson);

        sanitized.ShouldContain("webpage_url");
        sanitized.ShouldContain("logo_url");
        sanitized.ShouldContain("number_of_vacancies");
        sanitized.ShouldContain("source_type");
        sanitized.ShouldContain("text_formatted");
        sanitized.ShouldContain("identified_language");
    }

    [Fact]
    public void SanitizeForStorage_V2Shape_StripsEmployerPII()
    {
        // v2-svar har faktiskt employer.phone_number + employer.email — verifierat
        // i prod-data 2026-05-13. Default-deny ska droppa båda eftersom de inte
        // är i allowlist.
        const string rawJson = """
        {
            "id": "1",
            "employer": {
                "phone_number": "+46701234567",
                "email": "rekryterare@acme.se",
                "url": "www.acme.se",
                "organization_number": "5564932167",
                "name": "Acme AB",
                "workplace": "ACME AB"
            }
        }
        """;

        var sanitized = JobTechPayloadSanitizer.SanitizeForStorage(rawJson);

        sanitized.ShouldNotContain("+46701234567");
        sanitized.ShouldNotContain("rekryterare@acme.se");
        sanitized.ShouldNotContain("phone_number");
        sanitized.ShouldNotContain("\"email\"");
        sanitized.ShouldContain("organization_number");
        sanitized.ShouldContain("Acme AB");
    }

    [Fact]
    public void SanitizeForStorage_V2Shape_BlocksApplicationDetailsEmail()
    {
        // v2-svar har application_details.email + application_details.reference
        // + application_details.information. Hela noden droppas av default-deny.
        const string rawJson = """
        {
            "id": "1",
            "application_details": {
                "information": "Skicka CV till anneli",
                "reference": "Kock",
                "email": "anneli@eksjostadshotell.se",
                "via_af": false,
                "url": null,
                "other": null
            }
        }
        """;

        var sanitized = JobTechPayloadSanitizer.SanitizeForStorage(rawJson);

        sanitized.ShouldNotContain("application_details");
        sanitized.ShouldNotContain("anneli@eksjostadshotell.se");
        sanitized.ShouldNotContain("Skicka CV till anneli");
    }
}
