using System.Text.Json;
using Jobbliggaren.Application.Resumes.Queries;
using Jobbliggaren.Domain.Resumes;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Queries;

// Fas 4b PR-8b 8b.2 (ADR 0096) — the CvTemplateOptions -> CvTemplateOptionsDto mapper
// (ResumeMappingExtensions.ToDto). Pure mapping, no I/O — a standalone VO extension, so it
// unit-tests directly (unlike ToDetailDto, which drags v.Content.ToDto() through the crypto
// interceptor and can only be proven via integration; see GetResumeByIdQueryHandlerTests note).
//
// Why this pin exists (mirrors ResumeContentMapperTests.ToDto_MapsEveryFieldDirectly): the
// integration suite exercises the mapper only with PhotoEnabled == false, so it can never
// distinguish the COMPOSED EffectiveAtsSafe (Template.AtsSafe && !PhotoEnabled) from the
// template-only CvTemplate.AtsSafe — the two agree whenever there is no photo. It also never
// asserts the DTO's PhotoShape at all. A mapper that wired EffectiveAtsSafe to o.Template.AtsSafe,
// or dropped/transposed PhotoShape, would stay green everywhere else. These tests fail loud on
// exactly that drift.
public class CvTemplateOptionsMapperTests
{
    [Fact]
    public void ToDto_MapsEveryMemberDirectly_AgainstAHandBuiltExpectedDto()
    {
        // Distinct value in every string slot so a transposition of any two members diverges.
        var options = new CvTemplateOptions(
            CvTemplate.Klar, CvAccentColor.Graphite, CvFontPair.Classic,
            CvDensity.Airy, PhotoEnabled: false, CvPhotoShape.Rounded);

        var expected = new CvTemplateOptionsDto(
            Template: "Klar",
            AccentColor: "Graphite",
            FontPair: "Classic",
            Density: "Airy",
            PhotoEnabled: false,
            PhotoShape: "Rounded",
            EffectiveAtsSafe: true); // Klar is ATS-safe, no photo → composed true

        var dto = options.ToDto();

        // Positional records over pure string/bool members — JSON equality proves every field
        // survived to the right slot (a dropped or transposed member diverges here).
        JsonSerializer.Serialize(dto).ShouldBe(JsonSerializer.Serialize(expected));
    }

    [Fact]
    public void ToDto_EffectiveAtsSafe_IsComposedVerdict_NotTemplateOnly_WhenPhotoEnabled()
    {
        // The one state the integration suite never reaches: an ATS-safe template WITH a photo.
        // Template-only AtsSafe is true here; the honest composed verdict is false. This is the
        // single case that distinguishes the two wirings (P5 — the DTO and per-render label must
        // never disagree; a photo-bearing CV must not be mislabelled "Klarar ATS", §5 no-mis-report).
        var options = new CvTemplateOptions(
            CvTemplate.Accentlinje, CvAccentColor.WineRed, CvFontPair.Modern,
            CvDensity.Normal, PhotoEnabled: true, CvPhotoShape.Square);

        var dto = options.ToDto();

        options.Template.AtsSafe.ShouldBeTrue();      // template half alone would report ATS-safe
        dto.EffectiveAtsSafe.ShouldBeFalse();         // composed verdict downgrades for the photo
        dto.PhotoEnabled.ShouldBeTrue();              // photo surfaced honestly
        dto.PhotoShape.ShouldBe("Square");            // the otherwise-untested DTO member
    }
}
