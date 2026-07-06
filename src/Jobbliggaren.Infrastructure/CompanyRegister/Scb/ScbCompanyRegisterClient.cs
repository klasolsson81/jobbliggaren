using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.CompanyRegister.Scb;

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

        await foreach (var leaf in ScbPartitionPlanner.PlanAsync(
            seeds, ladder, options.Value.BatchSize,
            (query, ct) => CountAsync(query, outcome, ct), outcome, cancellationToken)
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
                    outcome.RecordProtectedPartition(kommunCode, sniCode);
                else
                    outcome.MarkTruncatedOrErrored();
            }

            var records = await FetchAsync(leaf.Query, outcome, cancellationToken).ConfigureAwait(false);

            // Fail-safe against envelope drift: a partition the planner counted as non-empty that
            // fetches/maps to zero rows is a parse problem, NOT a legitimately empty slice — treat the
            // run as truncated so the deregister sweep is disabled (a silent under-count must never let
            // the sweep flag those companies Deregistered). Belt-and-braces with the per-response
            // fail-safes in CountAsync/FetchAsync.
            if (records.Count == 0 && leaf.Count > 0)
                outcome.MarkTruncatedOrErrored();

            outcome.RecordFetched(records.Count);
            yield return records;
        }
    }

    private async Task<int> CountAsync(ScbQuery query, ScbSyncOutcome outcome, CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .PostAsJsonAsync(RaknaEndpoint, ToFilterRequest(query), JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // A single partition query error (e.g. an unexpected SCB 400/500) must NOT crash the whole
            // ~11 h run. Log it WITH the partition descriptor + SCB's validator reason (#708 — the
            // 2026-07-05 run's 40 unattributed 400s were undiagnosable without them; taxonomy codes and
            // the reason text are non-PII, never an org.nr), count it into the audit row, latch the run
            // truncated (deregister sweep disabled — never falsely deregister on incomplete data), and
            // skip this partition.
            LogPartitionRequestFailed(logger, RaknaEndpoint, (int)response.StatusCode,
                DescribeQuery(query),
                await ReadReasonAsync(response, cancellationToken).ConfigureAwait(false));
            outcome.RecordPartitionRequestFailed();
            return 0;
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
            // silently skip the partition and let the sweep deregister its companies. Fail safe.
            outcome.MarkTruncatedOrErrored();
            return 0;
        }
        return count.Value;
    }

    private async Task<IReadOnlyList<ScbCompanyRecord>> FetchAsync(
        ScbQuery query, ScbSyncOutcome outcome, CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .PostAsJsonAsync(HamtaEndpoint, ToFilterRequest(query), JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Non-fatal (as in CountAsync): log with descriptor + reason (#708), count, latch, skip.
            LogPartitionRequestFailed(logger, HamtaEndpoint, (int)response.StatusCode,
                DescribeQuery(query),
                await ReadReasonAsync(response, cancellationToken).ConfigureAwait(false));
            outcome.RecordPartitionRequestFailed();
            return [];
        }

        using var doc = await JsonDocument
            .ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            // Expected an array of companies — an unrecognized shape is envelope drift, not an empty
            // partition. Fail safe (disables the sweep).
            outcome.MarkTruncatedOrErrored();
            return [];
        }

        var records = new List<ScbCompanyRecord>(doc.RootElement.GetArrayLength());
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            var record = MapRow(row);
            if (record is not null)
                records.Add(record);
        }
        return records;
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
    /// #708 — reads a bounded, single-lined slice of a non-success response body: SCB's validator
    /// normally states WHY a query was rejected (the #628 "Avdelning A–U" 400 was diagnosed from
    /// exactly this text). A rakna/hamta/kodtabell rejection body carries no company rows by
    /// construction (the query was refused); the cap + control-char strip is belt-and-braces
    /// (CLAUDE.md §5). Never throws — a reason must never turn a handled failure into a crash.
    /// </summary>
    internal static async Task<string> ReadReasonAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
                return "(tom svarskropp)";

            var buffer = new System.Text.StringBuilder(Math.Min(body.Length, MaxReasonLength));
            foreach (var ch in body)
            {
                if (buffer.Length >= MaxReasonLength)
                    break;
                buffer.Append(char.IsControl(ch) ? ' ' : ch);
            }
            var reason = buffer.ToString().Trim();
            return body.Length > MaxReasonLength ? reason + "…" : reason;
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
}
