using JobbPilot.Domain.Applications;

namespace JobbPilot.Infrastructure.Security;

/// <summary>
/// TD-13 (ADR 0049, CTO-triage lucka 3) — EN sanning (SPOT) för vilka
/// (entitet, property) som krypteras. Statisk allowlist i Infrastructure;
/// Domain bär INGA krypto-attribut (ADR 0009, Clean Arch). Delas av
/// <see cref="FieldEncryptionSaveChangesInterceptor"/> (write) +
/// <see cref="FieldDecryptionMaterializationInterceptor"/> (read).
///
/// C3 = de tre TEXT-kolumnerna. <c>ResumeVersion.Content</c> (JSONB) hör
/// till C4 (Beslut 5 — JSON-transform komponeras separat) och läggs till då.
/// </summary>
internal static class EncryptedFieldRegistry
{
    private static readonly Dictionary<Type, string[]> Map = new()
    {
        [typeof(DomainApplication)] = ["CoverLetter"],
        [typeof(ApplicationNote)] = ["Content"],
        [typeof(FollowUp)] = ["Note"],
    };

    public static bool TryGetEncryptedProperties(Type entityType, out string[] properties) =>
        Map.TryGetValue(entityType, out properties!);
}
