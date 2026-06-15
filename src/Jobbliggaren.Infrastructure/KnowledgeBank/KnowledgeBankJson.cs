using System.Text.Json;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// Shared System.Text.Json options for all three knowledge-bank loaders (F4-7) — one
/// parse policy across rubric/cliché/verb (code-reviewer Minor 2). Default
/// System.Text.Json ignores unknown members (the forward-/back-compat skip-unknown the
/// N-1 test asserts); no <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/>
/// — Swedish tokens are mapped explicitly via <see cref="KnowledgeBankTokens"/> (DQ7).
/// </summary>
internal static class KnowledgeBankJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        AllowTrailingCommas = true,
    };
}
