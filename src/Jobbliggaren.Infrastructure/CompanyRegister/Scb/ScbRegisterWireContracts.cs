using System.Text.Json.Serialization;

namespace Jobbliggaren.Infrastructure.CompanyRegister.Scb;

/// <summary>
/// #560 (ADR 0091) — REQUEST wire contracts for SCB's <c>je/raknaforetag</c>, <c>je/hamtaforetag</c>
/// and <c>je/kodtabell</c> endpoints. These are Infrastructure-INTERNAL (the anti-corruption boundary,
/// ADR 0032): Application never references them — the client maps a neutral <see cref="ScbQuery"/> to
/// this shape on the way out and translates the raw JSON response to <see cref="ScbCompanyRecord"/>
/// on the way in. The request body shape (<c>{"Kategorier":[{"Kategori":..,"Kod":[..]}]}</c>) is
/// smoke-verified (issue #560). RESPONSE parsing is done tolerantly via <c>JsonElement</c> in the
/// client rather than typed DTOs, because the exact response envelope (property casing/nesting) is
/// confirmed against the live API at the population run.
/// </summary>
internal sealed class ScbCategoryRequest
{
    [JsonPropertyName("Kategori")]
    public required string Kategori { get; init; }

    [JsonPropertyName("Kod")]
    public required IReadOnlyList<string> Kod { get; init; }

    /// <summary>SNI level for the <c>Bransch</c> category (3 = the 5-digit detaljgrupp used by the
    /// deep split; #628 live-verified). Omitted from the JSON when null.</summary>
    [JsonPropertyName("BranschNiva")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BranschNiva { get; init; }
}

/// <summary>The filter body for <c>raknaforetag</c>/<c>hamtaforetag</c>: the AND-set of category
/// constraints. No Företagsstatus filter — the full mirror (incl. de-registered) is fetched
/// (ADR 0091 / Fork 4).</summary>
internal sealed class ScbFilterRequest
{
    [JsonPropertyName("Kategorier")]
    public required IReadOnlyList<ScbCategoryRequest> Kategorier { get; init; }
}

/// <summary>The body for <c>kodtabell</c>: fetch the fixed code table for one category (e.g.
/// <c>SätesKommun</c> → the 290 municipality codes; <c>Juridisk form</c> → the legal-form codes).
/// <c>Bransch</c> requires <see cref="BranschNiva"/> to return the codes at that SNI level (3 = the
/// ~800 5-digit detaljgrupper the #628 deep split fans by 2-digit prefix).</summary>
internal sealed class ScbKodtabellRequest
{
    [JsonPropertyName("Kategori")]
    public required string Kategori { get; init; }

    /// <summary>SNI level for the <c>Bransch</c> category (3 = 5-digit). Omitted from the JSON when
    /// null (all other categories).</summary>
    [JsonPropertyName("BranschNiva")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BranschNiva { get; init; }
}
