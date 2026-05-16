namespace JobbPilot.Application.JobAds.Queries.SuggestJobAdTerms;

/// <summary>
/// Escapar LIKE-metatecken (<c>%</c>, <c>_</c>, <c>\</c>) i användarinput så
/// det tolkas som literal text. ADR 0042 Beslut C / senior-cto-advisor
/// 2026-05-16 (in-block §9.6): oescapad <c>%</c>/<c>_</c> i ett left-anchored
/// prefix bryter left-anchor-egenskapen → btree-prefix-indexet kan inte
/// användas → seq-scan-DoS (ej SQL-injektion — EF.Functions.Like
/// parametriserar redan värdet; detta är LIKE-semantik-skydd).
/// Postgres default ESCAPE i LIKE är <c>\</c>.
/// </summary>
internal static class LikePattern
{
    public static string EscapePrefix(string input) =>
        input
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
}
