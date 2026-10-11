using System.Globalization;
using System.Text;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Application.JobAds.Internal;

/// <summary>
/// ADR 0067 Beslut 5c (Platsbanken sök-paritet Fas D2) — ren CPU-impl av
/// <see cref="ISearchQueryParser"/>. Normaliserar residual-fritext till en
/// säker FTS-sökterm; extraherar INGA dimensioner (Variant A+A, architect + CTO
/// 2026-06-10). <c>internal sealed</c> — bara porten korsar Application-gränsen.
///
/// <para>
/// Normaliseringen (en pass över råsträngen):
/// <list type="number">
///   <item>whitespace (inkl. tab/newline/CR — Unicode White_Space) kollapsas
///   till ETT mellanslag och trimmas i kanterna;</item>
///   <item>kontroll- och format-tecken (kategori <c>Cc</c>/<c>Cf</c> — null-byte,
///   C0-kontroll, zero-width-space, riktnings-override) strippas UTAN att lämna
///   mellanslag (de är osynlig stuffing, inte ordgränser);</item>
///   <item>resultat kortare än <see cref="SearchCriteria.QMinLength"/> → <c>null</c>
///   (en 1-tecken-term skulle via title-LIKE <c>%a%</c> matcha nästan hela
///   tabellen; recall-bevarande eftersom dimensionerna gäller ändå). <b>#831 —
///   den här grenen är sedan dess ENDA vakten på läsvägarna.</b> Tidigare stod
///   här "DoS-yta validatorn redan skyddar mot"; list-/facet-/remote-count-
///   validatorernas <c>MinimumLength</c> är borttagen, eftersom de mätte RÅ
///   text medan den här normaliserar först — <c>?q=a&lt;ZWSP&gt;</c> passerade
///   dem och nollades ändå här. Vad som återstår i validatorerna är
///   <c>MaximumLength</c>, som är pre-work-resursgränsen (se trunkerings-
///   punkten nedan: hela runa-passet sker FÖRE trunkering);</item>
///   <item>resultat längre än <see cref="SearchCriteria.QMaxLength"/> trunkeras
///   (defense-in-depth-tak — kastar ALDRIG).</item>
/// </list>
/// </para>
///
/// <para>
/// Whitespace-checken körs FÖRE Cc/Cf-strippningen: tab/newline är tekniskt
/// kategori <c>Cc</c> men ska behandlas som ordgräns (→ mellanslag), inte
/// strippas bort. websearch_to_tsquery hanterar sin egen lexem-tokenisering och
/// kastar aldrig på dålig syntax (ADR 0062) → parsern behöver inte escapa
/// tsquery-metatecken; dess ansvar är resurs-/DoS-hygien, inte injection-skydd
/// (EF/Npgsql parametriserar).
/// </para>
///
/// <para>
/// <b>Minus-words (#2143, ADR 0167).</b> After that pass the text is split on whitespace and
/// comma. A token whose first rune — after any leading <c>+</c> — is dash punctuation or U+2212
/// is an excluded term: its leading run of <c>+</c>, quotes, dashes and the operators websearch skips
/// while it waits for an operand (<c>! &amp; | ( ) &lt;</c>) and its trailing quotes are stripped; terms shorter than
/// <see cref="SearchCriteria.QMinLength"/>, terms with no letter or digit, and terms beyond
/// <see cref="ParsedSearchQuery.MaxExcludedTerms"/> are dropped. Only a token-initial dash excludes:
/// <c>e-handel</c> stays a positive word. When no token is excluded the result is exactly the
/// pre-#2143 residual, commas included.
/// </para>
/// </summary>
internal sealed class SearchQueryParser : ISearchQueryParser
{
    private static readonly char[] TokenSeparators = [' ', ','];

    public ParsedSearchQuery Parse(string? raw)
    {
        var normalized = Normalize(raw);
        if (normalized is null)
            return new ParsedSearchQuery(null);

        var tokens = normalized.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (!tokens.Any(IsExcludedToken))
            return new ParsedSearchQuery(AtLeastMinLength(normalized));

        var positive = new List<string>(tokens.Length);
        var excluded = new List<string>(ParsedSearchQuery.MaxExcludedTerms);
        foreach (var token in tokens)
        {
            if (!IsExcludedToken(token))
            {
                if (!IsBareOperator(token))
                    positive.Add(token);
                continue;
            }

            var term = StripExclusionPrefix(token);
            if (term.Length >= SearchCriteria.QMinLength
                && HasLetterOrDigit(term)
                && excluded.Count < ParsedSearchQuery.MaxExcludedTerms
                && !excluded.Contains(term, StringComparer.OrdinalIgnoreCase))
            {
                excluded.Add(term);
            }
        }

        return new ParsedSearchQuery(AtLeastMinLength(string.Join(' ', positive)), excluded);
    }

    private static string? AtLeastMinLength(string value) =>
        value.Length < SearchCriteria.QMinLength ? null : value;

    private static bool IsExcludedToken(string token)
    {
        var rest = token.AsSpan().TrimStart('+');
        return !rest.IsEmpty
            && Rune.DecodeFromUtf16(rest, out var first, out _) == System.Buffers.OperationStatus.Done
            && IsDash(first);
    }

    private static bool IsBareOperator(string token)
    {
        foreach (var rune in token.EnumerateRunes())
        {
            if (rune.Value != '+' && !IsDash(rune))
                return false;
        }

        return true;
    }

    private static string StripExclusionPrefix(string token)
    {
        var start = 0;
        foreach (var rune in token.EnumerateRunes())
        {
            if (rune.Value is not ('+' or '"' or '!' or '&' or '|' or '(' or ')' or '<') && !IsDash(rune))
                break;
            start += rune.Utf16SequenceLength;
        }

        return token[start..].TrimEnd('"');
    }

    // A term with no letter or digit yields no lexeme, no trigram and no label: it would exclude
    // nothing and still cost an anti join.
    private static bool HasLetterOrDigit(string term) =>
        term.EnumerateRunes().Any(Rune.IsLetterOrDigit);

    // U+2212 MINUS SIGN is a math symbol (Sm), not dash punctuation, but it is what a "minus"
    // key or an autocorrect produces as often as the hyphen-minus.
    private static bool IsDash(Rune rune) =>
        rune.Value == 0x2212 || Rune.GetUnicodeCategory(rune) == UnicodeCategory.DashPunctuation;

    // The pre-#2143 normalisation pass, unchanged: collapse whitespace, strip Cc/Cf, then bound the
    // length without splitting a surrogate pair. Returns null when nothing visible remains.
    private static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var sb = new StringBuilder(raw.Length);
        var pendingSpace = false;

        foreach (var rune in raw.EnumerateRunes())
        {
            // Whitespace FÖRST — tab/newline/CR är Cc men är ordgränser, inte
            // stuffing. Ledande whitespace ignoreras (sb tom); intern/avslutande
            // markeras pending och materialiseras bara om ett icke-whitespace-
            // tecken följer (→ ingen avslutande space, intern kollaps till ett).
            if (Rune.IsWhiteSpace(rune))
            {
                if (sb.Length > 0)
                    pendingSpace = true;
                continue;
            }

            // Kontroll-/format-tecken: osynlig stuffing → strippa utan space.
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format)
                continue;

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(rune.ToString());
        }

        if (sb.Length == 0)
            return null;

        if (sb.Length <= SearchCriteria.QMaxLength)
            return sb.ToString();

        // Trunkera till QMaxLength — men ALDRIG mitt i ett surrogatpar. Om
        // code-unit-gränsen splittar ett par (tecknet på gränsen är en high
        // surrogate vars low surrogate hamnar utanför snittet) backar vi till
        // rune-gränsen. En lone surrogate är ogiltig UTF-16 → kan inte enkodas
        // som UTF-8 och kraschar nedströms (Npgsql/Postgres text), vilket vore
        // brott mot "kastar ALDRIG"-garantin (ADR 0067 Beslut 5 / 0062).
        var cut = SearchCriteria.QMaxLength;
        if (char.IsHighSurrogate(sb[cut - 1]))
            cut--;

        return sb.ToString(0, cut);
    }
}
