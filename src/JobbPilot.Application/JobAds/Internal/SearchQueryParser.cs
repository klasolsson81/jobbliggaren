using System.Globalization;
using System.Text;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Domain.SavedSearches;

namespace JobbPilot.Application.JobAds.Internal;

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
///   tabellen — DoS-yta validatorn redan skyddar mot; recall-bevarande eftersom
///   dimensionerna gäller ändå);</item>
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
/// </summary>
internal sealed class SearchQueryParser : ISearchQueryParser
{
    public ParsedSearchQuery Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new ParsedSearchQuery(null);

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

        if (sb.Length < SearchCriteria.QMinLength)
            return new ParsedSearchQuery(null);

        if (sb.Length <= SearchCriteria.QMaxLength)
            return new ParsedSearchQuery(sb.ToString());

        // Trunkera till QMaxLength — men ALDRIG mitt i ett surrogatpar. Om
        // code-unit-gränsen splittar ett par (tecknet på gränsen är en high
        // surrogate vars low surrogate hamnar utanför snittet) backar vi till
        // rune-gränsen. En lone surrogate är ogiltig UTF-16 → kan inte enkodas
        // som UTF-8 och kraschar nedströms (Npgsql/Postgres text), vilket vore
        // brott mot "kastar ALDRIG"-garantin (ADR 0067 Beslut 5 / 0062).
        var cut = SearchCriteria.QMaxLength;
        if (char.IsHighSurrogate(sb[cut - 1]))
            cut--;

        return new ParsedSearchQuery(sb.ToString(0, cut));
    }
}
