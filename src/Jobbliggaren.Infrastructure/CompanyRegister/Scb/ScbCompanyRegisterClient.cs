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

    // SCB category names (smoke-verified request shape, issue #560).
    private const string CatSatesKommun = "SätesKommun";
    private const string CatJuridiskForm = "Juridisk form";
    private const string CatBransch = "Bransch";

    // Juridisk form 10 = Fysiska personer (enskild firma / sole traders). Excluding it at the SCB
    // query is the PRIMARY legal-entities-only filter (ADR 0091). Every other code (21..99) is a
    // legal entity.
    private const string SoleTraderJuridiskForm = "10";

    // SNI 2025 avdelningar (top-level sections) A..U — the Bransch niva-1 partition rung for the few
    // (municipality × legal form) partitions that still exceed the row cap (the storstad tail Klas
    // confirmed at smoke-test). A stable, closed reference set (not a CV-engine rubric list) — kept
    // as a constant rather than a code-table round-trip.
    private static readonly string[] SniSections =
        ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U"];

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
        // with SCB (single source of truth) — no hardcoded municipality/legal-form lists.
        var kommunCodes = await GetCodeTableAsync(CatSatesKommun, cancellationToken).ConfigureAwait(false);
        var legalForms = (await GetCodeTableAsync(CatJuridiskForm, cancellationToken).ConfigureAwait(false))
            .Where(code => !string.Equals(code, SoleTraderJuridiskForm, StringComparison.Ordinal))
            .ToArray();

        if (kommunCodes.Count == 0 || legalForms.Length == 0)
        {
            // A missing code table means we cannot bound the extract — treat as truncated so the
            // caller skips the deregister sweep, and yield nothing.
            LogCodeTablesEmpty(logger, kommunCodes.Count, legalForms.Length);
            outcome.MarkTruncatedOrErrored();
            yield break;
        }

        // Seed = one municipality × all legal forms. Ladder narrows an over-cap partition by a single
        // legal form, then by SNI section (avdelning).
        var seeds = kommunCodes
            .Select(code => new ScbQuery([
                new ScbCategoryFilter(CatSatesKommun, [code]),
                new ScbCategoryFilter(CatJuridiskForm, legalForms),
            ]))
            .ToArray();

        var ladder = new ScbFacet[]
        {
            new(CatJuridiskForm, legalForms),
            new(CatBransch, SniSections, BranschNiva: 1),
        };

        await foreach (var leaf in ScbPartitionPlanner.PlanAsync(
            seeds, ladder, options.Value.BatchSize,
            (query, ct) => CountAsync(query, outcome, ct), outcome, cancellationToken)
            .ConfigureAwait(false))
        {
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
        response.EnsureSuccessStatusCode();

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
        response.EnsureSuccessStatusCode();

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

    private async Task<IReadOnlyList<string>> GetCodeTableAsync(
        string category, CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .PostAsJsonAsync(KodtabellEndpoint, new ScbKodtabellRequest { Kategori = category },
                JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument
            .ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var codes = new List<string>();
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            var code = TryGetString(row, "Kod", "Varde", "Värde", "Value");
            if (!string.IsNullOrWhiteSpace(code))
                codes.Add(code);
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

    private static ScbCompanyRecord? MapRow(JsonElement row)
    {
        // OrgNr: SCB gives the 10-digit form directly for legal entities. Fall back to PeOrgNr with the
        // 16-prefix stripped (legal entities are 16 + 10-digit org.nr).
        var orgNr = FirstNonEmpty(
            GetVariable(row, "OrgNr", "Organisationsnummer"),
            StripLegalPeOrgNr(GetVariable(row, "PeOrgNr")));
        var name = FirstNonEmpty(GetVariable(row, "Företagsnamn", "Foretagsnamn", "Namn"));
        if (string.IsNullOrWhiteSpace(orgNr) || string.IsNullOrWhiteSpace(name))
            return null; // an unparseable row is dropped (counted as excluded-invalid downstream)

        var (kommunCode, kommunName) = GetCategory(row, "SätesKommun", "SatesKommun", "Säteskommun");
        var sniCodes = GetCategoryCodes(row, "Bransch");
        var reklamCode = GetCategory(row, "Reklam").Code;
        var status = FirstNonEmpty(
            TryGetString(row, "Företagsstatus", "Foretagsstatus"),
            GetCategory(row, "Företagsstatus", "Foretagsstatus").Code) ?? string.Empty;

        return new ScbCompanyRecord(
            OrganizationNumber: orgNr!.Trim(),
            Name: name!.Trim(),
            SeatMunicipalityCode: kommunCode ?? string.Empty,
            SeatMunicipalityName: kommunName,
            SniCodes: sniCodes,
            HasAdvertisingBlock: IsAdvertisingBlocked(reklamCode),
            RawStatusCode: status);
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

    // Walks the row's "Variabler" array ({Namn, Värde}) for a value by candidate names; also accepts a
    // top-level property of the same name.
    private static string? GetVariable(JsonElement row, params string[] names)
    {
        foreach (var name in names)
        {
            if (row.TryGetProperty(name, out var direct) && direct.ValueKind == JsonValueKind.String)
                return direct.GetString();
        }
        if (TryGetArray(row, out var variabler, "Variabler", "variabler"))
        {
            foreach (var v in variabler.EnumerateArray())
            {
                var vName = TryGetString(v, "Namn", "namn", "Variabel");
                if (vName is not null && names.Contains(vName, StringComparer.OrdinalIgnoreCase))
                    return TryGetString(v, "Värde", "Varde", "Value", "värde");
            }
        }
        return null;
    }

    // Walks the row's "Kategorier" array ({Kategori/Kategori_id, Kod, Klartext}) for the first match.
    private static (string? Code, string? Text) GetCategory(JsonElement row, params string[] names)
    {
        if (TryGetArray(row, out var kategorier, "Kategorier", "kategorier"))
        {
            foreach (var k in kategorier.EnumerateArray())
            {
                var kName = TryGetString(k, "Kategori_id", "Kategori", "kategori", "Namn");
                if (kName is not null && names.Contains(kName, StringComparer.OrdinalIgnoreCase))
                    return (TryGetString(k, "Kod", "Value", "Varde"), TryGetString(k, "Klartext", "Text", "klartext"));
            }
        }
        return (null, null);
    }

    private static List<string> GetCategoryCodes(JsonElement row, params string[] names)
    {
        var codes = new List<string>();
        if (TryGetArray(row, out var kategorier, "Kategorier", "kategorier"))
        {
            foreach (var k in kategorier.EnumerateArray())
            {
                var kName = TryGetString(k, "Kategori_id", "Kategori", "kategori", "Namn");
                if (kName is not null && names.Contains(kName, StringComparer.OrdinalIgnoreCase))
                {
                    var code = TryGetString(k, "Kod", "Value", "Varde");
                    if (!string.IsNullOrWhiteSpace(code))
                        codes.Add(code);
                }
            }
        }
        return codes;
    }

    private static bool TryGetArray(JsonElement element, out JsonElement array, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var candidate) && candidate.ValueKind == JsonValueKind.Array)
            {
                array = candidate;
                return true;
            }
        }
        array = default;
        return false;
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

    [LoggerMessage(EventId = 5701, Level = LogLevel.Error,
        Message = "ScbCompanyRegisterClient: kodtabell tom — kommuner={KommunCount}, legalForms={LegalFormCount}. Avbryter (markerar trunkerad; ingen sweep).")]
    private static partial void LogCodeTablesEmpty(ILogger logger, int kommunCount, int legalFormCount);
}
