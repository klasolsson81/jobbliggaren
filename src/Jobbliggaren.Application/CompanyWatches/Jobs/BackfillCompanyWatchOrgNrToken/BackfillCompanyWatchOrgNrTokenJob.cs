using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.CompanyWatches.Jobs.BackfillCompanyWatchOrgNrToken;

/// <summary>
/// #544 (ADR 0090 D5) — the one-off backfill that tokenises existing PLAINTEXT personnummer-shaped
/// <c>company_watches.organization_number</c> values (enskild-firma follows created via #455 before
/// this change) into HMAC tokens at rest. New follows already tokenise at the
/// <c>CompanyWatchFollowExecutor</c> seam; this closes the historical rows.
/// <para>
/// <b>EXECUTION IS KLAS-GATED (STOPP-5, security-auditor B5):</b> the rewrite DESTROYS the plaintext
/// personnummer in place (irreversible — the point; the pepper is permanent/non-rotatable, R1). Run
/// with <c>dryRun: true</c> FIRST and put the count report in front of Klas before the destructive
/// run; never run destructively unprompted.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>App-side, never SQL (B5):</b> the HMAC pepper is a runtime secret — a SQL rewrite would leak it
/// into the migration source / <c>__EFMigrationsHistory</c> / query logs (CLAUDE.md §5, ADR 0072
/// public repo). The token is computed in-process via <see cref="IProtectedIdentityTokenizer"/>.
/// </para>
/// <para>
/// <b>Covers soft-deleted rows (B5):</b> the id stream uses <c>IgnoreQueryFilters</c> — a
/// soft-deleted (unfollowed) enskild row still holds the plaintext personnummer and MUST be
/// tokenised too, else the "strict improvement" claim is false for that subset (residual plaintext
/// pnr at rest).
/// </para>
/// <para>
/// <b>Idempotent by shape:</b> <see cref="CompanyWatch.ApplyOrganizationNumberTokenBackfill"/> is a
/// no-op unless the stored value is a 10-digit personnummer-shaped plaintext (an AB org.nr stays
/// plaintext; an already-tokenised value is a fixed point — length ≠ 10). So a re-run, or the run
/// after a crash, never double-tokenises. No SQL predicate on the shape (<c>substring</c>-in-DB is a
/// second recogniser beside <c>IsPersonnummerShaped</c> — #844, a rule with two normalisers is two
/// rules); the tiny company-watch set is streamed and filtered in memory against the SSOT.
/// </para>
/// <para>
/// <b>No plaintext in a log, ever (§5):</b> LoggerMessages carry counts + opaque ids only.
/// </para>
/// <para>
/// <b>Layer:</b> pure Application — no Hangfire reference (the admin endpoint enqueues this class
/// directly, parity <c>BackfillRecruiterContactScrubJob</c>). One-off; never registered as recurring.
/// </para>
/// </remarks>
public sealed partial class BackfillCompanyWatchOrgNrTokenJob(
    IServiceScopeFactory scopeFactory,
    IAppDbContext db,
    IProtectedIdentityTokenizer tokenizer,
    IDateTimeProvider clock,
    IOptions<BackfillCompanyWatchOrgNrTokenOptions> options,
    ILogger<BackfillCompanyWatchOrgNrTokenJob> logger)
{
    public async Task<CompanyWatchOrgNrTokenBackfillCounts> RunAsync(
        bool dryRun, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var startedAt = clock.UtcNow;
        LogStarted(logger, dryRun, o.MaxItemsPerRun);

        var counts = new CompanyWatchOrgNrTokenBackfillCounts { DryRun = dryRun, StartedAt = startedAt };

        // Stream ids of EVERY watch, including soft-deleted (IgnoreQueryFilters — B5). Deterministic
        // order; never materialize the whole set (parity the extraction/scrub backfills, ADR 0045).
        var idQuery = db.CompanyWatches
            .IgnoreQueryFilters()
            .OrderBy(w => w.Id)
            .Select(w => w.Id.Value)
            .AsAsyncEnumerable();

        var perItemDelay = TimeSpan.FromMilliseconds(o.PerItemDelayMs);

        await foreach (var guid in idQuery.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts.Seen++;

            if (counts.Seen > o.MaxItemsPerRun)
            {
                LogMaxItemsReached(logger, o.MaxItemsPerRun);
                counts.Seen--;
                break;
            }

            var id = new CompanyWatchId(guid);
            try
            {
                await using var itemScope = scopeFactory.CreateAsyncScope();
                var scopedDb = itemScope.ServiceProvider.GetRequiredService<IAppDbContext>();

                var watch = await scopedDb.CompanyWatches
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

                if (watch is null)
                {
                    counts.Skipped++;
                    continue;
                }

                // Compute the token in-process (the pepper never touches SQL). The aggregate method
                // is the SSOT for "only a plaintext pnr converts" — it no-ops on AB / already-token.
                var tokenized = OrganizationNumber.FromTrusted(
                    tokenizer.Tokenize(watch.OrganizationNumber.Value));
                if (!watch.ApplyOrganizationNumberTokenBackfill(tokenized))
                {
                    counts.Skipped++;
                    continue;
                }

                counts.Tokenised++;
                if (watch.DeletedAt.HasValue)
                    counts.SoftDeletedTokenised++;

                if (!dryRun)
                    await scopedDb.SaveChangesAsync(cancellationToken);

                if (counts.Tokenised % o.ProgressLogEvery == 0)
                    LogProgress(logger, counts.Tokenised, counts.Seen, counts.Errors);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                counts.Errors++;
                LogItemFailed(logger, ex, id.Value);
            }

            if (perItemDelay > TimeSpan.Zero)
                await Task.Delay(perItemDelay, cancellationToken);
        }

        var completedAt = clock.UtcNow;
        counts.CompletedAt = completedAt;
        LogCompleted(logger, dryRun, counts.Seen, counts.Tokenised, counts.SoftDeletedTokenised,
            counts.Skipped, counts.Errors, (completedAt - startedAt).TotalSeconds);

        return counts;
    }

    [LoggerMessage(EventId = 6161, Level = LogLevel.Information,
        Message = "BackfillCompanyWatchOrgNrToken: startad — dryRun={DryRun}, maxItemsPerRun={Max}. "
            + "App-sidig HMAC, ingen SQL-peppar. Counts only — aldrig ett org.nr/personnummer i loggen.")]
    private static partial void LogStarted(ILogger logger, bool dryRun, int max);

    [LoggerMessage(EventId = 6162, Level = LogLevel.Information,
        Message = "BackfillCompanyWatchOrgNrToken: progress — tokenised={Tokenised}, seen={Seen}, errors={Errors}.")]
    private static partial void LogProgress(ILogger logger, int tokenised, int seen, int errors);

    [LoggerMessage(EventId = 6163, Level = LogLevel.Warning,
        Message = "BackfillCompanyWatchOrgNrToken: maxItemsPerRun={Max} nått — avbryter; re-enqueue fortsätter (idempotent).")]
    private static partial void LogMaxItemsReached(ILogger logger, int max);

    [LoggerMessage(EventId = 6164, Level = LogLevel.Warning,
        Message = "BackfillCompanyWatchOrgNrToken: watch {CompanyWatchId} misslyckades — fortsätter.")]
    private static partial void LogItemFailed(ILogger logger, Exception exception, Guid companyWatchId);

    [LoggerMessage(EventId = 6165, Level = LogLevel.Warning,
        Message = "BackfillCompanyWatchOrgNrToken: klar — dryRun={DryRun}, seen={Seen}, tokenised={Tokenised}, "
            + "softDeletedTokenised={SoftDeletedTokenised}, skipped={Skipped}, errors={Errors}, {Seconds}s. "
            + "STOPP-5: plaintext-personnummer skrivs över irreversibelt på tokeniserade rader; Klas ser dry-run-deltat FÖRE accept.")]
    private static partial void LogCompleted(ILogger logger, bool dryRun, int seen, int tokenised,
        int softDeletedTokenised, int skipped, int errors, double seconds);
}

/// <summary>
/// The STOPP-5 delta report, in numbers a human reviews. <c>Tokenised</c> = plaintext
/// personnummer-shaped rows converted to an HMAC token (on a dry run: WOULD have been converted);
/// <c>SoftDeletedTokenised</c> = of those, the soft-deleted (unfollowed) rows (B5 coverage witness).
/// </summary>
public sealed class CompanyWatchOrgNrTokenBackfillCounts
{
    public bool DryRun { get; init; }
    public int Seen { get; set; }
    public int Tokenised { get; set; }
    public int SoftDeletedTokenised { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
}
