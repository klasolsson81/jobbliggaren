namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// ADR 0067 Beslut 5c (Platsbanken sök-paritet Fas D2) — normaliserar residual
/// fritext (det användaren skrev men INTE tabbade till ett strukturerat chip)
/// till en säker FTS-sökterm. Ren CPU, ingen Npgsql/IO (Martin 2017 kap. 22) —
/// porten + impl bor därför helt i Application (till skillnad från
/// <see cref="IOccupationSynonymExpander"/> som ligger i Infrastructure pga
/// <c>IOptions</c>-binding). Impl:en är <c>internal sealed</c>; bara denna port
/// korsar gränsen.
///
/// <para>
/// <b>Decision-domar (dotnet-architect + senior-cto-advisor 2026-06-10, VAL 1 =
/// Variant A+A):</b> parsern extraherar INGA dimensioner ur residual-strängen.
/// Disambiguering till strukturerade filter sker vid input (FE typeahead-chip,
/// ADR 0067 Beslut 5b/Fas E) — "snarare än via gissande backend" (Beslut 5c).
/// Att extrahera dimensioner här vore Speculative Generality (Fowler 2018) och
/// dubblerar ett ansvar ADR:n redan lagt på FE. Kontraktet bär därför bara
/// <see cref="ParsedSearchQuery.ResidualQ"/>.
/// </para>
///
/// <para>
/// <b>Kraschsäkerhet by design (ADR 0067 Beslut 5 + ADR 0062):</b>
/// <see cref="Parse"/> kastar ALDRIG och producerar en ResidualQ som antingen är
/// <c>null</c> (ingen FTS-term) eller en sträng på 2–100 tecken
/// (<c>SearchCriteria.QMinLength</c>..<c>QMaxLength</c>). ResidualQ matar
/// <c>JobAdFilterCriteria.Q</c> → q-FTS-hybridens OR-additiva gren; den blir
/// aldrig ett hårt AND-villkor. En residual som inte matchar något ger noll
/// träffar men kraschar/tomfiltrerar aldrig sökningen.
/// </para>
/// </summary>
public interface ISearchQueryParser
{
    /// <summary>
    /// Normaliserar rå residual-fritext till en säker
    /// <see cref="ParsedSearchQuery"/>. Idempotent på redan-rena värden
    /// ("utvecklare" → "utvecklare"). Returnerar alltid ett resultat — aldrig
    /// exception, aldrig null-objekt.
    /// </summary>
    ParsedSearchQuery Parse(string? raw);
}
