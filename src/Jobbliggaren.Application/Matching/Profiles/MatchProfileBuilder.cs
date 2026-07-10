using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Matching.Profiles;

/// <summary>
/// The SSOT preference→profile mapper (F4-12/F4-13/F4-15, ADR 0076; ADR 0079 STEG 3 PR-D).
/// Owner-scoped by default — the request paths read only the current user via
/// <see cref="ICurrentUser"/>; the explicit-user-id overload
/// (<see cref="BuildFullForUserIdAsync"/>, ADR 0080 Vag 4) is the deliberate BACKGROUND/SYSTEM
/// seam (no <see cref="ICurrentUser"/>) for the Worker scan — see its remarks. NO AI/LLM,
/// and now NO DEK.
/// <list type="bullet">
/// <item><see cref="BuildFromPreferencesAsync"/> (F4-12/13) — Fast profile from stored
/// <c>MatchPreferences</c> only. No CV.</item>
/// <item><see cref="BuildFullForSortAsync"/> (the global SORT) and
/// <see cref="BuildFullForVerdictAsync"/> (the page-scoped TAG/modal) — Fast + the user's
/// CONFIRMED skill set (<c>MatchPreferences.PreferredSkills</c>, plaintext concept-ids) as
/// <c>CvSkillConceptIds</c>.</item>
/// </list>
/// <para>
/// <b>ADR 0079 Beslut 1 (the reroute):</b> the trusted capability source is the user-confirmed
/// set (CV-proposals ∪ user-edits, stored plaintext on <see cref="MatchPreferences.PreferredSkills"/>),
/// NOT the raw CV skill list. So both Full builders read what the user CONFIRMED — and become
/// identical and DEK-FREE. The former skill paths (top-5 plaintext <c>Resume.TopSkills</c> for
/// the sort; complete encrypted <c>Content.Skills</c> via a warmed owner DEK for the
/// tag/modal — R5-REBIND Option H) are removed: the confirmed set is plaintext on the JobSeeker,
/// so the global SORT and the page TAG/modal read the SAME source. This is decisive:
/// </para>
/// <list type="bullet">
/// <item><b>sort==grade coherence by construction</b> — the SQL golden rung
/// (<c>PerUserJobAdSearchQuery</c>, <c>@cvConceptIds = profile.CvSkillConceptIds</c>) and
/// the scorer (<c>ScoreConceptCoverage</c>) read the same confirmed concept-ids, so a skill the
/// user REMOVED can never lift an ad in one path while the other ignores it (no false lift).</item>
/// <item><b>no truncation/mis-report</b> — the confirmed set is the complete, curated set by
/// definition, so the verdict-bearing tag/modal no longer risks under-reporting a covered
/// must-have (the reason the old tag path needed the full encrypted skills).</item>
/// <item><b>no per-request DEK</b> — unblocks Wave 4 Worker background matching.</item>
/// </list>
/// <para>
/// The two Full overloads stay distinct interface members for their call-sites (sort vs
/// tag/modal) but share one implementation. The confirmed set is independent of a promoted CV:
/// a user who confirmed skills via search-add (no CV) still gets skill matching. An empty
/// confirmed set → <c>CvSkillConceptIds = []</c> → the skill/requirement dimensions report
/// <c>NotAssessed</c> (honest — nothing confirmed yet). The only CV read is the denormalized
/// plaintext <c>LatestRole</c> for the Title dimension (STEG 4, ADR 0058/0059, DEK-free).
/// Lives in the Application layer (touches only <see cref="IAppDbContext"/> +
/// <see cref="ICurrentUser"/>); unit-testable without a DB.
/// </para>
/// </summary>
public sealed class MatchProfileBuilder(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITaxonomyReadModel taxonomy)
    : IMatchProfileBuilder
{
    private static readonly CandidateMatchProfile EmptyFast = new(
        Title: string.Empty,
        SsykGroupConceptIds: [],
        PreferredRegionConceptIds: [],
        PreferredEmploymentTypeConceptIds: [],
        PreferredMunicipalityConceptIds: []);

    private static readonly FullCandidateMatchProfile EmptyFull = new(EmptyFast, []);

    public async ValueTask<CandidateMatchProfile> BuildFromPreferencesAsync(
        CancellationToken cancellationToken, bool includeRelated = false)
    {
        var jobSeeker = await LoadJobSeekerAsync(cancellationToken);
        return jobSeeker is null
            ? EmptyFast
            : await FastFromPreferencesAsync(jobSeeker, includeRelated, cancellationToken);
    }

    // ADR 0079 STEG 3 PR-D — the global SORT path. Reads the confirmed skill set
    // (plaintext, DEK-free). Identical to BuildFullForVerdictAsync (see class remarks):
    // the sort and the tag/modal read the SAME confirmed source, so they can never diverge
    // on a removed/added skill (sort==grade coherent; no false lift).
    public ValueTask<FullCandidateMatchProfile> BuildFullForSortAsync(
        CancellationToken cancellationToken, bool includeRelated = false) =>
        BuildFullAsync(includeRelated, cancellationToken);

    // ADR 0079 STEG 3 PR-D — the page-scoped TAG/modal verdict surface. Reads the confirmed
    // skill set (plaintext, DEK-free). The former DEK-warm + encrypted-content read is gone:
    // the verdict is computed against what the user CONFIRMED (complete by definition), not the
    // encrypted CV skills — no truncation/mis-report risk, no per-request KMS dependency.
    public ValueTask<FullCandidateMatchProfile> BuildFullForVerdictAsync(
        CancellationToken cancellationToken, bool includeRelated = false) =>
        BuildFullAsync(includeRelated, cancellationToken);

    // ADR 0080 Vag 4 PR-2 (Beslut 3) — the background/system variant: loads the JobSeeker by
    // an EXPLICIT user-id (no ICurrentUser) and shares the EXACT same DEK-free build body as
    // the owner-scoped path. OCP extension — the request-scoped overloads are untouched.
    public async ValueTask<FullCandidateMatchProfile> BuildFullForUserIdAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var jobSeeker = await LoadJobSeekerByUserIdAsync(userId, cancellationToken);
        // BACKGROUND/SYSTEM path NEVER broadens (ADR 0084 question D — related occupations are
        // list-only and drive no notifications). There is deliberately NO includeRelated param
        // on this overload: the Worker scan structurally cannot broaden (hardcoded false).
        return await BuildFullCoreAsync(jobSeeker, includeRelated: false, cancellationToken);
    }

    private async ValueTask<FullCandidateMatchProfile> BuildFullAsync(
        bool includeRelated, CancellationToken cancellationToken)
    {
        var jobSeeker = await LoadJobSeekerAsync(cancellationToken);
        return await BuildFullCoreAsync(jobSeeker, includeRelated, cancellationToken);
    }

    // The shared DEK-free build body (one knowledge piece, DRY) — used by both the
    // owner-scoped overloads and the explicit-user-id background variant. The load key is the
    // ONLY thing that differs between the call paths.
    private async ValueTask<FullCandidateMatchProfile> BuildFullCoreAsync(
        JobSeeker? jobSeeker, bool includeRelated, CancellationToken cancellationToken)
    {
        if (jobSeeker is null)
            return EmptyFull;

        var fast = await FastFromPreferencesAsync(jobSeeker, includeRelated, cancellationToken);

        // The CONFIRMED skill set IS the capability source (ADR 0079 Beslut 1) — plaintext
        // concept-ids already on the JobSeeker. No resolve, no CV-skill read, no DEK. Present
        // even when no CV is promoted (a user can confirm skills via search-add).
        var confirmedSkills = jobSeeker.MatchPreferences.PreferredSkills;

        // The ONLY CV read is the denormalized plaintext LatestRole for the Title dimension
        // (STEG 4, ADR 0058/0059 — DEK-free; no Include(Versions) ⇒ no encrypted ResumeVersion
        // materializes). No primary CV → empty Title → honest NotAssessed title.
        Resume? resume = null;
        if (jobSeeker.PrimaryResumeId is { } primaryResumeId)
        {
            resume = await db.Resumes
                .AsNoTracking()
                .Where(r => r.Id == primaryResumeId && r.JobSeekerId == jobSeeker.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new FullCandidateMatchProfile(WithLatestRole(fast, resume), confirmedSkills);
    }

    // STEG 4 (ADR 0079 / #5a; reverses ADR 0076 F4-16 Decision D7=A "title out of scope"):
    // the title dimension reads the primary CV's denormalized plaintext LatestRole
    // (ADR 0058/0059, DEK-free — available on the already-loaded Resume) so it produces a
    // real verdict + evidence instead of a permanent NotAssessed. EVIDENCE-ONLY: Title is
    // absent from MatchGradeCalculator and the PerUserJobAdSearchQuery ORDER BY, so this
    // can never move a grade or a sort position (regression-pinned by the unchanged
    // MatchGradeCalculatorTests + MatchSortOracleTests). The Fast/no-CV path keeps
    // Title = "" → honest NotAssessed (no role to compare).
    private static CandidateMatchProfile WithLatestRole(
        CandidateMatchProfile fast, Resume? resume) =>
        fast with { Title = resume?.LatestRole ?? string.Empty };

    // Owner-scoped load: gate on ICurrentUser, then delegate to the by-id load. No
    // authenticated user → null → the honest empty profile.
    private async ValueTask<JobSeeker?> LoadJobSeekerAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return null;

        return await LoadJobSeekerByUserIdAsync(currentUser.UserId.Value, cancellationToken);
    }

    // ADR 0080 Vag 4 PR-2 — load by an explicit user-id (the background/system path; no
    // ICurrentUser). Load the aggregate (parity with GetMyProfileQueryHandler) rather than
    // projecting the value-converted VO directly — avoids EF translation quirks with
    // strongly-typed VOs (memory: ef_strongly_typed_vo_contains).
    private async ValueTask<JobSeeker?> LoadJobSeekerByUserIdAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        return await db.JobSeekers
            .AsNoTracking()
            .FirstOrDefaultAsync(js => js.UserId == userId, cancellationToken);
    }

    // SPOT injection (ADR 0084 §Architecture point 4, issue #300) — the ONE place the exact
    // ssyk-4 preference set is broadened with its RELATED (substitutable) groups. When
    // <paramref name="includeRelated"/> is true, GetRelatedOccupationGroupsAsync (an in-memory
    // lookup against the already-cached taxonomy snapshot — no per-request DB hit, ADR 0043
    // §1.4) returns the substitutable ssyk-4 groups EXCLUDING the exact ones (already disjoint),
    // filled into the additive RelatedSsykGroupConceptIds init-property. When false the ACL is NOT
    // called and the related set stays empty (exact-only); the live FE include-related toggle (ADR
    // 0084 question A, ?relaterade=on, off by default) is the only thing that flips it true.
    // Broadening in exactly one place keeps sort==grade
    // coherence by construction: the scorer's exact ∪ related gate (PR-2) and (PR-4) the SQL rank
    // read the SAME broadened profile. The BACKGROUND overload passes false (question D, list-only).
    private async ValueTask<CandidateMatchProfile> FastFromPreferencesAsync(
        JobSeeker jobSeeker, bool includeRelated, CancellationToken cancellationToken)
    {
        var preferences = jobSeeker.MatchPreferences;

        // #477 Low 1 — kommun→län-containment. Populated UNCONDITIONALLY (a CORRECTNESS fix,
        // NOT the ?includeRelated-gated broadening below): a kommun-only preference must never
        // RB1-floor a län-only ad in the user's own kommun's län to Basic as a location
        // "contradiction". In-memory ACL lookup (no per-request DB hit, ADR 0043 §1.4); an empty
        // municipality preference → GetContainingRegionsAsync returns [] → the derived set stays
        // empty → pre-#477 behaviour bit-for-bit. The scorer/SQL read it as NotAssessed (neutral),
        // never a Match, so it neither floors nor lifts (honest — a län-only ad does not confirm
        // the user's specific kommun).
        var containmentRegions = await taxonomy.GetContainingRegionsAsync(
            preferences.PreferredMunicipalities, cancellationToken);

        var fast = new CandidateMatchProfile(
            Title: string.Empty,
            SsykGroupConceptIds: preferences.PreferredOccupationGroups,
            PreferredRegionConceptIds: preferences.PreferredRegions,
            PreferredEmploymentTypeConceptIds: preferences.PreferredEmploymentTypes,
            PreferredMunicipalityConceptIds: preferences.PreferredMunicipalities)
        {
            ContainmentRegionConceptIds = containmentRegions,
        };

        if (!includeRelated)
            return fast;

        var related = await taxonomy.GetRelatedOccupationGroupsAsync(
            preferences.PreferredOccupationGroups, cancellationToken);
        return fast with { RelatedSsykGroupConceptIds = related };
    }
}
