using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Matching.Profiles;

/// <summary>
/// The SSOT preference→profile mapper (F4-12/F4-13/F4-15, ADR 0076). Owner-scoped
/// (reads only the current user). NO AI/LLM.
/// <list type="bullet">
/// <item><see cref="BuildFromPreferencesAsync"/> (F4-12/13) — Fast profile from stored
/// <c>MatchPreferences</c> only. No CV, no DEK.</item>
/// <item><see cref="BuildFullFromTopSkillsAsync"/> (F4-15, the global SORT) — Fast +
/// the primary CV's <b>top-5 plaintext</b> <c>Resume.TopSkills</c> (ADR 0058/0059),
/// resolved to concept-ids. <b>No DEK</b> — <c>TopSkills</c> is a plaintext projection
/// column.</item>
/// <item><see cref="BuildFullFromCvSkillsAsync"/> (F4-15, the page-scoped TAG batch) —
/// Fast + the primary CV's <b>complete</b> <c>Content.Skills</c>, resolved to
/// concept-ids. Warms the owner DEK imperatively and reads the encrypted content;
/// <b>fail-closed</b> on a KMS failure (R5-REBIND Option H).</item>
/// </list>
/// It lives in the Application layer (touches only Application abstractions —
/// <see cref="IAppDbContext"/>, <see cref="ICurrentUser"/>, <see cref="ISkillResolver"/>,
/// and on the encrypted path the DEK collaborators) — no Npgsql/EF shadow-column
/// secret crosses it (unlike <c>MatchScorer</c>). Unit-testable without a DB. CV influence
/// on matching begins here (F4-15); the preference path (<see cref="BuildFromPreferencesAsync"/>)
/// stays CV/DEK-free.
/// </summary>
public sealed class MatchProfileBuilder(
    IAppDbContext db,
    ICurrentUser currentUser,
    ISkillResolver skillResolver,
    ICurrentDataOwner currentDataOwner,
    IUserDataKeyStore dataKeyStore)
    : IMatchProfileBuilder
{
    private static readonly CandidateMatchProfile EmptyFast = new(
        Title: string.Empty,
        SsykGroupConceptIds: [],
        PreferredRegionConceptIds: [],
        PreferredEmploymentTypeConceptIds: []);

    private static readonly FullCandidateMatchProfile EmptyFull = new(EmptyFast, []);

    public async ValueTask<CandidateMatchProfile> BuildFromPreferencesAsync(
        CancellationToken cancellationToken)
    {
        var jobSeeker = await LoadJobSeekerAsync(cancellationToken);
        return jobSeeker is null ? EmptyFast : FastFromPreferences(jobSeeker);
    }

    public async ValueTask<FullCandidateMatchProfile> BuildFullFromTopSkillsAsync(
        CancellationToken cancellationToken)
    {
        var jobSeeker = await LoadJobSeekerAsync(cancellationToken);
        if (jobSeeker is null)
            return EmptyFull;

        var fast = FastFromPreferences(jobSeeker);
        if (jobSeeker.PrimaryResumeId is not { } primaryResumeId)
            return new FullCandidateMatchProfile(fast, []);

        // Read ONLY the primary CV's denormalized top-5 plaintext skills (ADR 0058/0059) —
        // no Include(Versions) ⇒ no encrypted ResumeVersion.Content materializes ⇒ NO DEK.
        // The global sort's golden rung is a binary GIN overlap that emits no verdict, so a
        // truncated set can only under-lift, never mis-report (R5-REBIND Option H).
        var resume = await db.Resumes
            .AsNoTracking()
            .Where(r => r.Id == primaryResumeId && r.JobSeekerId == jobSeeker.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var topSkills = resume?.TopSkills ?? [];
        var conceptIds = skillResolver.Resolve(topSkills, cancellationToken);
        return new FullCandidateMatchProfile(fast, conceptIds.ToList());
    }

    public async ValueTask<FullCandidateMatchProfile> BuildFullFromCvSkillsAsync(
        CancellationToken cancellationToken)
    {
        var jobSeeker = await LoadJobSeekerAsync(cancellationToken);
        if (jobSeeker is null)
            return EmptyFull;

        var fast = FastFromPreferences(jobSeeker);
        if (jobSeeker.PrimaryResumeId is not { } primaryResumeId)
            return new FullCandidateMatchProfile(fast, []);

        // The complete skill set is required for the verdict-bearing TAG/modal surface
        // (ScoreConceptCoverage emits NoMatch — not NotAssessed — on a disjoint non-empty
        // set, so a truncated set would mis-report a covered must-have as missing,
        // CLAUDE.md §5). Content is encrypted ⇒ warm the owner DEK imperatively BEFORE the
        // content materializes (parity FieldEncryptionKeyPrefetchBehavior, ADR 0049/TD-13).
        // Fail-closed: a KMS/DEK failure propagates here — it never degrades to an empty
        // skill set (which would be a dishonest NotAssessed). R5-REBIND Option H.
        currentDataOwner.SetOwner(jobSeeker.Id);
        await dataKeyStore.GetOrCreateDataKeyAsync(jobSeeker.Id, cancellationToken);

        var resume = await db.Resumes
            .AsNoTracking()
            .Include(r => r.Versions)
            .Where(r => r.Id == primaryResumeId && r.JobSeekerId == jobSeeker.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (resume is null)
            return new FullCandidateMatchProfile(fast, []);

        // Defensive master lookup (rather than the throwing Resume.MasterVersion): a
        // best-effort match decoration must not fail a page render on a rare integrity edge.
        var master = resume.Versions
            .FirstOrDefault(v => v.Kind == ResumeVersionKind.Master && v.DeletedAt is null);
        var skillNames = master is null
            ? []
            : master.Content.Skills.Select(s => s.Name);

        var conceptIds = skillResolver.Resolve(skillNames, cancellationToken);
        return new FullCandidateMatchProfile(fast, conceptIds.ToList());
    }

    private async ValueTask<JobSeeker?> LoadJobSeekerAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return null;

        // Load the aggregate (parity with GetMyProfileQueryHandler) rather than
        // projecting the value-converted VO directly — avoids EF translation
        // quirks with strongly-typed VOs (memory: ef_strongly_typed_vo_contains).
        return await db.JobSeekers
            .AsNoTracking()
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);
    }

    private static CandidateMatchProfile FastFromPreferences(JobSeeker jobSeeker)
    {
        var preferences = jobSeeker.MatchPreferences;
        return new CandidateMatchProfile(
            Title: string.Empty,
            SsykGroupConceptIds: preferences.PreferredOccupationGroups,
            PreferredRegionConceptIds: preferences.PreferredRegions,
            PreferredEmploymentTypeConceptIds: preferences.PreferredEmploymentTypes);
    }
}
