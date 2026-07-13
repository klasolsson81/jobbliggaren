using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Sections.Queries.GetCvSectionSuggestions;

/// <summary>
/// Resolves the occupation-driven section suggestions for the OWNING job seeker's parsed CV
/// (Fas 4b 8b.4a, ADR 0107). Mirrors <c>SuggestCvImprovementsQueryHandler</c>: resolve owner from
/// <see cref="ICurrentUser"/>, FirstOrDefault filtered by Id + JobSeekerId, null on not-found OR
/// cross-user (logging the cross-user attempt).
/// <para>
/// The occupation axis is the CONFIRMED one — <c>MatchPreferences.PreferredOccupationGroups</c>,
/// what the user told the match settings — never <c>ParsedResume.OccupationProposals</c>, which is
/// an UNCONFIRMED guess by contract (ADR 0040 Beslut 4) and drops the <c>MatchKind</c> that would
/// let anyone tell an exact hit from a stemmed one. Driving a rule-table off an unconfirmed guess
/// would silently promote "we think you might be a nurse" into "here are a nurse's sections".
/// </para>
/// </summary>
public sealed class GetCvSectionSuggestionsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITaxonomyReadModel taxonomy,
    IBranschgruppProvider branschgruppProvider,
    ICvParsingLexicon lexicon,
    IFailedAccessLogger failedAccessLogger)
    : IQueryHandler<GetCvSectionSuggestionsQuery, CvSectionSuggestionsDto?>
{
    public async ValueTask<CvSectionSuggestionsDto?> Handle(
        GetCvSectionSuggestionsQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return null;

        var jobSeeker = await db.JobSeekers
            .AsNoTracking()
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);

        if (jobSeeker is null)
            return null;

        var parsedResumeId = new ParsedResumeId(query.ParsedResumeId);
        var resume = await db.ParsedResumes
            .AsNoTracking()
            .Where(r => r.Id == parsedResumeId && r.JobSeekerId == jobSeeker.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (resume is null)
        {
            var exists = await db.ParsedResumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == parsedResumeId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "ParsedResume", parsedResumeId.Value, currentUser.UserId.Value, "GetCvSectionSuggestions");
            }
            return null;
        }

        var catalog = branschgruppProvider.GetCatalog();

        // The confirmed axis. Empty = she has not stated an occupation — a legitimate state (a user
        // who skipped onboarding), NOT an error, and NOT the same thing as an occupation that maps
        // to Övriga. The DTO keeps them apart; merging them would ask a user who already answered
        // to answer again.
        var preferredGroups = jobSeeker.MatchPreferences.PreferredOccupationGroups;
        var hasOccupationPreference = preferredGroups.Count > 0;

        var branschgrupp = BranschgruppCatalog.Fallback;
        if (hasOccupationPreference)
        {
            // ssyk-4 group → occupation-field (the asset's key). Total over everything the
            // preference can hold: a stated group that the taxonomy snapshot no longer knows
            // contributes nothing rather than throwing (graceful degradation, parity the port's
            // siblings) — and contributing nothing lands her in Övriga, which is honest.
            var fields = await taxonomy.GetContainingOccupationFieldsAsync(
                [.. preferredGroups], cancellationToken);

            branschgrupp = catalog.ResolveBranschgrupp(fields);
        }

        var rules = catalog.RulesFor(branschgrupp);

        // Handoff rule (a) — the file always wins. A section the user already wrote is never
        // re-offered, whatever synonym she titled it with: the lexicon resolves "Kurser och intyg",
        // "Kurser och certifikat" and "Fortbildning" to the same canonical id, which is exactly the
        // identity PR-1 created. Without it, this rule could not evaluate its own guard and the
        // panel would cheerfully offer her a section she is already looking at.
        var present = resume.Content.Sections
            .Select(section => lexicon.TryResolveFreeSectionId(section.Heading))
            .Where(id => id is not null)
            .ToHashSet(StringComparer.Ordinal);

        var suppressed = rules.SuppressedSectionIds.ToHashSet(StringComparer.Ordinal);

        var suggestions = rules.StandardSections
            .Select(section => (Section: section, IsStandard: true))
            .Concat(rules.SuggestedSections.Select(section => (Section: section, IsStandard: false)))
            .Where(candidate => !present.Contains(candidate.Section.SectionId)
                                && !suppressed.Contains(candidate.Section.SectionId))
            .Select(candidate => new SectionSuggestionDto(
                candidate.Section.SectionId, candidate.Section.Heading, candidate.IsStandard))
            .ToList();

        return new CvSectionSuggestionsDto(
            branschgrupp, hasOccupationPreference, rules.Rationale, suggestions);
    }
}
