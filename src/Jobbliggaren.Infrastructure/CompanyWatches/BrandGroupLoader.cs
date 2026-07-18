using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;

namespace Jobbliggaren.Infrastructure.CompanyWatches;

/// <summary>
/// #311 PR-5 (ADR 0087 D4) — loads the committed, versioned brand-group catalogue
/// (<c>brand-groups.v1.json</c>) embedded in this assembly and maps it to the immutable Application
/// contract (<see cref="BrandGroupCatalog"/>). No <c>*File</c> type and no <c>JsonPropertyName</c>
/// leaks past Infrastructure (CLAUDE.md §2.1, parity <c>CriterionReferenceLoader</c> /
/// <c>BranschgruppLoader</c>).
///
/// <para>
/// <b>Every shape check here fails LOUD at host build</b> (the provider is instance-registered): a
/// malformed group is the vacuous-follow failure mode — a follow that matches nothing forever, or (for
/// a personnummer-shaped member) a personnummer committed to the repo. The one deliberate divergence
/// from the sibling loaders: an EMPTY <c>groups</c> array is LEGAL (returns an empty catalogue), because
/// this mechanism ships before any group is curated — <b>only</b> the empty top-level is legal; an
/// individual group with zero members is still a hard error.
/// </para>
/// </summary>
internal static class BrandGroupLoader
{
    private const string ResourceName =
        "Jobbliggaren.Infrastructure.CompanyWatches.brand-groups.v1.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    internal static BrandGroupCatalog Load()
    {
        using var stream = OpenResource(ResourceName);
        return LoadFrom(stream);
    }

    /// <summary>Test seam — drives synthetic (malformed or empty) assets through the REAL path
    /// (parity <c>CriterionReferenceLoader.LoadSniFrom</c>).</summary>
    internal static BrandGroupCatalog LoadFrom(Stream stream)
    {
        var file = JsonSerializer.Deserialize<BrandGroupsFile>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Varumärkesgrupp-assetet deserialiserade till null.");

        if (string.IsNullOrWhiteSpace(file.BrandGroupVersion))
            throw new InvalidOperationException("Varumärkesgrupp-assetet saknar brandGroupVersion.");

        // NOTE (the deliberate divergence): an EMPTY groups list is LEGAL — the mechanism ships before
        // any group is curated. Do NOT add an "empty groups" guard here.
        var groupsById = new Dictionary<string, BrandGroup>(StringComparer.Ordinal);
        foreach (var g in file.Groups)
        {
            var idResult = BrandGroupId.Create(g.Id);
            if (idResult.IsFailure)
                throw new InvalidOperationException(
                    $"Ogiltig varumärkesgrupp-slug: '{g.Id}' ({idResult.Error.Code}).");
            var id = idResult.Value.Value;

            if (string.IsNullOrWhiteSpace(g.DisplayName))
                throw new InvalidOperationException($"Varumärkesgrupp '{id}' saknar displayName.");

            if (g.Members.Count == 0)
                throw new InvalidOperationException(
                    $"Varumärkesgrupp '{id}' har inga medlemmar — en följning som aldrig matchar.");

            var members = new List<string>(g.Members.Count);
            var seenMembers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in g.Members)
            {
                var orgNrResult = OrganizationNumber.Create(m);
                if (orgNrResult.IsFailure)
                    throw new InvalidOperationException(
                        $"Varumärkesgrupp '{id}' har ett ogiltigt org.nr: '{m}'.");

                // A brand group is a set of legal entities (AB) — a personnummer-shaped (enskild-firma)
                // org.nr is by definition wrong data here, AND committing one to the repo would be a
                // plaintext personnummer at rest (CLAUDE.md §5, highest-priority guard). Reject fail-loud.
                if (orgNrResult.Value.IsPersonnummerShaped())
                    throw new InvalidOperationException(
                        $"Varumärkesgrupp '{id}' har en personnummer-formad medlem — grupper är juridiska "
                        + "personer (AB); en enskild firma får aldrig kureras (och aldrig checkas in).");

                if (!seenMembers.Add(orgNrResult.Value.Value))
                    throw new InvalidOperationException(
                        $"Varumärkesgrupp '{id}' listar samma org.nr två gånger.");

                members.Add(orgNrResult.Value.Value);
            }

            if (!groupsById.TryAdd(id, new BrandGroup(id, g.DisplayName.Trim(), members)))
                throw new InvalidOperationException($"Varumärkesgrupp '{id}' är deklarerad två gånger.");
        }

        return new BrandGroupCatalog(file.BrandGroupVersion, groupsById);
    }

    private static Stream OpenResource(string name)
    {
        var asm = typeof(BrandGroupLoader).Assembly;
        return asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"Embedded varumärkesgrupp-asset saknas: {name}. "
                + "Verifiera <EmbeddedResource> i Jobbliggaren.Infrastructure.csproj.");
    }
}

/// <summary>Deserialisation form. Infrastructure-only — the JSON member names never cross the
/// Application boundary (parity <c>SniFile</c>). The top-level <c>"//"</c> attribution key is
/// maintainer documentation and is deliberately not mapped.</summary>
internal sealed record BrandGroupsFile
{
    [JsonPropertyName("brandGroupVersion")]
    public string BrandGroupVersion { get; init; } = "";

    [JsonPropertyName("groups")]
    public IReadOnlyList<GroupFile> Groups { get; init; } = [];

    internal sealed record GroupFile
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; init; }

        [JsonPropertyName("members")]
        public IReadOnlyList<string> Members { get; init; } = [];
    }
}
