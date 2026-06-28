using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Sök-kriterier för <see cref="IJobAdSearchQuery.SearchAsync"/>. Komponerar
/// filter-SPOT:en (<see cref="Filter"/> — <see cref="JobAdFilterCriteria"/>)
/// med presentations-fält (sortering, paginering). Båda sök-handlers
/// (<c>ListJobAds</c> + <c>RunSavedSearch</c>) mappar sitt query/criteria-record
/// till denna record (ADR 0039 Beslut 1, ADR 0062).
/// <para>
/// Kompositionen — <see cref="Filter"/> som egen typ snarare än tre lösa
/// fält — gör SPOT till en kompilator-garanti: <c>SearchAsync</c> och
/// <c>CountAsync</c> konsumerar samma <see cref="JobAdFilterCriteria"/>-typ
/// och kan inte divergera (Fowler 2018 — Introduce Parameter Object).
/// </para>
/// <para>
/// #293/#306 (ADR 0042 Beslut E-amendment 2026-06-28): det tidigare
/// <c>Since</c>-fältet ("Ny sedan"-fönstret) är BORTTAGET — "Ny" = OLÄST
/// beräknas nu på FE ur <c>CreatedAt</c> mot den per-användar oläst-watermarken,
/// inte server-side mot ett 7-dygnsfönster.
/// </para>
/// </summary>
public sealed record JobAdSearchCriteria(
    JobAdFilterCriteria Filter,
    JobAdSortBy SortBy,
    int Page,
    int PageSize);
