using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.JobAds.Commands.SyncPlatsbankenSnapshot;

/// <summary>
/// Orchestrerar snapshot-import: hämtar items från <see cref="IJobSource"/>,
/// jämför mot existerande JobAds via (Source, ExternalId), och splittar i
/// Add (nya) vs UpdateFromSource (befintliga). Aggregat-konstruktion via
/// <see cref="JobAd.Import"/> sker här (Application-handler), inte i
/// Infrastructure — per ADR 0032 §4 + Clean Arch.
/// </summary>
/// <remarks>
/// P8b admin-trigger antar single-caller (synkron från admin-endpoint).
/// Race mot Hangfire-jobben (P8c) löses via UNIQUE-index +
/// DbUpdateException-retry i den separata <c>UpsertExternalJobAdCommand</c>-
/// handlern (P8c leverans). Här prioriterar vi enkel batch-semantik.
/// </remarks>
public sealed class SyncPlatsbankenSnapshotCommandHandler(
    IJobSource jobSource,
    IAppDbContext db,
    IDateTimeProvider clock)
    : ICommandHandler<SyncPlatsbankenSnapshotCommand, Result<SyncPlatsbankenSnapshotResult>>
{
    public async ValueTask<Result<SyncPlatsbankenSnapshotResult>> Handle(
        SyncPlatsbankenSnapshotCommand command, CancellationToken cancellationToken)
    {
        var startedAt = clock.UtcNow;
        var snapshot = await jobSource.FetchSnapshotAsync(cancellationToken);

        if (snapshot.Items.Count == 0)
        {
            return Result.Success(new SyncPlatsbankenSnapshotResult(
                FetchedCount: 0, AddedCount: 0, UpdatedCount: 0, SkippedCount: 0,
                StartedAt: startedAt, CompletedAt: clock.UtcNow));
        }

        var externalIds = snapshot.Items.Select(i => i.ExternalId).ToHashSet(StringComparer.Ordinal);

        // Bulk-fetch existing — single query mot UNIQUE-indexet på
        // (External_Source, External_ExternalId). EF Core översätter till
        // WHERE external_external_id = ANY(@ids) via Npgsql.
        var existing = await db.JobAds
            .Where(j => j.External != null
                        && j.External.Source == jobSource.Source
                        && externalIds.Contains(j.External.ExternalId))
            .ToListAsync(cancellationToken);

        var existingByExternalId = existing.ToDictionary(
            j => j.External!.ExternalId, StringComparer.Ordinal);

        var addedCount = 0;
        var updatedCount = 0;
        var skippedCount = 0;

        foreach (var item in snapshot.Items)
        {
            if (existingByExternalId.TryGetValue(item.ExternalId, out var existingJobAd))
            {
                var updateResult = existingJobAd.UpdateFromSource(
                    item.Title, item.Description, item.Url,
                    item.SanitizedRawPayload, item.ExpiresAt);

                if (updateResult.IsSuccess)
                    updatedCount++;
                else
                    skippedCount++;
                continue;
            }

            var companyResult = Company.Create(item.CompanyName);
            if (companyResult.IsFailure)
            {
                skippedCount++;
                continue;
            }

            var extRefResult = ExternalReference.Create(jobSource.Source, item.ExternalId);
            if (extRefResult.IsFailure)
            {
                skippedCount++;
                continue;
            }

            var importResult = JobAd.Import(
                item.Title, companyResult.Value, item.Description, item.Url,
                extRefResult.Value, item.SanitizedRawPayload,
                item.PublishedAt, item.ExpiresAt, clock);

            if (importResult.IsFailure)
            {
                skippedCount++;
                continue;
            }

            db.JobAds.Add(importResult.Value);
            addedCount++;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new SyncPlatsbankenSnapshotResult(
            FetchedCount: snapshot.Items.Count,
            AddedCount: addedCount,
            UpdatedCount: updatedCount,
            SkippedCount: skippedCount,
            StartedAt: startedAt,
            CompletedAt: clock.UtcNow));
    }
}
