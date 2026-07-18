using System.Security.Cryptography;
using System.Text.Json;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Domain.RecentJobSearches;

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
/// <c>{"q":string?|null,"occupationGroup":[...],"municipality":[...],"region":[...],"employmentType":[...],"worktimeExtent":[...],"employer":[...],"remote":bool,"sortBy":int}</c>.
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
/// <para>
/// <b>#311 PR-2b C1 (ADR 0087 D6, 2026-07-01):</b> employer (org.nr) infogad MELLAN
/// worktimeExtent och sortBy. <b>#551 PR-D (2026-07-18):</b> remote (distans, bool)
/// infogad MELLAN employer och sortBy — skalär-svans före sortBy. Samma additiva
/// format-bump-mönster: ovillkorlig serialisering bumpar hashen för alla rader →
/// benign dubblett, cap-20-eviction självläker, ingen versionering/backfill.
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
            criteria.Employer, criteria.Remote, criteria.SortBy);
    }

    public static string Compute(
        string? q,
        IReadOnlyList<string> occupationGroup,
        IReadOnlyList<string> municipality,
        IReadOnlyList<string> region,
        IReadOnlyList<string> employmentType,
        IReadOnlyList<string> worktimeExtent,
        IReadOnlyList<string> employer,
        bool remote,
        JobAdSortBy sortBy)
    {
        ArgumentNullException.ThrowIfNull(occupationGroup);
        ArgumentNullException.ThrowIfNull(municipality);
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(employmentType);
        ArgumentNullException.ThrowIfNull(worktimeExtent);
        ArgumentNullException.ThrowIfNull(employer);

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

            // #311 PR-2b C1 (ADR 0087 D6) — employer (org.nr) infogad MELLAN worktimeExtent och
            // sortBy (dimensions-fält grupperade, sortBy kvar som svans; samma additiva mönster som
            // Klass 2 Fas B2). Additivt format-bump: gamla recent-rader får annan hash → benign
            // dubblett (cap-20-eviction självläker, ingen orphan). Ingen versionering.
            writer.WriteStartArray("employer");
            foreach (var emp in employer)
                writer.WriteStringValue(emp);
            writer.WriteEndArray();

            // #551 PR-D (ADR 0087 D6-paritet) — distans/remote infogad MELLAN employer och
            // sortBy (skalär-svans före sortBy-svansen; samma additiva mönster som employer/Klass 2).
            // Skrivs OVILLKORLIGT (som list-arrayerna ovan) → additivt format-bump: alla gamla
            // recent-rader får annan hash → benign dubblett (cap-20-eviction självläker, ingen
            // orphan). Ingen versionering, ingen backfill (efemär cache, ADR 0060 Beslut 6).
            writer.WriteBoolean("remote", remote);

            writer.WriteNumber("sortBy", (int)sortBy);
            writer.WriteEndObject();
        }

        var hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexStringLower(hash);
    }
}
