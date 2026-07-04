using System.IO;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// CLAUDE.md §5 / the em-dash copy rule ("no U+2014 in UI copy") — the durable BACKEND counterpart to
/// the frontend ESLint U+2014 guard. The FE guard scans only <c>web/</c>, so backend CV-review /
/// CV-improve rule copy that the FE renders VERBATIM (evidence <c>Note</c>/<c>Observation</c>,
/// <c>NotAssessedReason</c>, and the improvement <c>Rationale</c>) slipped through. #579 (epic #478
/// follow-track a) swept those strings; this guard makes the sweep permanent.
///
/// <para><b>Scope — rendered-copy source trees.</b> Every <c>.cs</c> under
/// <see cref="ScannedCopyDirectories"/> is where user-facing Swedish rule copy is authored as string
/// literals, so a STRING LITERAL there must not contain U+2014. COMMENTS / XML-doc are explicitly
/// exempt (§5 forbids em-dash in copy, not in comments — and they legitimately carry em-dashes), so the
/// scan reports an em-dash ONLY when it sits inside a string literal, never in a <c>//</c> / <c>///</c>
/// / <c>/* */</c> comment. <c>Resumes/Parsing/</c> is deliberately NOT scanned: <c>PeriodParser</c>
/// carries a legitimate U+2014 inside a date-range regex ("2019—2021"), which is code, not copy; a
/// rendered-copy tree has no such literal by construction.</para>
///
/// <para>The KB-DATA side of the same sweep (the rendered cliché <c>why</c> field in
/// <c>cliche-list.v2.json</c>) is pinned separately, at its own invariant home, by
/// <c>ClicheLexiconTests</c> — a source-scan cannot know KB projection semantics (<c>why</c> renders,
/// <c>guidance</c> does not), so that knowledge stays where the projection lives.</para>
///
/// <para>Non-vacuous by self-proving negatives (parity <see cref="OrganizationNumberSurfacingGuardTests"/>):
/// the scanner is proven to FLAG an em-dash in a regular / verbatim / interpolated string literal and to
/// IGNORE one in a line / doc / block / trailing comment.</para>
/// </summary>
public class EmDashInReviewCopyGuardTests
{
    /// <summary>
    /// Source trees whose string literals are user-facing CV copy (rendered verbatim to the user):
    /// the CV-review rules, the CV-improve transforms/engine, and the CV renderer's structural labels
    /// (the section headings QuestPDF stamps into the CV document). Add a tree here whenever new
    /// rendered copy is authored elsewhere. NB: <c>Resumes/Rendering</c> legitimately uses the EN-dash
    /// (U+2013) as a year-range separator ("2021–pågående"); the scan is U+2014-specific and never
    /// flags it.
    /// </summary>
    private static readonly IReadOnlyList<string> ScannedCopyDirectories =
    [
        "src/Jobbliggaren.Infrastructure/Resumes/Review",
        "src/Jobbliggaren.Infrastructure/Resumes/Improvement",
        "src/Jobbliggaren.Infrastructure/Resumes/Rendering",
    ];

    [Fact]
    public void Rendered_review_copy_string_literals_contain_no_em_dash()
    {
        var offending = new List<string>();

        foreach (var relativeDir in ScannedCopyDirectories)
        {
            var absoluteDir = SourceAbsolutePath(relativeDir);
            foreach (var file in Directory.EnumerateFiles(absoluteDir, "*.cs", SearchOption.AllDirectories))
            {
                foreach (var (line, text) in ReviewCopyScan.EmDashesInStringLiterals(File.ReadAllText(file)))
                {
                    offending.Add($"{Path.GetFileName(file)}:{line}: {text.Trim()}");
                }
            }
        }

        offending.ShouldBeEmpty(
            "Följande rad(er) har em-dash (U+2014) i en STRÄNG-literal i CV-gransknings-/CV-" +
            "förbättrings-copyn. Em-dash är förbjudet i UI-copy (CLAUDE.md §5); FE renderar dessa " +
            "strängar verbatim (parity FE:s ESLint-guard). Ersätt med kolon/punkt/parentes (eller " +
            "en-dash för ett intervall) per PR #138-doktrinen — applicera per site, aldrig global " +
            "find-replace. Kommentarer/XML-doc är undantagna. Träffar:\n" + string.Join("\n", offending));
    }

    [Fact]
    public void Scanned_copy_directories_all_exist()
    {
        // Pins every scanned tree — a moved/renamed copy tree fails loud here instead of making the
        // guard silently vacuous (parity OrganizationNumberSurfacingGuardTests' path-exist pin).
        foreach (var relativeDir in ScannedCopyDirectories)
        {
            var absolute = SourceAbsolutePath(relativeDir);
            Directory.Exists(absolute).ShouldBeTrue(
                $"arch-testet pekar på en katalog som inte finns: {absolute}. Uppdatera " +
                "ScannedCopyDirectories om copy-trädet flyttats/döpts om.");
        }
    }

    [Fact]
    public void Scanned_trees_use_no_raw_string_literal_until_the_lexer_handles_it()
    {
        // Forward-safety tripwire (dotnet-architect #579): ReviewCopyScan does NOT yet model raw string
        // literals ("""..."""), so a MULTI-LINE raw string would be a silent false negative (an em-dash
        // on a continuation line read as code). No scanned copy uses one today. This fails LOUD the day
        // one is introduced, forcing the lexer to be extended FIRST — the guard never silently
        // under-reports.
        var withRawString = new List<string>();
        foreach (var relativeDir in ScannedCopyDirectories)
        {
            foreach (var file in Directory.EnumerateFiles(SourceAbsolutePath(relativeDir), "*.cs", SearchOption.AllDirectories))
            {
                if (File.ReadAllText(file).Contains("\"\"\""))
                {
                    withRawString.Add(Path.GetFileName(file));
                }
            }
        }

        withRawString.ShouldBeEmpty(
            "En eller flera skannade filer använder en raw string literal (\"\"\"...\"\"\") som " +
            "ReviewCopyScan-lexern ännu inte modellerar — en flerradig raw string blir då en tyst " +
            "false negative. Utöka lexern att hantera raw strings INNAN sådan copy tas in. Filer: " +
            string.Join(", ", withRawString));
    }

    // --- self-proving negatives: the scanner must FLAG string-literal em-dashes ---

    [Fact]
    public void Scan_flags_em_dash_in_a_regular_string_literal()
    {
        const string source = "var note = \"versalt 'skrik' — håll neutral ton\";";
        ReviewCopyScan.EmDashesInStringLiterals(source)
            .ShouldNotBeEmpty("en em-dash i en vanlig sträng-literal ska flaggas.");
    }

    [Fact]
    public void Scan_flags_em_dash_in_an_interpolated_string_literal()
    {
        const string source = "var note = $\"{count} utropstecken — håll neutral ton\";";
        ReviewCopyScan.EmDashesInStringLiterals(source)
            .ShouldNotBeEmpty("en em-dash i en interpolerad sträng-literal ska flaggas.");
    }

    [Fact]
    public void Scan_flags_em_dash_in_a_verbatim_string_literal()
    {
        const string source = "var note = @\"perioden — ej bedömd\";";
        ReviewCopyScan.EmDashesInStringLiterals(source)
            .ShouldNotBeEmpty("en em-dash i en verbatim sträng-literal ska flaggas.");
    }

    [Fact]
    public void Scan_flags_em_dash_in_the_tail_after_an_interpolation_hole_with_a_string_join()
    {
        // The most common interpolated shape in the scanned copy (e.g.
        // $"...: {string.Join(", ", labels)}."). The nested ", " separator toggles the lexer's
        // string-state an EVEN number of times (a valid hole's quotes are always balanced), so it
        // re-syncs and an em-dash in the literal TAIL is still inside the string. Pinned so a lexer
        // "simplification" cannot silently turn this into a false negative.
        const string source = "var note = $\"rubriker: {string.Join(\", \", labels)} — skriv ut\";";
        ReviewCopyScan.EmDashesInStringLiterals(source)
            .ShouldNotBeEmpty("en em-dash i svansen efter ett string.Join-hål ska flaggas.");
    }

    [Fact]
    public void Scan_flags_em_dash_after_an_escaped_quote_in_a_string()
    {
        // Escaped quotes (\") must not desync the lexer (the `\\` -> skip-next branch) — an em-dash
        // after an escaped-quote pair is still inside the string. This shape is real (cliché why
        // "Översatt \"passionate\", ..." pre-fix; ContentRules A7/A9 citations).
        const string source = "var note = $\"klyscha \\\"passionate\\\" — dålig\";";
        ReviewCopyScan.EmDashesInStringLiterals(source)
            .ShouldNotBeEmpty("en em-dash efter ett escapat citat ska flaggas.");
    }

    // --- self-proving negatives: the scanner must IGNORE comment em-dashes ---

    [Fact]
    public void Scan_ignores_em_dash_in_a_line_comment()
    {
        const string source = "// tredje person — använd konsekvent perspektiv\nvar x = 1;";
        ReviewCopyScan.EmDashesInStringLiterals(source)
            .ShouldBeEmpty("en em-dash i en rad-kommentar är tillåten och ska inte flaggas.");
    }

    [Fact]
    public void Scan_ignores_em_dash_in_a_doc_comment()
    {
        const string source = "/// <summary>C2 Ton — neutral ton</summary>\npublic int X;";
        ReviewCopyScan.EmDashesInStringLiterals(source)
            .ShouldBeEmpty("en em-dash i en XML-doc-kommentar är tillåten och ska inte flaggas.");
    }

    [Fact]
    public void Scan_ignores_em_dash_in_a_block_comment_spanning_lines()
    {
        const string source = "/* C4 — no third person\n   spanning — lines */\nvar x = 1;";
        ReviewCopyScan.EmDashesInStringLiterals(source)
            .ShouldBeEmpty("en em-dash i en block-kommentar (även över radbrytning) ska inte flaggas.");
    }

    [Fact]
    public void Scan_ignores_em_dash_in_a_trailing_comment_after_a_clean_string()
    {
        const string source = "var note = \"ren copy\"; // tredje person — använd perspektiv";
        ReviewCopyScan.EmDashesInStringLiterals(source)
            .ShouldBeEmpty("en em-dash i en efterföljande kommentar (efter en ren sträng) ska inte flaggas.");
    }

    private static string SourceAbsolutePath(string relativePath)
    {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.ShouldNotBeNull(
            "kunde inte hitta repo-roten (CLAUDE.md) uppåt från test-bin — arch-testet behöver " +
            "källträdet för källtext-scan");
        return dir!.FullName;
    }
}

/// <summary>
/// A minimal C# lexical scanner that reports every U+2014 that sits INSIDE a string literal, ignoring
/// em-dashes in comments (// , ///, /* */) and char literals. Side-effect-free and independently
/// testable — see the self-proving negatives in <see cref="EmDashInReviewCopyGuardTests"/>. It tracks
/// exactly the constructs used by the scanned copy: regular (<c>"..."</c>), verbatim (<c>@"..."</c>),
/// and interpolated (<c>$"..."</c>) strings, so a <c>//</c> inside a string is never mistaken for a
/// comment, nor a quoted span inside a comment for a literal. Block comments and verbatim strings carry
/// across line breaks; line comments and regular strings do not (mirrors the C# lexer).
/// </summary>
internal static class ReviewCopyScan
{
    private const char EmDash = '—';

    /// <summary>The 1-based lines of <paramref name="source"/> with a U+2014 inside a string literal.</summary>
    internal static IReadOnlyList<(int Line, string Text)> EmDashesInStringLiterals(string source)
    {
        var hits = new List<(int, string)>();
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var state = State.Code;
        var verbatim = false;

        for (var li = 0; li < lines.Length; li++)
        {
            var line = lines[li];
            var flagged = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                switch (state)
                {
                    case State.Code:
                        if (c == '/' && Next(line, i) == '/') { i = line.Length; } // line comment: skip rest of line
                        else if (c == '/' && Next(line, i) == '*') { state = State.BlockComment; i++; }
                        else if (c == '"') { state = State.Str; verbatim = IsVerbatimStart(line, i); }
                        else if (c == '\'') { state = State.Char; }
                        break;

                    case State.BlockComment:
                        if (c == '*' && Next(line, i) == '/') { state = State.Code; i++; }
                        break;

                    case State.Str when verbatim:
                        if (c == '"')
                        {
                            if (Next(line, i) == '"') { i++; } // "" is an escaped quote — stay in the string
                            else { state = State.Code; }
                        }
                        else if (c == EmDash) { flagged = true; }
                        break;

                    case State.Str: // regular or interpolated (non-verbatim)
                        if (c == '\\') { i++; } // escape the next char (e.g. \")
                        else if (c == '"') { state = State.Code; }
                        else if (c == EmDash) { flagged = true; }
                        break;

                    case State.Char:
                        if (c == '\\') { i++; }
                        else if (c == '\'') { state = State.Code; }
                        break;
                }
            }

            // Only block comments and verbatim strings cross a newline; reset the transient states
            // defensively at end of line (an unterminated line comment / regular string / char is a
            // compile error, never a real input).
            if (state is State.Char) state = State.Code;
            if (state == State.Str && !verbatim) state = State.Code;

            if (flagged) hits.Add((li + 1, line));
        }

        return hits;
    }

    private static char Next(string line, int i) => i + 1 < line.Length ? line[i + 1] : '\0';

    private static bool IsVerbatimStart(string line, int quoteIndex)
    {
        // @"  /  $@"  /  @$"  are verbatim; $"  and a plain "  are not.
        if (quoteIndex >= 1 && line[quoteIndex - 1] == '@') return true;
        if (quoteIndex >= 2 && line[quoteIndex - 1] == '$' && line[quoteIndex - 2] == '@') return true;
        return false;
    }

    private enum State { Code, BlockComment, Str, Char }
}
