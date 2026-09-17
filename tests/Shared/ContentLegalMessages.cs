namespace Jobbliggaren.TestSupport;

/// <summary>
/// Locates <c>web/jobbliggaren-web/messages/{language}/content-legal.json</c> — the published legal
/// copy — from a test binary by walking up to the repo root, so no test project's depth is hardcoded.
/// Linked into the test projects that pin a code constant against that copy
/// (<c>ContactAddressMatchesPublishedContactTests</c> in Application.UnitTests,
/// <c>TermsAcceptanceVersionsMatchPublishedPolicyTests</c> in Domain.UnitTests), the same way as
/// <c>TestIds</c> — one walk-up, two consumers, rather than a private copy per project. Fails loud and
/// names the path it looked for.
/// </summary>
internal static class ContentLegalMessages
{
    public static string PathFor(string language)
    {
        var relative = Path.Combine(
            "web", "jobbliggaren-web", "messages", language, "content-legal.json");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find {relative} by walking up from {AppContext.BaseDirectory}. "
            + "The published-copy pins need the repo checkout to be present.");
    }

    public static string ReadAllText(string language) => File.ReadAllText(PathFor(language));
}
