using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.JobAds.Commands.UpsertExternalJobAd;

/// <summary>
/// Race-säker upsert per ADR 0032 §5 — försök INSERT, vid Postgres UNIQUE-
/// violation (SqlState 23505) reload + UpdateFromSource. Defense-in-depth
/// mot parallella Hangfire-workers + admin-trigger som kallar samma flöde.
///
/// EF-tracking-disciplin: vid catch detachas den misslyckade Added-entiteten
/// så att UnitOfWorkBehavior:s efterföljande SaveChanges inte försöker
/// retry:a samma INSERT. <c>IDbExceptionInspector</c> håller Postgres-
/// specifik 23505-detection i Infrastructure (DIP, Martin 2017 kap. 11).
/// </summary>
public sealed partial class UpsertExternalJobAdCommandHandler(
    IAppDbContext db,
    IDbExceptionInspector dbExceptionInspector,
    IJobAdKeywordExtractor keywordExtractor,
    IDateTimeProvider clock,
    ILogger<UpsertExternalJobAdCommandHandler> logger)
    : ICommandHandler<UpsertExternalJobAdCommand, Result<UpsertOutcome>>
{
    public async ValueTask<Result<UpsertOutcome>> Handle(
        UpsertExternalJobAdCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = command.Item;

        // #874 / F4-4 (ADR 0071/0074 Path C) + F4-4b — the deterministic keyword/skill extraction the
        // aggregate folds into its ingest transitions (Import / UpdateFromSource) as the required
        // JobAdTermExtraction delegate. Reads the public ad text (POST-SCRUB — the aggregate supplies its
        // own scrubbed Title/Description) PLUS the employer-stated structured requirements
        // (item.Requirements). Pure/in-process: local NLP + the committed skill taxonomy + pre-linked
        // requirement concepts, no external call, reads only public ad data (no PII); Worker-sync context
        // → no request-latency budget (ADR 0045). Passing it as a REQUIRED parameter is what makes a text
        // write without re-extraction uncompilable, and the aggregate invoking it over post-scrub text
        // keeps terms deriving from clean text on both the Add and Update paths (F-B, #842 Tier A).
        ExtractedTerms Extract(string title, string description) =>
            keywordExtractor.Extract(new JobAdExtractionInput(title, description, item.Requirements));

        var companyResult = Company.Create(item.CompanyName);
        if (companyResult.IsFailure)
        {
            LogSkippedValidation(logger, command.ExternalId, companyResult.Error.Code);
            return Result.Success(UpsertOutcome.Skipped);
        }

        var extRefResult = ExternalReference.Create(command.Source, command.ExternalId);
        if (extRefResult.IsFailure)
        {
            LogSkippedValidation(logger, command.ExternalId, extRefResult.Error.Code);
            return Result.Success(UpsertOutcome.Skipped);
        }

        var importResult = JobAd.Import(
            item.Title, companyResult.Value, item.Description, item.Url,
            extRefResult.Value, item.SanitizedRawPayload, item.Facets,
            item.DeclaredContacts, item.PublishedAt, item.ExpiresAt, clock, Extract);

        if (importResult.IsFailure)
        {
            LogSkippedValidation(logger, command.ExternalId, importResult.Error.Code);
            return Result.Success(UpsertOutcome.Skipped);
        }

        var newJobAd = importResult.Value;
        db.JobAds.Add(newJobAd);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(UpsertOutcome.Added);
        }
        catch (DbUpdateException ex) when (dbExceptionInspector.IsUniqueConstraintViolation(ex))
        {
            // UNIQUE-violation = annan worker (eller admin-trigger) hann före.
            // Detacha den Added-entitet vi just försökte spara — annars retry:ar
            // UnitOfWorkBehavior samma INSERT vid sin SaveChanges efter handler.
            db.Detach(newJobAd);
            LogUpsertCollision(logger, command.ExternalId);
        }

        // Reload existing — har samma (Source, ExternalId)-tuple per UNIQUE-indexet.
        // Kan teoretiskt redan vara borttagen (admin DELETE eller soft-delete) → skip.
        var source = command.Source;
        var externalId = command.ExternalId;
        var existing = await db.JobAds
            .Where(j => j.External != null
                        && j.External.Source == source
                        && j.External.ExternalId == externalId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            LogExistingMissing(logger, command.ExternalId);
            return Result.Success(UpsertOutcome.Skipped);
        }

        // #841/#842/#874 — the Update path re-writes raw_payload, so it MUST re-write everything derived
        // from the source text: the seven facets (#841), the scrubbed body + declared contacts (#842 Tier
        // A), and the extracted terms (#874). None can be forgotten — JobAd.UpdateFromSource takes the
        // facets, the declared contacts AND the extraction delegate as REQUIRED parameters, so omitting
        // any of them would not compile. That is the whole reason the fix lives in the aggregate's
        // signature rather than a convention here: this handler neither scrubs, merges, nor extracts
        // anything itself — it hands the aggregate the inputs and the aggregate keeps the invariants.
        // (Re-extraction on the Update path is exactly what keeps an updated ad's terms tracking its
        // possibly-changed title/description; applying only on the Add path would leave updated ads with
        // stale terms — now structurally impossible, not a comment we have to trust.)
        var updateResult = existing.UpdateFromSource(
            item.Title, item.Description, item.Url,
            item.SanitizedRawPayload, item.Facets, item.DeclaredContacts, item.ExpiresAt, Extract);

        if (updateResult.IsFailure)
        {
            LogSkippedValidation(logger, command.ExternalId, updateResult.Error.Code);
            return Result.Success(UpsertOutcome.Skipped);
        }

        return Result.Success(UpsertOutcome.Updated);
    }

    [LoggerMessage(EventId = 5101, Level = LogLevel.Information,
        Message = "UpsertExternalJobAd: UNIQUE-collision på ExternalId={ExternalId} — växlar till update.")]
    private static partial void LogUpsertCollision(ILogger logger, string externalId);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Warning,
        Message = "UpsertExternalJobAd: skip ExternalId={ExternalId} domain-validation {ErrorCode}.")]
    private static partial void LogSkippedValidation(ILogger logger, string externalId, string errorCode);

    [LoggerMessage(EventId = 5103, Level = LogLevel.Warning,
        Message = "UpsertExternalJobAd: ExternalId={ExternalId} hade collision men existerar inte längre — race med radering.")]
    private static partial void LogExistingMissing(ILogger logger, string externalId);
}
