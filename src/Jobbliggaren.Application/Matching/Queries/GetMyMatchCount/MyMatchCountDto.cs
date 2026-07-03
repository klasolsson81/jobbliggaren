namespace Jobbliggaren.Application.Matching.Queries.GetMyMatchCount;

/// <summary>
/// ADR 0079 STEG 6, harmoniserad 2026-07-03 (Klas "samma siffra"; CTO-bind H2) — antalet
/// aktiva annonser som matchar den sparade matchningens sök-facetter (yrke ∧ ort ∧
/// anställningsform som hårda filter). Samma tal som setup-modalens live-räknare och den
/// länkade /jobb-sidans TotalCount per konstruktion (delad <c>ApplyFilter</c>-SPOT, inga
/// grad-band i counten — graden lever som badges/sort/bakgrundsmatchning).
/// <c>Count == 0</c> betyder antingen inget angivet yrke ELLER inga annonser för valen just
/// nu (båda honest; notisen renderar nollstate-copy, faller aldrig tillbaka på en mock-siffra).
/// </summary>
public sealed record MyMatchCountDto(int Count)
{
    public static readonly MyMatchCountDto Zero = new(0);
}
