using System.Text.RegularExpressions;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// The ONE owner of "which glyphs does a CV glue several items onto a single line with?" (#844).
///
/// <para>Two call sites share this knowledge, and they are the same knowledge piece:</para>
/// <list type="bullet">
/// <item><c>ParseList</c> — a skills/languages line ("C# · PostgreSQL · Docker", "A | B").</item>
/// <item><see cref="PreambleResidue"/> — a rail/sidebar CV whose contact block linearizes onto ONE
/// line ("Anna Andersson | anna@example.com | 070-123 45 67 | Göteborg").</item>
/// </list>
///
/// <para>The proof that they are one piece and not two is in the rule's own long-standing comment:
/// <i>"Space is deliberately NOT a separator (it would shred multi-word skills like 'ASP.NET Core')"</i>
/// — the contact rail needs exactly that same restraint, or it would shred "Anna Andersson" and
/// "Upplands Väsby". A glyph added here for skills is wanted in the rail too. Same change-reason
/// (CCP), so: one home.</para>
///
/// <para><b>What deliberately did NOT move here</b>, because sharing it would be a false DRY that
/// couples unrelated change-reasons:</para>
/// <list type="bullet">
/// <item><c>TitleOrgSeparators</c> splits an entry HEADER into two FIELDS and therefore contains
/// prose prepositions (<c>" at "</c>, <c>" på "</c>, <c>" hos "</c>). Subtracting "på" from a
/// preamble would be nonsense.</item>
/// <item><c>BulletMarkers</c> answers "is the FIRST character of this line a bullet?" — a
/// line-leading marker rule, not glue between items.</item>
/// </list>
/// FORM, so it lives in C# and not in the lexicon (ADR 0108 §2).
/// </summary>
internal static partial class InlineSeparators
{
    /// <summary>
    /// Splits a line into the items it glues together. Kept byte-identical to the pattern
    /// <c>ParseList</c> previously owned, so the promotion is behaviour-preserving by inspection
    /// (the <see cref="DatePatterns"/> precedent).
    /// </summary>
    // #252: list/keyword sections separate items by newline, comma, semicolon, middot, bullet or
    // pipe. Space is deliberately NOT a separator (see the class remarks).
    [GeneratedRegex(@"[\n,;•·|]", RegexOptions.CultureInvariant)]
    internal static partial Regex Pattern();

    internal static string[] Split(string text) => Pattern().Split(text);

    /// <summary>
    /// Strips leading/trailing glue glyphs from one item. Promoted together with
    /// <see cref="Pattern"/> because the two are always applied at the same call site
    /// (<c>ParseList</c> split, then trimmed each token) — they are one rule in two statements, and
    /// separating them is how the halves drift apart.
    /// </summary>
    /// <remarks>
    /// <para>Trims BOTH ends. It used to trim only the leading end while its own documentation said
    /// "leading/trailing" — and that gap was load-bearing: "Göteborg |" and "· Göteborg ·" normalised
    /// differently depending on which side asked, which is how a recogniser and its consumer disagreed
    /// about the same city. The doc was the specification; the code was the bug.</para>
    ///
    /// <para><b>And it is a FIXPOINT — normalising twice must equal normalising once.</b>
    /// <c>string.Trim(char[])</c> removes one CONTIGUOUS run and stops at the first character outside
    /// the set, so a single pass leaves "* - Göteborg" as "- Göteborg" (the space halts it) while a
    /// double pass yields "Göteborg". Any two call sites that normalise a different NUMBER of times
    /// then disagree — which is the same fork one layer down, and this defect class has already bitten
    /// five times in one change.
    ///
    /// Idempotence retires the PROPERTY rather than the instance: it no longer matters how many times,
    /// or in what order, a call site normalises. That is a stronger guarantee than any convention about
    /// who calls what, because it cannot be forgotten.</para>
    /// </remarks>
    internal static string TrimGlue(string item)
    {
        var previous = item;
        var current = item.Trim().Trim(GlueGlyphs).Trim();

        while (current.Length != previous.Length)
        {
            previous = current;
            current = current.Trim().Trim(GlueGlyphs).Trim();
        }

        return current;
    }

    private static readonly char[] GlueGlyphs = ['•', '-', '*', '·', '–', '—', '|'];
}
