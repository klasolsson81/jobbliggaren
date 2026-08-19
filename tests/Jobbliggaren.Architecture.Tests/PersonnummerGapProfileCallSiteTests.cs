using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Pins WHICH gap-bridging profile each production call site passes to
/// <c>PersonnummerTextNormalizer.Normalize</c> (#1415, ADR 0134).
///
/// <para><b>Why a fitness function and not just the compiler.</b> Making the profile a
/// required parameter forces a call site to CHOOSE; it cannot force the choice to be right,
/// and the two profiles are not orderable by strength. Widening a CV surface to
/// <c>SingleLineUserInput</c> would look like a strengthening in review — more forms
/// detected — while actually opening the ~1-in-10 date-column collision that
/// <c>PersonnummerBridgeCollisionRateTests</c> measures. Narrowing <c>?q=</c> the other way
/// would silently restore the leak #1415 was filed for. Neither shows up as a failing test
/// anywhere else, because each profile is internally consistent.</para>
///
/// <para><b>The file-name surface is the subtle one.</b> A CV file name is a single line a
/// human typed, so it reads like a <c>SingleLineUserInput</c> case — but it is REDACTED
/// downstream (<c>BackfillParsedResumeSourceFileNameMaskJob</c>,
/// <c>ParsedResume</c>/<c>ResumeFile</c>), and the redaction path deliberately keeps the
/// narrow bridge. Flagging it on a wider profile than the redactor can mask would produce
/// flagged-but-unmasked, which is exactly the flag ⊆ redaction superset break that #465 and
/// #498 each had to close. It stays on <c>ExtractedDocumentText</c> and that is a decision,
/// not an oversight.</para>
/// </summary>
public class PersonnummerGapProfileCallSiteTests
{
    // Call site -> the profile it must pass. Exact-match, so a NEW call site fails here too:
    // adding one is a decision about which policy governs a new kind of text, and that is
    // precisely the decision this file exists to make visible.
    private static readonly (string File, string Profile)[] Expected =
    [
        ("Jobbliggaren.Application/RecentJobSearches/Behaviors/RecentJobSearchCaptureBehavior.cs", "SingleLineUserInput"),
        ("Jobbliggaren.Application/Resumes/Commands/ImportResume/ImportResumeCommandHandler.cs", "ExtractedDocumentText"),
        ("Jobbliggaren.Application/Resumes/Common/AutoPromoteGate.cs", "ExtractedDocumentText"),
        ("Jobbliggaren.Application/Resumes/Common/ResumeContentPersonnummerGuard.cs", "ExtractedDocumentText"),
        ("Jobbliggaren.Domain/JobSeekers/JobSeeker.cs", "ExtractedDocumentText"),
        ("Jobbliggaren.Domain/Resumes/Resume.cs", "ExtractedDocumentText"),
    ];

    // Matches a CALL, never a doc-comment mention: the profile argument must be present.
    // FindingTargetFingerprint.cs names the type in a comment warning AGAINST using it, and
    // a name-based scan would count that as a call site — the #1414 grep trap, in test form.
    private static readonly Regex CallWithProfile = new(
        @"PersonnummerTextNormalizer\.Normalize\([^;]*?PersonnummerGapProfile\.(\w+)\)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Deliberately the crudest possible form. An earlier draft carried a variable-length
    // lookbehind to skip commented-out calls; it was removed unverified rather than kept
    // clever, because a guard clause nothing exercises is a claim, not a mechanism — and a
    // commented-out call here SHOULD show up as a discrepancy worth a human's eye anyway.
    private static readonly Regex AnyCall = new(
        @"PersonnummerTextNormalizer\.Normalize\(",
        RegexOptions.Compiled);

    [Fact]
    public void Every_production_call_site_passes_the_profile_its_text_kind_requires()
    {
        var srcRoot = Path.Combine(RepoRoot(), "src");
        Directory.Exists(srcRoot).ShouldBeTrue($"Hittade inte src-roten: {srcRoot}");

        var actual = new List<(string File, string Profile)>();
        foreach (var path in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            var relative = Path.GetRelativePath(srcRoot, path).Replace(Path.DirectorySeparatorChar, '/');
            foreach (Match m in CallWithProfile.Matches(File.ReadAllText(path)))
                actual.Add((relative, m.Groups[1].Value));
        }

        var actualSet = actual.Distinct().OrderBy(x => x.File, StringComparer.Ordinal)
            .ThenBy(x => x.Profile, StringComparer.Ordinal).ToList();
        var expectedSet = Expected.Distinct().OrderBy(x => x.File, StringComparer.Ordinal)
            .ThenBy(x => x.Profile, StringComparer.Ordinal).ToList();

        actualSet.ShouldBe(
            expectedSet,
            "the personnummer gap profile at a call site is a decision about what KIND of text " +
            "that call site holds, and the two profiles are not orderable by strength. Widening a " +
            "CV surface to SingleLineUserInput opens the ~1-in-10 date-column collision measured " +
            "in PersonnummerBridgeCollisionRateTests; narrowing ?q= restores the plaintext leak of " +
            "#1415. If this list is genuinely changing, change ADR 0134 in the same commit.");
    }

    [Fact]
    public void No_production_call_site_omits_the_profile()
    {
        // Non-vacuity for the scan above: it counts calls that CARRY a profile, so a call
        // written without one would simply not appear and the exact-match would pass while
        // the guard measured less than it claims. The compiler forbids that shape today; this
        // pins that the SCAN would notice if it ever stopped doing so.
        var srcRoot = Path.Combine(RepoRoot(), "src");

        var withProfile = 0;
        var anyCalls = 0;
        foreach (var path in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            var text = File.ReadAllText(path);
            withProfile += CallWithProfile.Count(text);
            anyCalls += AnyCall.Count(text);
        }

        anyCalls.ShouldBe(
            withProfile,
            $"{anyCalls - withProfile} call site(s) reach Normalize without a " +
            "PersonnummerGapProfile argument the scan can see");
        withProfile.ShouldBe(
            7,
            "six files, seven invocations (ImportResumeCommandHandler guards both the CV body " +
            "and the file name). A drop means a guarded surface lost its guard.");
    }

    // ADR 0134 D7: the flag-subset-of-redaction invariant survives the wider SingleLineUserInput
    // profile ONLY because nothing redacts a search query — the wider flag path has nothing on
    // the redaction path to be a superset of. That is a property of where Redact is called, and
    // it is exactly the kind of claim that rots silently: the day someone redacts a q value,
    // D7 becomes false and PersonnummerGuardPathEquivalenceTests still passes, because its
    // corpus is generated from the CV repertoire and would not contain the widened forms.
    [Fact]
    public void Every_redaction_call_site_is_a_resume_surface_so_D7s_vacuity_holds()
    {
        var srcRoot = Path.Combine(RepoRoot(), "src");

        var callSites = new List<string>();
        foreach (var path in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            if (RedactCall.IsMatch(File.ReadAllText(path)))
                callSites.Add(Path.GetRelativePath(srcRoot, path).Replace(Path.DirectorySeparatorChar, '/'));
        }

        // Non-vacuity first: an empty set would satisfy the "all are under Resumes/" assertion
        // trivially, and this guard would then be measuring nothing at all.
        callSites.ShouldNotBeEmpty("no Redact call sites found — the scan is measuring nothing");

        var outsideResumes = callSites
            .Where(f => !f.Contains("/Resumes/", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        outsideResumes.ShouldBeEmpty(
            "ADR 0134 D7 says the widened SingleLineUserInput flag profile is safe because the " +
            "redaction path never sees the text it governs. A Redact call outside Resumes/ may " +
            "break that: if it redacts a single-line user input, the flag path can now detect " +
            "forms the redactor cannot mask, which is the flagged-but-unmasked superset break " +
            "#465 and #498 each had to close. Re-read D7 before adding one. Offending files: " +
            string.Join(", ", outsideResumes));
    }

    private static readonly Regex RedactCall = new(
        @"PersonnummerRedactor\.Redact\(",
        RegexOptions.Compiled);

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
