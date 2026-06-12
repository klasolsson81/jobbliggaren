using System.Security.Cryptography;
using System.Text.Json;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.SavedSearches;

namespace JobbPilot.Domain.RecentJobSearches;

/// <summary>
/// Deterministic FilterHash-beräkning (CTO 2026-05-20 Variant A). Hashen är
/// uniqueness-identitet för <see cref="RecentJobSearch"/> per JobSeeker:
/// <c>UNIQUE(job_seeker_id, filter_hash)</c>. Domain-placerad eftersom
/// canonical-formatet är ett domän-kontrakt — om Infrastructure ändrar
/// serialisering tyst förlorar vi unique-index-integritet (Clean Arch
/// dependency rule, Martin 2017 kap. 22).
///
/// <para>
/// Canonical-JSON (Fas C2, ADR 0067 — "ssyk"-nyckeln utgick med occupation-
/// name-dimensionen; CTO-dom (d) 2026-06-09 — befintliga rader raderades i
/// C2-migrationen, ingen hash-versionering):
/// <c>{"q":string?|null,"occupationGroup":[...],"municipality":[...],"region":[...],"employmentType":[...],"worktimeExtent":[...],"sortBy":int}</c>.
/// Fältordningen är fixerad och dokumenterad — ändras den ändras hashen för
/// logiskt samma sökning. Listorna är redan sorted+distinct ordinal från
/// <see cref="SearchCriteria"/>:s invarianter → deterministisk. SHA-256 ger
/// fix 64-tecken hex-output, ingen känd preimage-attack relevant för denna
/// icke-säkerhets-användning.
/// <para>
/// <b>Fas B2 (ADR 0067 Beslut 6, 2026-06-12):</b> employmentType/worktimeExtent
/// infogade MELLAN region och sortBy (CTO-dom Q4 — dimensions-fält grupperade,
/// sortBy kvar som svans). Additivt format-bump: gamla recent-rader får annan
/// hash än ny logiskt-identisk sökning → benign dubblett (cap-20-eviction
/// självläker, INGEN rad orphan:as — vi adderar dims, till skillnad mot C2 där
/// Ssyk TOGS BORT). Ingen versionering.
/// </para>
/// </para>
/// </summary>
public static class FilterHashCalculator
{
    public static string Compute(SearchCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        return Compute(
            criteria.Q, criteria.OccupationGroup, criteria.Municipality,
            criteria.Region, criteria.EmploymentType, criteria.WorktimeExtent,
            criteria.SortBy);
    }

    public static string Compute(
        string? q,
        IReadOnlyList<string> occupationGroup,
        IReadOnlyList<string> municipality,
        IReadOnlyList<string> region,
        IReadOnlyList<string> employmentType,
        IReadOnlyList<string> worktimeExtent,
        JobAdSortBy sortBy)
    {
        ArgumentNullException.ThrowIfNull(occupationGroup);
        ArgumentNullException.ThrowIfNull(municipality);
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(employmentType);
        ArgumentNullException.ThrowIfNull(worktimeExtent);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            if (q is null)
                writer.WriteNull("q");
            else
                writer.WriteString("q", q);

            writer.WriteStartArray("occupationGroup");
            foreach (var g in occupationGroup)
                writer.WriteStringValue(g);
            writer.WriteEndArray();

            writer.WriteStartArray("municipality");
            foreach (var m in municipality)
                writer.WriteStringValue(m);
            writer.WriteEndArray();

            writer.WriteStartArray("region");
            foreach (var r in region)
                writer.WriteStringValue(r);
            writer.WriteEndArray();

            writer.WriteStartArray("employmentType");
            foreach (var e in employmentType)
                writer.WriteStringValue(e);
            writer.WriteEndArray();

            writer.WriteStartArray("worktimeExtent");
            foreach (var w in worktimeExtent)
                writer.WriteStringValue(w);
            writer.WriteEndArray();

            writer.WriteNumber("sortBy", (int)sortBy);
            writer.WriteEndObject();
        }

        var hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexStringLower(hash);
    }
}
