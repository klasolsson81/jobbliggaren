namespace Jobbliggaren.Application.Matching.Queries.GetMyMatchCount;

/// <summary>
/// ADR 0079 STEG 6 — antalet annonser som matchar profilen i headline-grad-setet
/// (Bra + Stark, Klas 2026-06-24). En grad-NEUTRAL siffra ("Det finns X jobb som matchar
/// din profil") — aldrig etiketterad "Toppmatchningar" (counten är Fast-bandet, G3-OPT-A).
/// <c>Count == 0</c> betyder antingen inget angivet yrke ELLER inga matchningar just nu
/// (båda honest; notisen renderar nollstate-copy, faller aldrig tillbaka på en mock-siffra).
/// </summary>
public sealed record MyMatchCountDto(int Count)
{
    public static readonly MyMatchCountDto Zero = new(0);
}
