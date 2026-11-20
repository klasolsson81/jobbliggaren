using System.Text;
using System.Text.Json;
using Jobbliggaren.Infrastructure.JobAds.JuniorIt;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.JuniorIt;

/// <summary>
/// The junior IT list's shape checks (ADR 0186), driven through the real <c>LoadFrom(Stream)</c> seam with synthetic
/// assets; every malformed shape must fail loud, because the list is instance-registered and a malformed one is a page
/// that silently lists nothing. Plus the shipped file against its own contract.
/// </summary>
public class JuniorItListLoaderTests
{
    private const string ResourceName = "Jobbliggaren.Infrastructure.JobAds.JuniorIt.junior-it-list.v1.json";

    private static readonly string[] TopLevelKeys =
        ["//", "juniorItListVersion", "readAt", "occupationFieldConceptId", "headlinePattern", "curatedExternalIds"];

    private const string MinimalValid = """
        {
          "//": "test asset",
          "juniorItListVersion": "test",
          "readAt": "2026-11-19",
          "occupationFieldConceptId": "apaJ_2ja_LuF",
          "headlinePattern": "junior|trainee",
          "curatedExternalIds": ["90000001", "90000002"]
        }
        """;

    private static MemoryStream Json(string json) => new(Encoding.UTF8.GetBytes(json));

    /// <summary>Injects a defect and proves it landed, since an unmatched Replace is a silent no-op.</summary>
    private static MemoryStream Mutated(string find, string replaceWith)
    {
        MinimalValid.Contains(find, StringComparison.Ordinal).ShouldBeTrue($"'{find}' is not in MinimalValid.");
        return Json(MinimalValid.Replace(find, replaceWith, StringComparison.Ordinal));
    }

    [Fact]
    public void LoadFrom_MapsTheContract_WhenTheAssetIsWellFormed()
    {
        var list = JuniorItListLoader.LoadFrom(Json(MinimalValid));

        list.Version.ShouldBe("test");
        list.ReadAt.ShouldBe(new DateOnly(2026, 11, 19));
        list.OccupationFieldConceptId.ShouldBe("apaJ_2ja_LuF");
        list.HeadlinePattern.ShouldBe("junior|trainee");
        list.CuratedExternalIds.ShouldBe(["90000001", "90000002"]);
    }

    public static TheoryData<string, string, string> MalformedShapes => new()
    {
        { "a blank version", "\"juniorItListVersion\": \"test\"", "\"juniorItListVersion\": \" \"" },
        { "a version under another casing", "\"juniorItListVersion\"", "\"JuniorItListVersion\"" },
        { "no version", "\"juniorItListVersion\": \"test\",", "" },
        { "a readAt that is no date", "\"2026-11-19\"", "\"2026-13-01\"" },
        { "a readAt in another format", "\"2026-11-19\"", "\"19/11/2026\"" },
        { "a readAt with a time", "\"2026-11-19\"", "\"2026-11-19T00:00:00\"" },
        { "a blank field id", "\"apaJ_2ja_LuF\"", "\"\"" },
        { "a blank pattern", "\"junior|trainee\"", "\"  \"" },
        { "a pattern that does not compile", "\"junior|trainee\"", "\"junior(|trainee\"" },
        { "an id with a letter", "\"90000001\"", "\"x9000000\"" },
        { "an id with a space", "\"90000001\"", "\" 90000001\"" },
        { "an id with a hyphen", "\"90000001\"", "\"9000-0001\"" },
        { "an empty id", "\"90000001\"", "\"\"" },
        { "an id of 21 digits", "\"90000001\"", "\"900000010000000000001\"" },
        { "an id written twice", "\"90000002\"", "\"90000001\"" },
    };

    [Theory]
    [MemberData(nameof(MalformedShapes))]
    public void LoadFrom_AMalformedList_FailsLoud(string shape, string find, string replaceWith)
    {
        _ = shape;

        Should.Throw<InvalidOperationException>(() => JuniorItListLoader.LoadFrom(Mutated(find, replaceWith)));
    }

    [Fact]
    public void LoadFrom_AJsonNull_FailsLoud() =>
        Should.Throw<InvalidOperationException>(() => JuniorItListLoader.LoadFrom(Json("null")));

    [Fact]
    public void LoadFrom_AnIdWrittenAsANumber_IsRefused() =>
        Should.Throw<JsonException>(() => JuniorItListLoader.LoadFrom(Mutated("\"90000001\"", "90000001")));

    [Fact]
    public void LoadFrom_AComment_IsRefused() =>
        Should.Throw<JsonException>(() => JuniorItListLoader.LoadFrom(Mutated("\"//\": \"test asset\",", "// test asset")));

    [Fact]
    public void Load_TheShippedList_PassesItsOwnChecks()
    {
        var list = JuniorItListLoader.Load();

        list.OccupationFieldConceptId.ShouldBe("apaJ_2ja_LuF", "the Data/IT occupation field");
        list.CuratedExternalIds.Count.ShouldBe(143);
        list.CuratedExternalIds.ShouldAllBe(id => id.Length > 0 && id.All(char.IsAsciiDigit));
        list.CuratedExternalIds.Distinct(StringComparer.Ordinal).Count().ShouldBe(list.CuratedExternalIds.Count);
    }

    [Fact]
    public void TheShippedFile_HasExactlyTheSixTopLevelKeys()
    {
        // The loader ignores an unknown key, so a misspelt one would load as a missing value or be dropped unseen.
        using var stream = typeof(JuniorItListLoader).Assembly.GetManifestResourceStream(ResourceName);
        stream.ShouldNotBeNull();
        using var document = JsonDocument.Parse(stream);

        document.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)
            .ShouldBe([.. TopLevelKeys.Order(StringComparer.Ordinal)]);
        document.RootElement.GetProperty("curatedExternalIds").EnumerateArray()
            .ShouldAllBe(id => id.ValueKind == JsonValueKind.String);
    }
}
