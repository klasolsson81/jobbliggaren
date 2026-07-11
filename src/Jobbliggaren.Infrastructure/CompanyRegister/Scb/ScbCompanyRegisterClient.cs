using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.CompanyRegister.Scb;

/// <summary>
/// #712 — the result of a partition fetch (<c>hamtaforetag</c>): the mapped rows plus whether SCB
/// rejected the request. Distinguishes a genuine SCB-rejected fetch (RETRYABLE — the caller re-queues it
/// for the end-of-run wave) from a successfully-fetched but empty/parse-degraded partition. Envelope
/// drift (a non-array body) is NOT a fetch failure — it hard-latches truncation inside FetchAsync and
/// returns an empty, non-failed result (never retryable, since the shape, not transient load, is wrong).
/// </summary>
internal readonly record struct ScbFetchResult(IReadOnlyList<ScbCompanyRecord> Records, bool Failed)
{
    public static ScbFetchResult Ok(IReadOnlyList<ScbCompanyRecord> records) => new(records, false);
    public static ScbFetchResult Failure { get; } = new([], true);
}

/// <summary>
/// #712 — a partition captured for the end-of-run retry wave: the failed <see cref="ScbQuery"/> plus the
/// REMAINING ladder (<c>ladder[depth..]</c>) it must resume from. Re-driving the planner with only the
/// remaining ladder is what lets a count-failed 2-digit partition re-split into its 5-digit children on
/// retry (rather than over-cap-fetching and silently losing the tail) — while never re-widening back over
/// the already-applied rungs.
/// </summary>
internal sealed record ScbRetrySeed(ScbQuery Query, IReadOnlyList<IScbRung> RemainingLadder);

/// <summary>
/// #560 (ADR 0091) — the real SCB population client: implements <see cref="IScbCompanyRegisterSource"/>
/// over SCB's certificate-authenticated <c>sokpavar</c> API (JE / legal-entity endpoints). Drives the
/// adaptive count-then-slice partitioning (<see cref="ScbPartitionPlanner"/>) so every fetch stays
/// within SCB's 2000-row cap, and translates the wire rows into the neutral
/// <see cref="ScbCompanyRecord"/>. The client certificate + the process-wide 10-calls/10-s rate
/// limiter are wired onto the typed <c>HttpClient</c> in <c>AddScbCompanyRegister</c> — this class just
/// issues sequential awaited POSTs (the resilience pipeline paces them).
///
/// <para>
/// <b>Legal-entities-only at the source:</b> every query includes <c>Juridisk form ≠ 10</c> (the legal
/// form codes, sole traders excluded), so personnummer-shaped rows are never fetched. The orchestrator
/// applies an independent <c>IsPersonnummerShaped</c> guard before persisting (defense-in-depth).
/// </para>
///
/// <para>
/// <b>Response parsing is tolerant (<c>JsonElement</c>):</b> the exact <c>hamtaforetag</c> envelope
/// (property casing/nesting of Variabler vs Kategorier) is confirmed against the live API at the
/// population run — the extractor tries the documented field names and degrades to null, never throws
/// on a shape surprise. The org.nr is never logged (CLAUDE.md §5).
/// </para>
/// </summary>
internal sealed partial class ScbCompanyRegisterClient(
    HttpClient httpClient,
    IOptions<ScbRegisterOptions> options,
    ILogger<ScbCompanyRegisterClient> logger) : IScbCompanyRegisterSource
{
    // JE (legal-entity) endpoints, relative to the configured base URL.
    private const string KodtabellEndpoint = "api/je/kodtabell";
    private const string RaknaEndpoint = "api/je/raknaforetag";
    private const string HamtaEndpoint = "api/je/hamtaforetag";

    // SCB category names (smoke-verified request shape, issue #560; SNI split live-verified #628).
    private const string CatSatesKommun = "SätesKommun";
    private const string CatJuridiskForm = "Juridisk form";

    // SNI deep-split categories (#628, live-verified). "2-siffrig bransch 1" filters on the 2-digit
    // division; "Bransch" with BranschNiva 3 filters on the 5-digit detaljgrupp. There is NO
    // "Avdelning" category — the v1 assumption of a "Bransch niva 1 (A-U)" section rung returned
    // HTTP 400 live and was dropped.
    private const string CatTwoDigitBransch = "2-siffrig bransch 1";
    private const string CatBransch = "Bransch";
    private const int BranschNivaFiveDigit = 3;

    // Juridisk form 10 = Fysiska personer (enskild firma / sole traders). Excluding it at the SCB
    // query is the PRIMARY legal-entities-only filter (ADR 0091). Every other code (21..99) is a
    // legal entity.
    private const string SoleTraderJuridiskForm = "10";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async IAsyncEnumerable<IReadOnlyList<ScbCompanyRecord>> StreamLegalEntitiesAsync(
        ScbSyncOutcome outcome,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        // Seeds + ladder are built from the live code tables so the partition dimensions stay in sync
        // with SCB (single source of truth) — no hardcoded municipality/legal-form/SNI lists.
        var kommunCodes = await GetCodeTableAsync(CatSatesKommun, branschNiva: null, cancellationToken).ConfigureAwait(false);
        var legalForms = (await GetCodeTableAsync(CatJuridiskForm, branschNiva: null, cancellationToken).ConfigureAwait(false))
            .Where(code => !string.Equals(code, SoleTraderJuridiskForm, StringComparison.Ordinal))
            .ToArray();

        if (kommunCodes.Count == 0 || legalForms.Length == 0)
        {
            // A missing seed code table means we cannot bound the extract — treat as truncated so the
            // caller skips the deregister sweep, and yield nothing.
            LogCodeTablesEmpty(logger, kommunCodes.Count, legalForms.Length);
            outcome.MarkTruncatedOrErrored();
            yield break;
        }

        // Seed = one municipality × all legal forms.
        var seeds = kommunCodes
            .Select(code => new ScbQuery([
                new ScbCategoryFilter(CatSatesKommun, [code]),
                new ScbCategoryFilter(CatJuridiskForm, legalForms),
            ]))
            .ToArray();

        var ladder = await BuildLadderAsync(legalForms, cancellationToken).ConfigureAwait(false);

        // #712 — partitions SCB rejects mid-run are captured here (query + remaining ladder) and retried
        // end-of-run, instead of latching the whole run truncated on the spot. The queue is filled ONLY
        // during the main stream (the retry wave never re-queues), so the wave terminates.
        var retryQueue = new List<ScbRetrySeed>();

        await foreach (var leaf in ScbPartitionPlanner.PlanAsync(
            seeds, ladder, options.Value.BatchSize,
            (query, ct) => CountAsync(query, outcome, ct), outcome,
            onCountFailed: (query, depth) =>
            {
                // A count-failed partition: tally it (latches truncation until a full retry recovery) and
                // capture it for the end-of-run wave with the ladder it must resume from.
                outcome.RecordPartitionRequestFailed();
                retryQueue.Add(new ScbRetrySeed(query, RemainingLadder(ladder, depth)));
            },
            cancellationToken)
            .ConfigureAwait(false))
        {
            // #640 (Guard 1): the planner bounded this partition as far as the ladder allows but it is
            // STILL over the fetch cap — we will fetch only the first cap rows, so its tail is un-observed.
            // If it reduces to exactly one SätesKommun + one 5-digit Bransch we protect just that
            // (kommun, SNI) key-space and keep sweeping the clean 99%+ (partition-scoped sweep). If it does
            // NOT (a coarser over-cap leaf, e.g. the defensive empty-children path at the 2-digit level),
            // we cannot bound the unfetched tail to a tight key and must disable the WHOLE sweep — a
            // fail-safe: the client's extraction-miss IS the whole-run truncation latch.
            if (leaf.OverCap)
            {
                if (TryExtractProtectedKey(leaf.Query, out var kommunCode, out var sniCode))
                    // #717 — carry the over-cap count (leaf.Count, already taken by raknaforetag) so the run
                    // sizes this tail for free. leaf.Count > cap by construction (over-cap), and the same
                    // (kommun, SNI) may recur across Juridisk forms — the outcome accumulates per key.
                    outcome.RecordProtectedPartition(kommunCode, sniCode, leaf.Count);
                else
                    outcome.MarkTruncatedOrErrored();
            }

            var fetch = await FetchAsync(leaf.Query, outcome, cancellationToken).ConfigureAwait(false);
            if (fetch.Failed)
            {
                // #712: an SCB-rejected fetch is captured for the retry wave (the count already succeeded ≤
                // cap, so resume from leaf.Depth). Tally latches truncation until a full retry recovery.
                outcome.RecordPartitionRequestFailed();
                retryQueue.Add(new ScbRetrySeed(leaf.Query, RemainingLadder(ladder, leaf.Depth)));
                continue;
            }

            // Fail-safe against envelope drift: a partition the planner counted as non-empty that fetched
            // successfully but mapped to zero rows is a parse problem, NOT a legitimately empty slice —
            // treat the run as truncated so the deregister sweep is disabled. Guard on !Failed so a genuine
            // SCB rejection (handled above, retryable) is never conflated with envelope drift (hard latch).
            if (fetch.Records.Count == 0 && leaf.Count > 0)
                outcome.MarkTruncatedOrErrored();

            outcome.RecordFetched(fetch.Records.Count);
            yield return fetch.Records;
        }

        // #712 — end-of-run retry wave: re-drive the planner over the captured failed partitions, yielding
        // recovered batches into the SAME stream (same pnr-filter + batch-upsert path in the orchestrator).
        await foreach (var batch in DrainRetryQueueAsync(retryQueue, outcome, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return batch;
        }
    }

    /// <summary>
    /// #712 — the end-of-run retry wave. Re-drives the planner over each captured failed partition through
    /// its remaining ladder, fetching recovered leaves; a partition still failing after
    /// <see cref="ScbRegisterOptions.RetryMaxAttempts"/> passes stays failed. Sets the POST-retry residual
    /// on the outcome (0 = all recovered → the run's partition-failure truncation clears and the sweep may
    /// run — the #708 pass criteria) and escalates any residual in an aggregated WARN. The wave itself
    /// never re-queues (a failure inside it only flags the seed to retry NEXT pass or land residual), so it
    /// is bounded and terminates. No-op when the kill-switch is off or nothing failed.
    /// </summary>
    private async IAsyncEnumerable<IReadOnlyList<ScbCompanyRecord>> DrainRetryQueueAsync(
        List<ScbRetrySeed> retryQueue, ScbSyncOutcome outcome,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (!opts.RetryFailedPartitions || retryQueue.Count == 0)
            yield break;

        var originalCount = retryQueue.Count;
        var pending = retryQueue;

        for (var attempt = 1; attempt <= opts.RetryMaxAttempts && pending.Count > 0; attempt++)
        {
            // End-of-run backoff, exponential per pass — the wave runs after the heaviest write load, so it
            // can afford to wait out a transient SCB blip (attempt 1 → 30 s, 2 → 60 s, …).
            var backoff = TimeSpan.FromSeconds(
                opts.RetryInitialBackoffSeconds * Math.Pow(2, attempt - 1));
            await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);

            var stillFailing = new List<ScbRetrySeed>(pending.Count);
            foreach (var seed in pending)
            {
                var seedFailed = false;
                await foreach (var leaf in ScbPartitionPlanner.PlanAsync(
                    [seed.Query], seed.RemainingLadder, opts.BatchSize,
                    (query, ct) => CountAsync(query, outcome, ct), outcome,
                    // Retry pass: a count failure only FLAGS the seed to try again next pass — it never
                    // re-queues, so the wave cannot grow unbounded.
                    onCountFailed: (_, _) => seedFailed = true,
                    cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (leaf.OverCap)
                    {
                        if (TryExtractProtectedKey(leaf.Query, out var kommunCode, out var sniCode))
                            outcome.RecordProtectedPartition(kommunCode, sniCode, leaf.Count);
                        else
                            outcome.MarkTruncatedOrErrored();
                    }

                    var fetch = await FetchAsync(leaf.Query, outcome, cancellationToken).ConfigureAwait(false);
                    if (fetch.Failed)
                    {
                        seedFailed = true;
                        continue;
                    }

                    if (fetch.Records.Count == 0 && leaf.Count > 0)
                        outcome.MarkTruncatedOrErrored();

                    outcome.RecordFetched(fetch.Records.Count);
                    yield return fetch.Records;
                }

                if (seedFailed)
                    stillFailing.Add(seed);
            }

            pending = stillFailing;
        }

        // The audit row's FailedPartitionCount now reflects the POST-retry residual (0 if all recovered),
        // not the pre-retry count. A residual of 0 clears the partition-failure cause of truncation so the
        // sweep runs; any hard latch (reconciliation gap, envelope drift) still forces truncation.
        outcome.SetResidualPartitionFailures(pending.Count);
        LogPartitionRetrySummary(logger, originalCount, originalCount - pending.Count, pending.Count, opts.RetryMaxAttempts);
        if (pending.Count > 0)
            LogPartitionRetryExhausted(logger, pending.Count, opts.RetryMaxAttempts, DescribeSeeds(pending));
    }

    /// <summary>#712 — the ladder a captured partition must resume from: <c>ladder[depth..]</c>. Empty when
    /// the partition failed at or past the deepest rung (a retry then re-counts it and, if still over cap,
    /// yields an over-cap leaf → protect-or-latch, exactly as the main stream would).</summary>
    private static IReadOnlyList<IScbRung> RemainingLadder(IReadOnlyList<IScbRung> ladder, int depth) =>
        depth >= ladder.Count ? [] : [.. ladder.Skip(depth)];

    private async Task<ScbCountResult> CountAsync(ScbQuery query, ScbSyncOutcome outcome, CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .PostAsJsonAsync(RaknaEndpoint, ToFilterRequest(query), JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // A single partition query error (e.g. an unexpected SCB 400/500) must NOT crash the whole
            // ~11 h run. Log it WITH the partition descriptor + SCB's validator reason (#708 — the
            // 2026-07-05 run's 40 unattributed 400s were undiagnosable without them; taxonomy codes and
            // the reason text are non-PII, never an org.nr). #712: this method is side-effect-free wrt the
            // failure tally/queue — it returns Failure and the CALLER (onCountFailed) decides whether to
            // tally+queue (main stream) or just flag the seed (retry wave).
            LogPartitionRequestFailed(logger, RaknaEndpoint, (int)response.StatusCode,
                DescribeQuery(query),
                await ReadReasonAsync(response, cancellationToken).ConfigureAwait(false));
            return ScbCountResult.Failure;
        }

        // raknaforetag returns the count as the JSON body — accept a bare number or a small object
        // that wraps it (e.g. {"Antal":n}); take the first integer we find.
        using var doc = await JsonDocument
            .ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var count = ExtractCount(doc.RootElement);
        if (count is null)
        {
            // A count we cannot parse (envelope drift) must NOT look like a legitimate 0 — that would
            // silently skip the partition and let the sweep deregister its companies. Fail safe: hard-latch
            // (NOT a retryable failure — the shape is wrong, not the load) and report as a zero count.
            outcome.MarkTruncatedOrErrored();
            return ScbCountResult.Ok(0);
        }
        return ScbCountResult.Ok(count.Value);
    }

    private async Task<ScbFetchResult> FetchAsync(
        ScbQuery query, ScbSyncOutcome outcome, CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .PostAsJsonAsync(HamtaEndpoint, ToFilterRequest(query), JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Non-fatal (as in CountAsync): log with descriptor + reason (#708). #712: side-effect-free —
            // return Failure and let the caller tally+queue (main) or flag the seed (retry wave).
            LogPartitionRequestFailed(logger, HamtaEndpoint, (int)response.StatusCode,
                DescribeQuery(query),
                await ReadReasonAsync(response, cancellationToken).ConfigureAwait(false));
            return ScbFetchResult.Failure;
        }

        using var doc = await JsonDocument
            .ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            // Expected an array of companies — an unrecognized shape is envelope drift, not an empty
            // partition and not a retryable rejection. Hard-latch (disables the sweep) and return a
            // non-failed empty result so it is never re-queued for retry.
            outcome.MarkTruncatedOrErrored();
            return ScbFetchResult.Ok([]);
        }

        var records = new List<ScbCompanyRecord>(doc.RootElement.GetArrayLength());
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            var record = MapRow(row);
            if (record is not null)
                records.Add(record);
        }
        return ScbFetchResult.Ok(records);
    }

    // Builds the partition ladder from the live SNI code table. Rung 1 = single Juridisk form; rung 2 =
    // 2-digit SNI division ("2-siffrig bransch 1"); rung 3 = 5-digit Bransch (niva 3), fanned by the
    // parent's 2-digit prefix. The 2-digit values and the prefix map are both DERIVED from the single
    // niva-3 code table (SNI 2025 is strictly nested: the first two chars of a 5-digit code ARE its
    // division), so the map is total over the 2-digit values (every division has ≥1 child). If the
    // niva-3 table is unavailable, fall back to the pre-#628 legal-form-only ladder — the run still
    // bounds partitions by form and per-partition ladder-exhaustion latches truncated where genuinely
    // over cap (no preemptive whole-run truncation).
    private async Task<IReadOnlyList<IScbRung>> BuildLadderAsync(
        IReadOnlyList<string> legalForms, CancellationToken cancellationToken)
    {
        var fiveDigit = await GetCodeTableAsync(CatBransch, BranschNivaFiveDigit, cancellationToken).ConfigureAwait(false);

        var prefixMap = fiveDigit
            .Where(code => code.Length >= 2)
            .GroupBy(code => code[..2], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g], StringComparer.Ordinal);

        if (prefixMap.Count == 0)
        {
            // No usable SNI table — bound only by legal form (pre-#628 behaviour), never crash.
            LogSniTableEmpty(logger, fiveDigit.Count);
            return [new ScbStaticRung(CatJuridiskForm, legalForms)];
        }

        var twoDigit = prefixMap.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();

        return
        [
            new ScbStaticRung(CatJuridiskForm, legalForms),
            // #708 — the 2-digit rung reconciles OBSERVE-ONLY: a division whose companies are invisible
            // to the derived value set is counted + logged, never latched, until a completion run has
            // shown the guard's live firing behavior (ADR 0091 amendment 2026-07-06).
            new ScbStaticRung(CatTwoDigitBransch, twoDigit, ReconciliationMode: ScbReconciliationMode.Observe),
            new ScbPrefixRung(CatTwoDigitBransch, CatBransch, BranschNivaFiveDigit, prefixMap),
        ];
    }

    private async Task<IReadOnlyList<string>> GetCodeTableAsync(
        string category, int? branschNiva, CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .PostAsJsonAsync(KodtabellEndpoint,
                new ScbKodtabellRequest { Kategori = category, BranschNiva = branschNiva },
                JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // A code-table failure means we cannot bound the extract — return empty; the caller marks
            // the run truncated (or falls back to the form-only ladder) and never crashes. #708: a
            // DIMENSION-table rejection is a different failure than a partition rejection — its own
            // event (5704) with the category identity, so it is never misread as a partition 400.
            LogCodeTableRequestFailed(logger, category, (int)response.StatusCode,
                await ReadReasonAsync(response, cancellationToken).ConfigureAwait(false));
            return [];
        }

        using var doc = await JsonDocument
            .ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // SCB kodtabell shape (live-verified): an object with a "VardeLista" array of {"Varde","Text"}.
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("VardeLista", out var vardeLista)
            || vardeLista.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var codes = new List<string>();
        foreach (var row in vardeLista.EnumerateArray())
        {
            var code = TryGetString(row, "Varde", "Kod", "Värde", "Value");
            if (!string.IsNullOrWhiteSpace(code))
                codes.Add(code.Trim());
        }
        return codes;
    }

    private static ScbFilterRequest ToFilterRequest(ScbQuery query) =>
        new()
        {
            Kategorier = query.Filters
                .Select(f => new ScbCategoryRequest { Kategori = f.Category, Kod = f.Codes, BranschNiva = f.BranschNiva })
                .ToArray(),
        };

    // #640 (Guard 1) — a STRICT bound: an over-cap leaf can be protected as a partition-scoped (kommun,
    // SNI) key ONLY when it carries exactly ONE SätesKommun code AND exactly ONE 5-digit Bransch (niva 3)
    // code. Any coarser over-cap leaf (2-digit level, seed level, the defensive empty-children path)
    // returns false → the caller latches the whole run truncated (fail-safe: a key we cannot tighten to a
    // single (kommun, SNI) pair must never narrow the sweep's protected set and risk a false deregister).
    private static bool TryExtractProtectedKey(ScbQuery query, out string kommunCode, out string sniCode)
    {
        kommunCode = string.Empty;
        sniCode = string.Empty;

        var kommunFilter = query.Filters.FirstOrDefault(
            f => string.Equals(f.Category, CatSatesKommun, StringComparison.Ordinal));
        var branschFilter = query.Filters.FirstOrDefault(
            f => string.Equals(f.Category, CatBransch, StringComparison.Ordinal)
                && f.BranschNiva == BranschNivaFiveDigit);

        if (kommunFilter is not { Codes.Count: 1 } || branschFilter is not { Codes.Count: 1 })
            return false;

        kommunCode = kommunFilter.Codes[0];
        sniCode = branschFilter.Codes[0];
        return true;
    }

    // --- Tolerant response extraction (verified against the live envelope at the population run) ---

    // Returns null (NOT 0) for an unrecognized envelope, so the caller can fail safe (mark truncated)
    // rather than mistake a parse-miss for a legitimately empty partition.
    private static int? ExtractCount(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var bare))
            return bare;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in (ReadOnlySpan<string>)["Antal", "Count", "antal"])
            {
                if (element.TryGetProperty(name, out var prop) && prop.TryGetInt32(out var wrapped))
                    return wrapped;
            }
        }
        return null;
    }

    // The 5 SNI code fields on a hamtaforetag row (live-verified flat shape). "Bransch_nP, kod" is the
    // dotted variant — we take the plain 5-digit "Bransch_n, kod".
    private static readonly string[] SniCodeKeys =
        ["Bransch_1, kod", "Bransch_2, kod", "Bransch_3, kod", "Bransch_4, kod", "Bransch_5, kod"];

    private static ScbCompanyRecord? MapRow(JsonElement row)
    {
        // hamtaforetag returns a FLAT object per company (live-verified): OrgNr is the 10-digit form
        // directly; fall back to PeOrgNr with the 16-prefix stripped (legal entities are 16 + 10-digit).
        var orgNr = FirstNonEmpty(GetProp(row, "OrgNr"), StripLegalPeOrgNr(GetProp(row, "PeOrgNr")));
        var name = GetProp(row, "Företagsnamn", "Firma");
        if (string.IsNullOrWhiteSpace(orgNr) || string.IsNullOrWhiteSpace(name))
            return null; // an unparseable row is dropped (counted as excluded-invalid downstream)

        var sniCodes = new List<string>(5);
        foreach (var key in SniCodeKeys)
        {
            var code = GetProp(row, key)?.Trim();
            if (!string.IsNullOrWhiteSpace(code)) // blank slots come back as spaces
                sniCodes.Add(code);
        }

        return new ScbCompanyRecord(
            OrganizationNumber: orgNr!.Trim(),
            Name: name!.Trim(),
            SeatMunicipalityCode: GetProp(row, "Säteskommun, kod")?.Trim() ?? string.Empty,
            SeatMunicipalityName: GetProp(row, "Säteskommun")?.Trim(),
            SniCodes: sniCodes,
            HasAdvertisingBlock: IsAdvertisingBlocked(GetProp(row, "Reklam, kod")),
            RawStatusCode: (GetProp(row, "Företagsstatus, kod") ?? string.Empty).Trim());
    }

    // Reklam 21/22/23 = has opted out (reklamspärr); 11/12/13 = accepts. Anything starting with '2'
    // is a block.
    private static bool IsAdvertisingBlocked(string? reklamCode) =>
        !string.IsNullOrEmpty(reklamCode) && reklamCode[0] == '2';

    private static string? StripLegalPeOrgNr(string? peOrgNr)
    {
        if (string.IsNullOrWhiteSpace(peOrgNr))
            return null;
        var digits = peOrgNr.Trim();
        // Legal entities: 16 + 10-digit org.nr. Strip the 16-prefix to our 10-digit key.
        return digits.Length == 12 && digits.StartsWith("16", StringComparison.Ordinal)
            ? digits[2..]
            : digits;
    }

    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    // Reads a flat string property from a hamtaforetag row by candidate names (SCB's real shape — the
    // fields sit directly on the object, e.g. "OrgNr", "Säteskommun, kod").
    private static string? GetProp(JsonElement row, params string[] names)
    {
        foreach (var name in names)
        {
            if (row.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        return null;
    }

    private static string? TryGetString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        return null;
    }

    // --- #708 failure observability (partition descriptor + validator reason) ---

    // Bounds for the failure-log fields. The descriptor's worst case is a seed (1 kommun + ~40 legal
    // forms ≈ 200 chars); the reason is SCB's validator message (one sentence). Both capped defensively.
    private const int MaxDescriptorLength = 512;
    private const int MaxReasonLength = 500;
    // Bounded READ cap (bytes) — the raw error body is never materialized past this during the ~11 h
    // run (security-auditor #708: no unbounded ReadAsStringAsync on a failure path). Comfortably above
    // MaxReasonLength even for multi-byte UTF-8.
    private const int MaxReasonBytes = 2048;

    /// <summary>
    /// #708 — serializes the failing query's filters as <c>Kategori(BranschNiva)=[kod,kod,…]</c> pairs
    /// so a rejected partition is identifiable from the WARN alone (which rung depth, which shape,
    /// which codes). Every value is an SCB taxonomy code (kommun/legal-form/SNI) — NEVER an org.nr
    /// (CLAUDE.md §5); the population channel has no org.nr-valued filter category by construction.
    /// </summary>
    internal static string DescribeQuery(ScbQuery query)
    {
        var descriptor = string.Join("; ", query.Filters.Select(f =>
        {
            var niva = f.BranschNiva is { } n ? $"(niva {n})" : string.Empty;
            return $"{f.Category}{niva}=[{string.Join(",", f.Codes)}]";
        }));
        return descriptor.Length <= MaxDescriptorLength
            ? descriptor
            : descriptor[..MaxDescriptorLength] + "…";
    }

    /// <summary>
    /// #712 — joins the residual retry seeds' descriptors into one bounded, single-line string for the
    /// escalation WARN (5705). Same discipline as <see cref="DescribeQuery"/>/5702: taxonomy codes only
    /// (kommun/form/SNI), never an org.nr, and capped so a large residual can never emit an unbounded line.
    /// </summary>
    internal static string DescribeSeeds(IReadOnlyList<ScbRetrySeed> seeds)
    {
        var joined = string.Join(" | ", seeds.Select(seed => DescribeQuery(seed.Query)));
        return joined.Length <= MaxDescriptorLength ? joined : joined[..MaxDescriptorLength] + "…";
    }

    /// <summary>
    /// #708 — reads a bounded, single-lined slice of a non-success response body: SCB's validator
    /// normally states WHY a query was rejected (the #628 "Avdelning A–U" 400 was diagnosed from
    /// exactly this text). A rakna/hamta/kodtabell rejection body carries no company rows by
    /// construction (the query was refused); the byte-bounded read + control-char/line-separator
    /// strip is belt-and-braces (CLAUDE.md §5; U+2028/U+2029 included — some log viewers treat them
    /// as line breaks). Never throws — a reason must never turn a handled failure into a crash.
    /// </summary>
    internal static async Task<string> ReadReasonAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            // Bounded read: at most MaxReasonBytes + 1 (the extra byte is the truncation probe) —
            // a runaway error page is never materialized whole (security-auditor #708).
            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var raw = new byte[MaxReasonBytes + 1];
            var read = 0;
            int n;
            while (read < raw.Length
                && (n = await stream.ReadAsync(raw.AsMemory(read), cancellationToken).ConfigureAwait(false)) > 0)
            {
                read += n;
            }

            var clipped = read > MaxReasonBytes;
            var body = Encoding.UTF8.GetString(raw, 0, Math.Min(read, MaxReasonBytes));

            var buffer = new StringBuilder(Math.Min(body.Length, MaxReasonLength));
            foreach (var ch in body)
            {
                if (buffer.Length >= MaxReasonLength)
                {
                    clipped = true;
                    break;
                }
                // Neutralize anything a log viewer could render as a line break: control chars
                // (CR/LF/TAB/ESC …) AND the Unicode line/paragraph separators, which char.IsControl
                // misses (code-reviewer + security-auditor #708).
                var breaksLine = char.IsControl(ch) || ch is '\u2028' or '\u2029';
                buffer.Append(breaksLine ? ' ' : ch);
            }

            var reason = buffer.ToString().Trim();
            if (reason.Length == 0)
                return "(tom svarskropp)"; // also covers a body of ONLY control chars/whitespace
            // The ellipsis reflects ACTUAL truncation (byte cap or char cap), not the raw length
            // (dotnet-architect #708 — a trimmed-but-untruncated reason must not claim it was cut).
            return clipped ? reason + "…" : reason;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"(svarskropp oläsbar: {ex.GetType().Name})";
        }
    }

    [LoggerMessage(EventId = 5701, Level = LogLevel.Error,
        Message = "ScbCompanyRegisterClient: kodtabell tom — kommuner={KommunCount}, legalForms={LegalFormCount}. Avbryter (markerar trunkerad; ingen sweep).")]
    private static partial void LogCodeTablesEmpty(ILogger logger, int kommunCount, int legalFormCount);

    [LoggerMessage(EventId = 5702, Level = LogLevel.Warning,
        Message = "ScbCompanyRegisterClient: SCB {Endpoint} svarade {StatusCode} för partition {Partition} — orsak: {Reason}. Partitionen hoppas över, körningen markeras trunkerad (ingen falsk avregistrering). Loggar aldrig org.nr.")]
    private static partial void LogPartitionRequestFailed(
        ILogger logger, string endpoint, int statusCode, string partition, string reason);

    // #708 — kodtabell (dimension-table) rejection: its own event so it is never misread as a
    // partition 400 (different failure class, different identity — a category, not a partition).
    [LoggerMessage(EventId = 5704, Level = LogLevel.Warning,
        Message = "ScbCompanyRegisterClient: SCB kodtabell för kategori {Category} svarade {StatusCode} — orsak: {Reason}. Extraktet kan inte avgränsas (trunkerad eller form-only-fallback). Loggar aldrig org.nr.")]
    private static partial void LogCodeTableRequestFailed(
        ILogger logger, string category, int statusCode, string reason);

    [LoggerMessage(EventId = 5703, Level = LogLevel.Warning,
        Message = "ScbCompanyRegisterClient: SNI-kodtabell (Bransch niva 3) tom ({RowCount} rader) — faller tillbaka till Juridisk form-only-liggaren (djupdelning inaktiverad denna körning).")]
    private static partial void LogSniTableEmpty(ILogger logger, int rowCount);

    // #712 — end-of-run retry wave summary (INFO): how many captured partitions were retried, how many
    // recovered, and the residual. Counts only — the per-failure descriptors are on 5702 (mid-run) and, if
    // any residual remains, 5705 below. Never an org.nr (CLAUDE.md §5).
    [LoggerMessage(EventId = 5706, Level = LogLevel.Information,
        Message = "ScbCompanyRegisterClient: end-of-run retry-våg klar — försökte {Attempted}, återhämtade {Recovered}, residual {Residual} (max {MaxAttempts} pass). Loggar aldrig org.nr.")]
    private static partial void LogPartitionRetrySummary(ILogger logger, int attempted, int recovered, int residual, int maxAttempts);

    // #712 — the "never a silent permanent gap" escalation (WARN): partitions that stayed SCB-rejected
    // after the whole retry wave. The run REMAINS truncated (sweep disabled) and the residual descriptors
    // (kommun/form/SNI codes, bounded, never an org.nr) are surfaced so the residual is diagnosable and can
    // seed a #641/deep-cell follow-up. Fires only when residual > 0 (a fully-recovered wave is silent here).
    [LoggerMessage(EventId = 5705, Level = LogLevel.Warning,
        Message = "ScbCompanyRegisterClient: end-of-run retry-våg uttömd — {StillFailing} partitioner kvarstår SCB-avvisade efter {MaxAttempts} pass. Körningen förblir trunkerad (sweep avstängd, ingen falsk avregistrering). Kvarvarande deskriptorer (kommun/form/SNI): {Descriptors}. Loggar aldrig org.nr.")]
    private static partial void LogPartitionRetryExhausted(ILogger logger, int stillFailing, int maxAttempts, string descriptors);
}
