using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries.ListCompanyWatches;

/// <summary>
/// ADR 0087 D3 — list the current user's active company follows. UserId-scoped (the soft-delete
/// query filter hides unfollowed rows). <c>company_name</c> is resolved at READ via a projection
/// over <c>job_ads</c> (ADR 0087 D2 — never a denormalised snapshot): the most-recent ad's employer
/// name per org.nr. The personnummer guard (FORK C1 / D8(c)) is applied per row in the projection —
/// a personnummer-shaped org.nr is masked (null) and flagged; the raw value never leaves this
/// handler un-flagged.
///
/// <para>
/// #447 (ADR 0087 D2; senior-cto-advisor 2026-07-01) — each row also carries <c>ActiveAdCount</c>
/// ("X aktiva annonser just nu"): a SECOND bounded in-handler projection over public <c>job_ads</c>
/// keyed by the SAME org.nr set (ADR 0048 in-handler cross-aggregate read), counting only
/// <c>status='Active'</c> — which is the WHOLE exclusion: JobAd has no soft-delete axis and no query
/// filter (#821), so a retracted ad is excluded by its Status, not by a filter. Kept as a separate additive projection (the name projection is unchanged)
/// rather than merged into one GROUP BY — two bounded round-trips over a handful of distinct org.nrs
/// is the accepted cost of a lower-risk additive diff (CTO verdict b). The raw org.nr is read
/// SERVER-SIDE only, to GROUP BY — it is never surfaced (the count is a plain <c>int</c>, public
/// data) nor logged (the org.nr surfacing-guard log-scan covers this handler).
/// </para>
/// </summary>
public sealed class ListCompanyWatchesQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IMatchProfileBuilder profileBuilder,
    IPerUserJobAdSearchQuery perUserSearch,
    IProtectedIdentityTokenizer tokenizer)
    : IQueryHandler<ListCompanyWatchesQuery, IReadOnlyList<CompanyWatchDto>>
{
    // #452 — "matchande annonser" = grade >= Good in the Fast band (parity
    // GetMyMatchCountQueryHandler.HeadlineGrades). Top is not Fast-computable (G3-OPT-A) and is
    // irrelevant to a >= Good COUNT: skills only elevate WITHIN the notifiable band, never lift a
    // Basic across the Good threshold, so the Fast-band >= Good set == the Full-band >= Good set
    // (Fast==Full oracle, ADR 0087 D5-tillägg).
    private static readonly IReadOnlyList<MatchGrade> MatchingGrades =
        [MatchGrade.Good, MatchGrade.Strong];

    public async ValueTask<IReadOnlyList<CompanyWatchDto>> Handle(
        ListCompanyWatchesQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return [];

        var userId = currentUser.UserId.Value;

        var watches = await db.CompanyWatches
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

        if (watches.Count == 0)
            return [];

        // ADR 0087 D2 — resolve company_name at read from public job_ads. org.nr is a STORED
        // generated column exposed as the EF shadow property "OrganizationNumber" (parity with
        // JobAdSearchComposition, PR-2). The employer name per org.nr is enough to identify the watch
        // (display projection, not an invariant — graceful null when no current ad carries it). The
        // name is stable per org.nr (one legal entity = one name), so SELECT DISTINCT (org.nr, name)
        // is pushed server-side — the fetch is bounded to distinct pairs (a handful), never the full
        // ad set for a prolific employer (avoids the §5 unpaginated-fetch smell). string? element type
        // so the EF.Property<string?> Contains translates cleanly (the column is nullable; a NULL
        // org.nr ad never matches via `= ANY(...)`). Values themselves non-null.
        // #544 (ADR 0090 D5) — a watch's stored value is EITHER a plaintext AB org.nr OR an HMAC token
        // for a personnummer-shaped (enskild-firma) org.nr. The job_ads projections below key on the
        // PLAINTEXT org.nr, so resolve each enskild token back to its plaintext via the bounded
        // pnr-shaped active-ad set (the tokeniser is deterministic: HMAC(plaintext) == token). This
        // resolves the name + counts at READ (ADR 0087 D3 / B3 — never a denormalised snapshot); the
        // DTO output is unchanged from the plaintext era, only the at-rest storage differs.
        // IsPersonnummerShaped is the SSOT discriminator (B2): a token → true (length≠10), AB → false.
        var plaintextByEnskildKey = new Dictionary<string, string>(StringComparer.Ordinal);
        if (watches.Any(w => w.OrganizationNumber.IsPersonnummerShaped()))
        {
            // The pnr-shaped job_ads org.nrs (translatable SUPERSET of IsPersonnummerShaped:
            // Length==10 AND 3rd digit 0/1). The IDENTICAL prefilter lives in CompanyWatchScanJob,
            // duplicated verbatim and deliberately NOT single-sourced: 2 call sites is below the §3.6
            // rule-of-three, and single-sourcing the Scan site's OR-disjunct would force an OrElse
            // predicate combinator the repo won't take: LinqKit is off the BUILD.md §3.1 allowlist, and
            // hand-rolling its ExpressionVisitor to dodge that is the same dependency-discipline breach
            // (declined — dotnet-architect + senior-cto-advisor 2026-07-18). Each copy is oracle-pinned
            // INDEPENDENTLY against its OWN SQL statement: THIS List arm by
            // CompanyWatchesTests.GET_list_reports_active_ad_count_even_when_org_number_is_masked (a
            // 3rd-digit-1 sole-prop whose activeAdCount must resolve to 1 — drop the "1" disjunct and it
            // goes red, mutation-verified 2026-07-18), the Scan arm by that job's
            // RunAsync_PnrShapePrefilter_AdmitsBothBoundaryThirdDigits_TheSupersetPin. A too-narrow copy
            // on either side = silent no-match (the cardinal sin). DISTINCT + bounded (enskild-firma
            // employers are rare). STATUS-AGNOSTIC on purpose (parity the name lookup below): a followed
            // company keeps its name whether or not its ads are Active, so the token must still resolve
            // for an archived-only enskild firma; the #447/#452 counts apply their OWN Active gate. HMAC
            // each so an enskild watch token resolves to the public plaintext org.nr. Server-side only —
            // the raw org.nr is never surfaced/logged; the plaintext-key arm covers the backfill window.
            var pnrShapedAdOrgNrs = await db.JobAds
                .AsNoTracking()
                .Where(j => EF.Property<string?>(j, "OrganizationNumber") != null
                            && EF.Property<string?>(j, "OrganizationNumber")!.Length == 10
                            && (EF.Property<string?>(j, "OrganizationNumber")!.Substring(2, 1) == "0"
                                || EF.Property<string?>(j, "OrganizationNumber")!.Substring(2, 1) == "1"))
                .Select(j => EF.Property<string?>(j, "OrganizationNumber"))
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var p in pnrShapedAdOrgNrs)
            {
                if (p is null) continue;
                plaintextByEnskildKey[tokenizer.Tokenize(p)] = p; // token → plaintext (post-backfill)
                plaintextByEnskildKey[p] = p;                     // plaintext → plaintext (legacy window)
            }
        }

        // Resolve each watch to the PLAINTEXT org.nr the projections key on. AB → itself; enskild → the
        // resolved plaintext, or null when no active ad currently carries that employer.
        var resolvedByWatchId = watches.ToDictionary(
            w => w.Id,
            w => w.OrganizationNumber.IsPersonnummerShaped()
                ? plaintextByEnskildKey.GetValueOrDefault(w.OrganizationNumber.Value)
                : w.OrganizationNumber.Value);
        var resolvedPlaintexts = resolvedByWatchId.Values
            .Where(p => p is not null).Select(p => p!).Distinct().ToList();
        var orgNrs = resolvedPlaintexts.Select(o => (string?)o).ToList();

        var nameByOrgNr = (await db.JobAds
                .AsNoTracking()
                .Where(j => orgNrs.Contains(EF.Property<string?>(j, "OrganizationNumber")))
                .Select(j => new { OrgNr = EF.Property<string?>(j, "OrganizationNumber"), Name = j.Company.Name })
                .Distinct()
                .ToListAsync(cancellationToken))
            .Where(x => x.OrgNr is not null)
            .GroupBy(x => x.OrgNr!)
            .ToDictionary(g => g.Key, g => g.First().Name);

        // #447 — active-ad count per followed employer. Same org.nr set, PUBLIC job_ads, but keyed on
        // status='Active' — which is the WHOLE exclusion (JobAd has no soft-delete axis and no query
        // filter, #821; there is no ADR 0048 soft-delete filter here to credit — #864 B4 truth-sync).
        // Repo-wide translation form j.Status == JobAdStatus.Active, value-converted
        // to `status = 'Active'`). GROUP BY the STORED organization_number shadow column server-side.
        // Bounded to the handful of watched org.nrs. Only Postgres
        // computes the generated column + translates this GROUP BY, so the count is proven by the
        // Testcontainers integration test (InMemory hides both).
        var activeAdCountByOrgNr = (await db.JobAds
                .AsNoTracking()
                .Where(j => orgNrs.Contains(EF.Property<string?>(j, "OrganizationNumber"))
                            && j.Status == JobAdStatus.Active)
                .GroupBy(j => EF.Property<string?>(j, "OrganizationNumber"))
                .Select(g => new { OrgNr = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .Where(x => x.OrgNr is not null)
            .ToDictionary(x => x.OrgNr!, x => x.Count);

        // #452 (ADR 0087 D5-tillägg) — "matchande annonser"-count per watched employer, computed at
        // READ by the SAME shared Fast GradeRankExpression /jobb uses (sort==grade coherence,
        // ADR 0079). The company-watch SCAN stays scorer-free (D5) and FollowedCompanyAdHit gains no
        // grade column — the grade is a derived read label only. SSYK-gate (parity
        // GetMyMatchCountQueryHandler): a user who has stated no occupation gets NULL (not-assessed),
        // never a hard 0 — a 0 would read as "no matching ads" when the truth is "state your
        // occupations" (the FE renders that nudge). No cross-user JOIN: the count reads only PUBLIC
        // job_ads + the CURRENT user's own Fast profile (BuildFullForSortAsync is ICurrentUser-scoped),
        // so there is no cross-user surface (ADR 0087 D8 / GDPR). Bounded to the watched org.nr set.
        var profile = await profileBuilder.BuildFullForSortAsync(cancellationToken);
        IReadOnlyDictionary<string, int>? matchingByOrgNr = null;
        if (profile.Fast.SsykGroupConceptIds.Count > 0)
        {
            matchingByOrgNr = await perUserSearch.CountPerUserByEmployerAsync(
                resolvedPlaintexts, profile, MatchingGrades, cancellationToken);
        }

        return watches
            .Select(w =>
            {
                var isProtected = w.OrganizationNumber.IsPersonnummerShaped();
                // #544: the projections key on the resolved PLAINTEXT org.nr (AB → itself; enskild →
                // the token's resolved plaintext, or null when no active ad carries that employer).
                var resolved = resolvedByWatchId[w.Id];
                return new CompanyWatchDto(
                    Id: w.Id.Value,
                    // FORK C1 / D8(c): never surface a personnummer-shaped org.nr (nor its token).
                    OrganizationNumber: isProtected ? null : w.OrganizationNumber.Value,
                    IsProtectedIdentity: isProtected,
                    // Name resolves at READ from public job_ads (ADR 0087 D3 / B3 — no snapshot),
                    // unchanged from the plaintext era via the token→plaintext resolution above.
                    CompanyName: resolved is null ? null : nameByOrgNr.GetValueOrDefault(resolved),
                    FollowedAt: w.CreatedAt,
                    // #447: public open-role count — surfaced even when the org.nr is masked (no PII);
                    // 0 when the employer has no active ad (or none ingested yet).
                    ActiveAdCount: resolved is null ? 0 : activeAdCountByOrgNr.GetValueOrDefault(resolved),
                    // #452: null = not-assessed (no stated occupation); else the >= Good matching
                    // count (0 when this employer has no matching active ad). Surfaced even when the
                    // org.nr is masked (public data, no user-PII).
                    MatchingAdCount: matchingByOrgNr is null
                        ? null
                        : resolved is null ? 0 : matchingByOrgNr.GetValueOrDefault(resolved),
                    // F4b: the per-watch filter, straight off the already-materialised aggregate (no
                    // extra query, no per-watch GET). null = no filter, mirroring the domain's canonical
                    // NULL — never a redundant hasFilter bool beside it. Labels stay FE-side (the picker
                    // already holds the taxonomy tree; a second label authority could only drift).
                    Filter: w.Filter is null
                        ? null
                        : new WatchFilterDto(
                            w.Filter.Municipalities,
                            w.Filter.Regions,
                            w.Filter.OnlyMatched));
            })
            .ToList();
    }
}
