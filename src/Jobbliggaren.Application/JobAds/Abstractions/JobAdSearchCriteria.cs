using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Sök-kriterier för <see cref="IJobAdSearchQuery.SearchAsync"/>. Komponerar
/// filter-SPOT:en (<see cref="Filter"/> — <see cref="JobAdFilterCriteria"/>)
/// med presentations-fält (sortering, paginering, "Ny"-fönster). Båda
/// sök-handlers (<c>ListJobAds</c> + <c>RunSavedSearch</c>) mappar sitt
/// query/criteria-record till denna record (ADR 0039 Beslut 1, ADR 0062).
/// <para>
/// Kompositionen — <see cref="Filter"/> som egen typ snarare än tre lösa
/// fält — gör SPOT till en kompilator-garanti: <c>SearchAsync</c> och
/// <c>CountAsync</c> konsumerar samma <see cref="JobAdFilterCriteria"/>-typ
/// och kan inte divergera (Fowler 2018 — Introduce Parameter Object).
/// <see cref="Since"/> är runtime-presentationskontext för "Ny"-badgen
/// (ADR 0042 Beslut E) — <c>null</c> för RunSavedSearch (en körning
/// exponerar aldrig <c>IsNew = true</c>).
/// </para>
/// </summary>
public sealed record JobAdSearchCriteria(
    JobAdFilterCriteria Filter,
    JobAdSortBy SortBy,
    int Page,
    int PageSize,
    DateTimeOffset? Since);
