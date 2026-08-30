using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Applications.Queries.GetActivityReport;

/// <summary>
/// AF activity-report read model (issue #316). Deterministic projection — NO AI.
/// Lists every application the current user submitted in the selected month,
/// one item per sought job, so the FE can offer a per-field copy button.
///
/// "Submitted in month M" = <c>AppliedAt ∈ [start, end)</c> regardless of the
/// application's CURRENT status: the person applied that month even if the
/// thread has since moved to Rejected/Accepted/Ghosted (senior-cto-advisor
/// 2026-06-28 D3). Draft applications have a null <c>AppliedAt</c> and are
/// excluded; soft-deleted applications are excluded by the global query filter.
///
/// Month boundaries are the SWEDISH civil month, half-open (Klas-direktiv
/// 2026-07-28, ADR 0064 Amendment — the same ruling that moved "nya idag" off
/// UTC midnight). They come from <see cref="ISwedishCalendar"/>, so the window
/// and the "Datum sökt" column the FE already renders in Europe/Stockholm now
/// agree; they did not before, by up to two hours at each boundary. When the
/// caller passes no month the handler defaults to the current SWEDISH month
/// (CLAUDE.md §5 — never <c>DateTime.UtcNow</c>).
/// </summary>
public sealed class GetActivityReportQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITaxonomyReadModel taxonomy,
    IDateTimeProvider clock,
    ISwedishCalendar calendar)
    : IQueryHandler<GetActivityReportQuery, ActivityReportDto>
{
    public async ValueTask<ActivityReportDto> Handle(
        GetActivityReportQuery query, CancellationToken cancellationToken)
    {
        var month = ResolveMonth(query);

        // Half-open [Start, End) on the Swedish civil calendar. BOTH ends come
        // from the port: the exclusive end is asked for, never derived.
        //
        // The retired line wrote `start.AddMonths(1)`, and against the UTC start
        // it replaced that was CORRECT — the anchor was the 1st at 00:00:00Z,
        // every month has a 1st, so nothing ever clamped. The form is lethal only
        // against a SWEDISH boundary, whose anchor is the previous month's LAST
        // day: short by 2 d 23 h for March, by a day for May, July and December,
        // by 1 d 1 h for October, and silently exact in the other seven months.
        // Carrying the line across unchanged is what the port's remarks forbid,
        // and it is why the end is a value here rather than an operation. This is
        // a WHERE against the database, so the failure mode is quietly too few
        // rows in a document filed with Arbetsförmedlingen.
        var window = calendar.MonthWindow(month);

        if (!currentUser.UserId.HasValue)
            return new ActivityReportDto(month.Year, month.Month, []);

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return new ActivityReportDto(month.Year, month.Month, []);

        // Hoisted for readability only. An earlier version of this comment claimed
        // EF translates a local more reliably than member access on a struct;
        // that is false, and `currentUser.UserId.Value` seven lines above is the
        // counter-example — a struct member access inside an EF predicate, run
        // against real Postgres by the Api integration suite. Both are evaluatable
        // subtrees rooted in captured state rather than in the query root, and EF
        // parameterises them identically. (They are NOT the same closure: one is a
        // field reached through `this`, the other a local in a generated display
        // class. An earlier draft said so; the conclusion survives, that mechanism
        // claim did not.)
        //
        // What IS true of these two values: both are `timestamptz` parameters,
        // the port guarantees Offset == Zero, and Npgsql throws otherwise. The
        // unit tests run EF InMemory and are blind to that;
        // GetActivityReportSwedishMonthBoundaryIntegrationTests is the gate.
        var start = window.Start;
        var end = window.End;

        // ADR 0048: EN LEFT JOIN job_ads via GroupJoin/DefaultIfEmpty FÖRE
        // materialisering. IgnoreQueryFilters / hand-rullade soft-delete-predikat
        // FÖRBJUDET (ADR 0048 c).
        //
        // j == null betyder att ansökan saknar ANNONSRAD (manuell eller enbart brev)
        // — INTE att annonsen är tillbakadragen. JobAd har ingen soft-delete-axel
        // (#821). En tillbakadragen annons ARKIVERAS (Status = "Archived") och joinar
        // fortfarande → metadatan visas. Det är rimligt här (användaren sökte ju
        // jobbet), men observera att samma falska premiss bar en DPIA-utsaga på
        // employer-attributions-vägarna → #824.
        var rows = await db.Applications
            .AsNoTracking()
            .Where(a => a.JobSeekerId == jobSeekerId
                        && a.AppliedAt != null
                        && a.AppliedAt >= start
                        && a.AppliedAt < end)
            .OrderBy(a => a.AppliedAt)
            .ThenBy(a => a.Id)
            .GroupJoin(db.JobAds, a => a.JobAdId, j => j.Id, (a, ja) => new { a, ja })
            .SelectMany(x => x.ja.DefaultIfEmpty(), (x, j) => new
            {
                x.a,
                j,
                // "Ort"-källa: kommun-concept-id är sedan #841 en vanlig mappad
                // JobAd-property (ACL, ADR 0043/0067 — JobTech-taxonomikod, ingen
                // Domain-koppling; C#-skriven, ej STORED/shadow). Ingen annonsrad (manuell/enbart
                // brev) → j == null → ingen ort. (#805-3: en ARKIVERAD annons
                // joinar fortfarande och bär sin ort — den gamla "soft-deletad"-
                // utsagan var falsk, #821.)
                MunicipalityConceptId =
                    j != null ? j.MunicipalityConceptId : null
            })
            // #892 (CTO R1/R5 + §14.3): rapporten LÄSTE live-annonsen medan Art.
            // 17(3)(e)-retentionen av snapshot_company motiverades av EXAKT den här
            // läsvägen — efter en radering sa arbetsgivarkolumnen "[raderad]".
            // Raderad annons → identitet ur ansökans AdSnapshot; utan snapshot →
            // null (DTO:ns frånvaro-vokabulär, FE renderar "Saknas") — aldrig
            // domän-sentinelen "[raderad]" över gränsen (§2.3). AdStatus bär
            // livscykel-signalen till FE-markören (aldrig defaultad, #805-3-idiomet).
            // Orten läses fortsatt live: *_concept_id-facetterna överlever Erase()
            // (NotRecruiterData — se JobAd.Erase-kommentaren). Source-ternären
            // avviker MEDVETET från Employer/Title/Url: Source överlever också
            // Erase() (samma NotRecruiterData-klass), så utan snapshot är live-
            // värdet fortfarande SANT — bara de blankade identitetsfälten går
            // till null (code-review Minor 2).
            .Select(r => new
            {
                r.a.Id,
                AppliedAt = r.a.AppliedAt!.Value,
                Employer = r.j != null
                    ? (r.j.Status == JobAdStatus.Erased
                        ? (r.a.AdSnapshot != null ? r.a.AdSnapshot.Company : null)
                        : r.j.Company.Name)
                    : r.a.ManualPosting != null ? r.a.ManualPosting.Company : null,
                Title = r.j != null
                    ? (r.j.Status == JobAdStatus.Erased
                        ? (r.a.AdSnapshot != null ? r.a.AdSnapshot.Title : null)
                        : r.j.Title)
                    : r.a.ManualPosting != null ? r.a.ManualPosting.Title : null,
                Url = r.j != null
                    ? (r.j.Status == JobAdStatus.Erased
                        ? (r.a.AdSnapshot != null ? r.a.AdSnapshot.Url : null)
                        : r.j.Url)
                    : r.a.ManualPosting != null ? r.a.ManualPosting.Url : null,
                Source = r.j != null
                    ? (r.j.Status == JobAdStatus.Erased && r.a.AdSnapshot != null
                        ? r.a.AdSnapshot.Source
                        : r.j.Source.Value)
                    : r.a.ManualPosting != null ? "Manual" : null,
                AdStatus = r.j != null ? r.j.Status.Value : null,
                r.MunicipalityConceptId
            })
            .ToListAsync(cancellationToken);

        var locationByConceptId = await ResolveLocationsAsync(
            rows.Select(r => r.MunicipalityConceptId), cancellationToken);

        var items = rows
            .Select(r => new ActivityReportItemDto(
                r.Id.Value,
                r.AppliedAt,
                r.Employer,
                r.Title,
                r.MunicipalityConceptId is not null
                    && locationByConceptId.TryGetValue(r.MunicipalityConceptId, out var loc)
                        ? loc
                        : null,
                r.Source,
                r.Url,
                r.AdStatus))
            .ToList();

        return new ActivityReportDto(month.Year, month.Month, items);
    }

    private CivilMonth ResolveMonth(GetActivityReportQuery query)
    {
        if (query.Year.HasValue && query.Month.HasValue)
            return CivilMonth.Of(query.Year.Value, query.Month.Value);

        // Default = the current SWEDISH month (Klas 2026-06-28: the current month
        // is always the sensible default; the picker still lets you pick an
        // earlier month to report). Validator guarantees both-or-neither, so we
        // only reach here when both are null.
        //
        // Reading clock.UtcNow.Year/.Month put the first one to two hours of
        // every Swedish month in the PREVIOUS one — and on 1 January in the
        // previous YEAR. Deriving it from a boundary instant would be worse: that
        // value is the previous month by construction, every day of the month.
        return calendar.MonthOf(clock.UtcNow);
    }

    /// <summary>
    /// Batch-resolve distinct municipality concept-ids to human labels via the
    /// taxonomy ACL (one call, bounded). A concept-id that does not resolve
    /// (taxonomy drift) comes back without a label — we drop it rather than
    /// surface the opaque id in a civic report (§5).
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> ResolveLocationsAsync(
        IEnumerable<string?> conceptIds, CancellationToken cancellationToken)
    {
        var distinct = conceptIds
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct()
            .ToList();

        if (distinct.Count == 0)
            return new Dictionary<string, string>();

        var labels = await taxonomy.ResolveLabelsAsync(distinct, cancellationToken);

        var map = new Dictionary<string, string>(labels.Count);
        foreach (var label in labels)
        {
            // An unresolved code renders as the neutral empty placeholder, never
            // as a leaked concept-id (§5).
            if (label.Label is not null)
                map[label.ConceptId] = label.Label;
        }
        return map;
    }
}
