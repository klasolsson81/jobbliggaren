using System.Net;
using System.Text;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.CompanyRegister.Scb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #560 (ADR 0091) — unit tests for the SCB client's request/response plumbing against a FAKE
/// <see cref="HttpMessageHandler"/> (no real cert, no network). Exercises the code-table seeding →
/// count-then-slice → fetch flow and the tolerant wire→<see cref="ScbCompanyRecord"/> mapping. NB:
/// the canned <c>hamtaforetag</c> envelope encodes the ASSUMED response shape; the exact live shape is
/// confirmed at the population run and the mapping (Infrastructure-internal, behind the ACL) adjusted
/// there if needed.
/// </summary>
public class ScbCompanyRegisterClientTests
{
    // Live-verified hamtaforetag envelope: a FLAT object per company. One legal entity (Volvo),
    // advertising-blocked (Reklam 21), active (Företagsstatus 1), one SNI code, seat Stockholm.
    private const string HamtaJson = """
        [
          {
            "PeOrgNr": "165560125790",
            "OrgNr": "5560125790",
            "Företagsnamn": "Volvo AB",
            "Säteskommun, kod": "0180",
            "Säteskommun": "Stockholm",
            "Företagsstatus, kod": "1",
            "Företagsstatus": "Är verksam",
            "Reklam, kod": "21",
            "Juridisk form, kod": "49",
            "Bransch_1, kod": "29100",
            "Bransch_2, kod": "     "
          }
        ]
        """;

    [Fact]
    public async Task StreamLegalEntitiesAsync_SeedsFromCodeTables_FiltersSoleTraders_AndMapsRow()
    {
        var client = BuildClient(new FakeScbHandler());
        var outcome = new ScbSyncOutcome();

        var batches = new List<IReadOnlyList<ScbCompanyRecord>>();
        await foreach (var batch in client.StreamLegalEntitiesAsync(
            outcome, TestContext.Current.CancellationToken))
        {
            batches.Add(batch);
        }

        var record = batches.SelectMany(b => b).ShouldHaveSingleItem();
        record.OrganizationNumber.ShouldBe("5560125790");
        record.Name.ShouldBe("Volvo AB");
        record.SeatMunicipalityCode.ShouldBe("0180");
        record.SeatMunicipalityName.ShouldBe("Stockholm");
        record.SniCodes.ShouldBe(["29100"]);
        record.HasAdvertisingBlock.ShouldBeTrue();     // Reklam 21 → opted out
        record.RawStatusCode.ShouldBe("1");
        outcome.PartitionsFetched.ShouldBe(1);
        outcome.TotalRowsFetched.ShouldBe(1);
        outcome.TruncatedOrErrored.ShouldBeFalse();
    }

    [Fact]
    public async Task StreamLegalEntitiesAsync_MarksTruncated_WhenCodeTablesEmpty()
    {
        var client = BuildClient(new EmptyCodeTableHandler());
        var outcome = new ScbSyncOutcome();

        var any = false;
        await foreach (var _ in client.StreamLegalEntitiesAsync(
            outcome, TestContext.Current.CancellationToken))
        {
            any = true;
        }

        any.ShouldBeFalse();
        outcome.TruncatedOrErrored.ShouldBeTrue();
    }

    [Fact]
    public async Task StreamLegalEntitiesAsync_MarksTruncated_WhenCountEnvelopeUnrecognized()
    {
        // Fail-safe (dotnet-architect Minor): a raknaforetag body we cannot parse must NOT look like a
        // legitimate 0 (which would silently skip the partition and let the sweep deregister it). Code
        // tables seed fine, but the count is an unrecognized shape → the run is marked truncated → the
        // sweep is disabled downstream.
        var client = BuildClient(new UnparseableCountHandler());
        var outcome = new ScbSyncOutcome();

        await foreach (var _ in client.StreamLegalEntitiesAsync(
            outcome, TestContext.Current.CancellationToken))
        {
            // no batches expected (count parsed as 0)
        }

        outcome.TruncatedOrErrored.ShouldBeTrue();
    }

    [Fact]
    public async Task StreamLegalEntitiesAsync_MarksTruncated_AndDoesNotThrow_OnPartitionHttpError()
    {
        // A single SCB partition error (HTTP 400) must NOT crash the ~1-3 h run — the client logs it,
        // marks the run truncated (deregister sweep disabled), and skips the partition. Pins the fix
        // for the live-run crash on the Bransch split.
        var client = BuildClient(new BadRequestCountHandler());
        var outcome = new ScbSyncOutcome();

        await foreach (var _ in client.StreamLegalEntitiesAsync(
            outcome, TestContext.Current.CancellationToken))
        {
        }

        outcome.TruncatedOrErrored.ShouldBeTrue();
    }

    [Fact]
    public async Task StreamLegalEntitiesAsync_DeepSplits_SendsTwoDigitThenNiva3BranschBodies()
    {
        // #628 wire contract: when a partition stays over cap the client drills SätesKommun → Juridisk
        // form → "2-siffrig bransch 1" → "Bransch"/niva 3. Pin the exact live-verified request shapes:
        // the Bransch code table is requested WITH BranschNiva 3; the 2-digit rung sends
        // "2-siffrig bransch 1"; the 5-digit leaf sends "Bransch"+BranschNiva 3 and DROPS the 2-digit.
        var handler = new RecordingDrillHandler();
        var client = BuildClient(handler);
        var outcome = new ScbSyncOutcome();

        await foreach (var _ in client.StreamLegalEntitiesAsync(
            outcome, TestContext.Current.CancellationToken))
        {
        }

        // The niva-3 Bransch code table was requested with BranschNiva 3.
        handler.Requests.ShouldContain(r =>
            r.Path.EndsWith("kodtabell", StringComparison.Ordinal)
            && r.Body.Contains("\"Kategori\":\"Bransch\"", StringComparison.Ordinal)
            && r.Body.Contains("\"BranschNiva\":3", StringComparison.Ordinal));

        // The 2-digit division rung was applied (a filter body carrying "2-siffrig bransch 1").
        handler.Requests.ShouldContain(r =>
            IsFilterEndpoint(r.Path) && r.Body.Contains("2-siffrig bransch 1", StringComparison.Ordinal));

        // The 5-digit leaf carries Bransch + BranschNiva 3 and the 2-digit constraint is stripped.
        handler.Requests.ShouldContain(r =>
            IsFilterEndpoint(r.Path)
            && r.Body.Contains("\"Kategori\":\"Bransch\"", StringComparison.Ordinal)
            && r.Body.Contains("\"BranschNiva\":3", StringComparison.Ordinal)
            && !r.Body.Contains("2-siffrig bransch 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StreamLegalEntitiesAsync_FallsBackToFormOnlyLadder_WhenSniTableEmpty_NotPreemptivelyTruncated()
    {
        // #628 fallback: an empty niva-3 Bransch code table must NOT abort or preemptively truncate the
        // run — the client falls back to the pre-#628 legal-form-only ladder and still bounds partitions.
        // Here every partition fits under cap after the seed, so the run completes cleanly (not truncated).
        var client = BuildClient(new EmptySniTableHandler());
        var outcome = new ScbSyncOutcome();

        var records = new List<ScbCompanyRecord>();
        await foreach (var batch in client.StreamLegalEntitiesAsync(
            outcome, TestContext.Current.CancellationToken))
        {
            records.AddRange(batch);
        }

        records.ShouldNotBeEmpty();                    // form-only ladder still fetches
        outcome.TruncatedOrErrored.ShouldBeFalse();    // empty SNI table alone must not mark truncated
    }

    [Fact]
    public async Task StreamLegalEntitiesAsync_ProtectsOverCapFiveDigitTail_WithoutLatching()
    {
        // #640 (Guard 1): a 5-digit Bransch leaf (0180 × form 49 × 70100) is STILL over cap (2809). The
        // planner emits it over-cap; the client bounds it to the (kommun, SNI) key and records a protected
        // partition — the run stays clean (its sibling 70200 completes the 2-digit parent's count, so the
        // completeness reconciliation passes), so the sweep runs elsewhere and skips only that key.
        var client = BuildClient(new ProtectedTailHandler());
        var outcome = new ScbSyncOutcome();

        await foreach (var _ in client.StreamLegalEntitiesAsync(
            outcome, TestContext.Current.CancellationToken))
        {
        }

        outcome.TruncatedOrErrored.ShouldBeFalse();                 // partition-scoped, not whole-run
        outcome.ReconciliationGaps.ShouldBe(0);
        var protectedPartition = outcome.ProtectedPartitions.ShouldHaveSingleItem();
        protectedPartition.SeatMunicipalityCode.ShouldBe("0180");
        protectedPartition.SniCode.ShouldBe("70100");
        // #717 — the over-cap raknaforetag count (2809) is carried for free tail sizing (one leaf here).
        outcome.ProtectedPartitionSizes[protectedPartition]
            .ShouldBe(new ScbProtectedPartitionSize(OverCapCount: 2809, LeafCount: 1));
    }

    [Fact]
    public async Task StreamLegalEntitiesAsync_LatchesTruncated_WhenOverCapLeafIsNotASingleKommunSniPair()
    {
        // #640 fail-safe: with the SNI table empty the client falls back to the form-only ladder, so an
        // over-cap partition bottoms out at (kommun, form) with NO 5-digit Bransch. That coarse leaf cannot
        // be bounded to a tight (kommun, SNI) key, so the client latches the WHOLE run truncated rather than
        // narrow the sweep unsafely — the extraction-miss IS the whole-run latch. No key is protected.
        var client = BuildClient(new CoarseOverCapHandler());
        var outcome = new ScbSyncOutcome();

        await foreach (var _ in client.StreamLegalEntitiesAsync(
            outcome, TestContext.Current.CancellationToken))
        {
        }

        outcome.TruncatedOrErrored.ShouldBeTrue();
        outcome.ProtectedPartitions.ShouldBeEmpty();
    }

    // ---- #712: end-of-run retry wave ----

    [Fact]
    public async Task StreamLegalEntitiesAsync_RecoversFetchFailure_OnRetryWave_NotTruncated()
    {
        // #712: the seed's fetch (hamtaforetag) fails on the main stream (transient SCB 400) but succeeds
        // on the end-of-run retry wave → its rows flow, the run ends NON-truncated (residual 0), so the
        // deregister sweep may run (the #708 pass criteria).
        var client = BuildClient(new HamtaRecoversOnSecondCallHandler(), FastRetry());
        var outcome = new ScbSyncOutcome();

        var records = new List<ScbCompanyRecord>();
        await foreach (var batch in client.StreamLegalEntitiesAsync(outcome, TestContext.Current.CancellationToken))
            records.AddRange(batch);

        records.ShouldNotBeEmpty();                         // recovered rows flowed
        outcome.PartitionRequestFailures.ShouldBe(0);       // residual reset to 0 after full recovery
        outcome.TruncatedOrErrored.ShouldBeFalse();         // sweep may run
    }

    [Fact]
    public async Task StreamLegalEntitiesAsync_RecoversCountFailure_OnRetryWave_NotTruncated()
    {
        // #712: the seed's count (raknaforetag) fails on the main stream → captured (not dropped as a
        // phantom zero) → re-counts and fetches on the retry wave → non-truncated.
        var client = BuildClient(new RaknaRecoversOnSecondCallHandler(), FastRetry());
        var outcome = new ScbSyncOutcome();

        var records = new List<ScbCompanyRecord>();
        await foreach (var batch in client.StreamLegalEntitiesAsync(outcome, TestContext.Current.CancellationToken))
            records.AddRange(batch);

        records.ShouldNotBeEmpty();
        outcome.PartitionRequestFailures.ShouldBe(0);
        outcome.TruncatedOrErrored.ShouldBeFalse();
    }

    [Fact]
    public async Task StreamLegalEntitiesAsync_PersistentFailure_StaysTruncated_WithResidual_AndEscalates()
    {
        // #712: a partition SCB rejects on every pass stays failed → residual reflects it, the run stays
        // truncated (sweep skipped — never falsely deregister), and the escalation WARN (5705) fires so
        // the residual is never a silent permanent gap.
        var logger = new CapturingLogger<ScbCompanyRegisterClient>();
        var client = BuildClient(new RaknaAlwaysFailsHandler(), FastRetry(), logger);
        var outcome = new ScbSyncOutcome();

        await foreach (var _ in client.StreamLegalEntitiesAsync(outcome, TestContext.Current.CancellationToken))
        {
        }

        outcome.PartitionRequestFailures.ShouldBe(1);       // residual = the one unrecovered partition
        outcome.TruncatedOrErrored.ShouldBeTrue();          // sweep skipped
        logger.Entries.ShouldContain(e => e.EventId == 5705); // escalation WARN — never a silent gap
    }

    [Fact]
    public async Task StreamLegalEntitiesAsync_CountFailedOverCapPartition_ReSplitsToNiva3_OnRetryWave()
    {
        // #712 (the 2026-07-11 probe's 2214 case): a 2-digit partition whose count FAILED mid-run returns
        // an OVER-CAP count on retry — a naive re-fetch would lose its tail. Because it re-enters the
        // planner through its remaining ladder, it re-splits to its niva-3 children (each ≤ cap) which are
        // fetched. The run recovers non-truncated.
        var client = BuildClient(new OverCapReSplitsOnRetryHandler(), FastRetry());
        var outcome = new ScbSyncOutcome();

        var records = new List<ScbCompanyRecord>();
        await foreach (var batch in client.StreamLegalEntitiesAsync(outcome, TestContext.Current.CancellationToken))
            records.AddRange(batch);

        records.Count.ShouldBe(2);                          // both niva-3 children fetched (one row each)
        outcome.PartitionRequestFailures.ShouldBe(0);
        outcome.ReconciliationGaps.ShouldBe(0);             // children sum to the parent → no false SNI gap
        outcome.TruncatedOrErrored.ShouldBeFalse();
    }

    [Fact]
    public async Task StreamLegalEntitiesAsync_GenuineSniGapMaskedByTransientChildFailure_StaysTruncated()
    {
        // #712 (Major-1 regression, the load-bearing safety invariant): a niva-3 (Latch-rung) child count
        // fails transiently mid-run while a GENUINE no-subcode gap exists at its 2-digit parent (the
        // surviving children sum below the parent). The failed child re-queues the PARENT, so the retry
        // wave re-counts every child and RE-RUNS the completeness reconciliation → the genuine gap fires a
        // hard latch → the run STAYS truncated → the deregister sweep is skipped → NO false deregistration.
        // Without the fix, the recovered child would clear the residual and the masked gap would let the
        // sweep run.
        var client = BuildClient(new GenuineGapMaskedByTransientFailureHandler(), FastRetry());
        var outcome = new ScbSyncOutcome();

        await foreach (var _ in client.StreamLegalEntitiesAsync(outcome, TestContext.Current.CancellationToken))
        {
        }

        outcome.ReconciliationGaps.ShouldBe(1);             // the genuine gap fired in the wave
        outcome.PartitionRequestFailures.ShouldBe(0);       // the partition itself recovered...
        outcome.TruncatedOrErrored.ShouldBeTrue();          // ...but the hard-latched gap keeps it truncated
    }

    [Fact]
    public async Task StreamLegalEntitiesAsync_EscalationWarn5705_CarriesTaxonomyCodes_NeverAnOrgNr()
    {
        // #712 (CLAUDE.md §5): the escalation WARN must surface the residual partition descriptors as
        // taxonomy codes (kommun/form/SNI) only — never an org.nr. Pin that the 5705 message contains a
        // kommun code and no 10-digit personnummer/org.nr-shaped run.
        var logger = new CapturingLogger<ScbCompanyRegisterClient>();
        var client = BuildClient(new RaknaAlwaysFailsHandler(), FastRetry(), logger);
        var outcome = new ScbSyncOutcome();

        await foreach (var _ in client.StreamLegalEntitiesAsync(outcome, TestContext.Current.CancellationToken))
        {
        }

        logger.Entries.ShouldContain(e => e.EventId == 5705);
        var escalation = logger.Entries.First(e => e.EventId == 5705);
        escalation.Message.ShouldContain("SätesKommun");                          // taxonomy descriptor present
        System.Text.RegularExpressions.Regex.IsMatch(escalation.Message, @"\b\d{10}\b")
            .ShouldBeFalse();                                                     // never a 10-digit org.nr/pnr
    }

    [Fact]
    public async Task StreamLegalEntitiesAsync_RetryKillSwitchOff_PreservesPreRetryTruncation()
    {
        // #712 kill-switch: with RetryFailedPartitions=false the wave never runs, so a mid-run partition
        // failure latches truncation exactly as before #712 (the residual is never reset).
        var options = Options.Create(new ScbRegisterOptions { BatchSize = 2000, RetryFailedPartitions = false });
        var client = BuildClient(new RaknaAlwaysFailsHandler(), options);
        var outcome = new ScbSyncOutcome();

        await foreach (var _ in client.StreamLegalEntitiesAsync(outcome, TestContext.Current.CancellationToken))
        {
        }

        outcome.PartitionRequestFailures.ShouldBe(1);       // main-stream tally, never reset by a wave
        outcome.TruncatedOrErrored.ShouldBeTrue();
    }

    private static bool IsFilterEndpoint(string path) =>
        path.EndsWith("raknaforetag", StringComparison.Ordinal)
        || path.EndsWith("hamtaforetag", StringComparison.Ordinal);

    // #712 — 1 s is the minimum valid backoff (Range(1, 600)); recovery tests recover on pass 1, so the
    // wall-clock cost is ~1 s (persistent tests run both passes, ~3 s), and the suite runs collections in
    // parallel.
    private static IOptions<ScbRegisterOptions> FastRetry() =>
        Options.Create(new ScbRegisterOptions { BatchSize = 2000, RetryInitialBackoffSeconds = 1, RetryMaxAttempts = 2 });

    private static ScbCompanyRegisterClient BuildClient(
        HttpMessageHandler handler, IOptions<ScbRegisterOptions>? options = null,
        Microsoft.Extensions.Logging.ILogger<ScbCompanyRegisterClient>? logger = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://fake.scb.local/nv0101/v1/sokpavar/"),
        };
        return new ScbCompanyRegisterClient(
            httpClient,
            options ?? Options.Create(new ScbRegisterOptions { BatchSize = 2000 }),
            logger ?? NullLogger<ScbCompanyRegisterClient>.Instance);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class FakeScbHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            // Dispatch by endpoint (path checked before the Juridisk-form body probe, which also
            // appears in rakna/hamta filter bodies). Within kodtabell, distinguish the three code
            // tables the client requests: Bransch (niva 3, 5-digit), Juridisk form, and SätesKommun.
            if (path.EndsWith("kodtabell", StringComparison.Ordinal))
            {
                if (body.Contains("\"Bransch\"", StringComparison.Ordinal))
                    return Json("""{"VardeLista":[{"Varde":"29100"},{"Varde":"70100"},{"Varde":"70200"}]}""");
                return body.Contains("Juridisk form", StringComparison.Ordinal)
                    ? Json("""{"VardeLista":[{"Varde":"10","Text":"Fysiska personer"},{"Varde":"49","Text":"Övriga aktiebolag"}]}""")
                    : Json("""{"VardeLista":[{"Varde":"0180","Text":"Stockholm"}]}""");
            }
            if (path.EndsWith("raknaforetag", StringComparison.Ordinal))
                return Json("2");
            if (path.EndsWith("hamtaforetag", StringComparison.Ordinal))
                return Json(HamtaJson);
            return Json("[]");
        }
    }

    private sealed class EmptyCodeTableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Json("[]"));
    }

    // Code tables seed normally, but raknaforetag returns HTTP 400 (a partition error).
    private sealed class BadRequestCountHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            if (path.EndsWith("kodtabell", StringComparison.Ordinal))
            {
                return body.Contains("Juridisk form", StringComparison.Ordinal)
                    ? Json("""{"VardeLista":[{"Varde":"49"}]}""")
                    : Json("""{"VardeLista":[{"Varde":"0180"}]}""");
            }
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("bad request", Encoding.UTF8, "text/plain"),
            };
        }
    }

    // Code tables seed normally, but raknaforetag returns an object with no recognizable count field.
    private sealed class UnparseableCountHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            if (path.EndsWith("kodtabell", StringComparison.Ordinal))
            {
                return body.Contains("Juridisk form", StringComparison.Ordinal)
                    ? Json("""{"VardeLista":[{"Varde":"49"}]}""")
                    : Json("""{"VardeLista":[{"Varde":"0180"}]}""");
            }
            // Unrecognized count envelope (no bare number, no Antal/Count).
            return Json("""{"unexpected":true}""");
        }
    }

    // Kommun + Juridisk form seed normally, but the Bransch (niva 3) code table comes back empty →
    // the client must fall back to the form-only ladder. Seed counts are ≤ cap so the run is clean.
    private sealed class EmptySniTableHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            if (path.EndsWith("kodtabell", StringComparison.Ordinal))
            {
                if (body.Contains("\"Bransch\"", StringComparison.Ordinal))
                    return Json("""{"VardeLista":[]}"""); // empty SNI table → fallback
                return body.Contains("Juridisk form", StringComparison.Ordinal)
                    ? Json("""{"VardeLista":[{"Varde":"49"}]}""")
                    : Json("""{"VardeLista":[{"Varde":"0180"}]}""");
            }
            if (path.EndsWith("raknaforetag", StringComparison.Ordinal))
                return Json("2"); // under cap → seed yields directly
            if (path.EndsWith("hamtaforetag", StringComparison.Ordinal))
                return Json(HamtaJson);
            return Json("[]");
        }
    }

    // Records every request; forces a full SNI drill by returning over-cap counts for every partition
    // except the 5-digit Bransch leaf. One kommun (0180) × one legal form (49) keeps the tree small.
    private sealed class RecordingDrillHandler : HttpMessageHandler
    {
        public List<(string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((path, body));

            if (path.EndsWith("kodtabell", StringComparison.Ordinal))
            {
                if (body.Contains("\"Bransch\"", StringComparison.Ordinal))
                    return Json("""{"VardeLista":[{"Varde":"70100"},{"Varde":"70200"}]}"""); // one division (70)
                return body.Contains("Juridisk form", StringComparison.Ordinal)
                    ? Json("""{"VardeLista":[{"Varde":"49"}]}""")
                    : Json("""{"VardeLista":[{"Varde":"0180"}]}""");
            }
            if (path.EndsWith("raknaforetag", StringComparison.Ordinal))
                // 5-digit leaf (Bransch, capital B) ≤ cap; everything above (seed/form/2-digit) over cap.
                return body.Contains("\"Bransch\"", StringComparison.Ordinal) ? Json("5") : Json("3000");
            if (path.EndsWith("hamtaforetag", StringComparison.Ordinal))
                return Json(HamtaJson);
            return Json("[]");
        }
    }

    // #640: drills to a 5-digit Bransch leaf that is STILL over cap (70100 = 2809). Its sibling 70200 = 191
    // completes the 2-digit parent's count (2809 + 191 = 3000) so the completeness reconciliation passes —
    // the over-cap 70100 tail is a single (kommun, SNI) pair the client protects without latching.
    private sealed class ProtectedTailHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            if (path.EndsWith("kodtabell", StringComparison.Ordinal))
            {
                if (body.Contains("\"Bransch\"", StringComparison.Ordinal))
                    return Json("""{"VardeLista":[{"Varde":"70100"},{"Varde":"70200"}]}""");
                return body.Contains("Juridisk form", StringComparison.Ordinal)
                    ? Json("""{"VardeLista":[{"Varde":"49"}]}""")
                    : Json("""{"VardeLista":[{"Varde":"0180"}]}""");
            }
            if (path.EndsWith("raknaforetag", StringComparison.Ordinal))
            {
                if (body.Contains("70100", StringComparison.Ordinal))
                    return Json("2809"); // 5-digit tail, still over cap → protected
                if (body.Contains("70200", StringComparison.Ordinal))
                    return Json("191");  // sibling leaf, under cap (2809 + 191 = 3000 = 2-digit parent)
                return Json("3000");     // seed / form / 2-digit division — all over cap
            }
            if (path.EndsWith("hamtaforetag", StringComparison.Ordinal))
                return Json(HamtaJson);
            return Json("[]");
        }
    }

    // #640 fail-safe: empty SNI table → form-only ladder; the form partition stays over cap and bottoms out
    // at (kommun, form) with no 5-digit code, so the client cannot bound a (kommun, SNI) key → whole-run latch.
    private sealed class CoarseOverCapHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            if (path.EndsWith("kodtabell", StringComparison.Ordinal))
            {
                if (body.Contains("\"Bransch\"", StringComparison.Ordinal))
                    return Json("""{"VardeLista":[]}"""); // empty SNI table → form-only fallback
                return body.Contains("Juridisk form", StringComparison.Ordinal)
                    ? Json("""{"VardeLista":[{"Varde":"49"}]}""")
                    : Json("""{"VardeLista":[{"Varde":"0180"}]}""");
            }
            if (path.EndsWith("raknaforetag", StringComparison.Ordinal))
                return Json("3000"); // over cap at every level; form-only ladder cannot slice further
            if (path.EndsWith("hamtaforetag", StringComparison.Ordinal))
                return Json(HamtaJson);
            return Json("[]");
        }
    }

    // ---- #712 helpers ----

    private static HttpResponseMessage BadRequest() =>
        new(HttpStatusCode.BadRequest)
        {
            // The generic SCB "try again" body the 2026-07-11 failures all carried.
            Content = new StringContent(
                """{"Message":"Ett oväntat fel har uppstått. Försök igen eller kontakta SCB om felet kvarstår."}""",
                Encoding.UTF8, "application/json"),
        };

    // Standard single-kommun single-form seeding shared by the #712 handlers.
    private static HttpResponseMessage? SeedCodeTable(string path, string body)
    {
        if (!path.EndsWith("kodtabell", StringComparison.Ordinal))
            return null;
        if (body.Contains("\"Bransch\"", StringComparison.Ordinal))
            return Json("""{"VardeLista":[{"Varde":"70100"},{"Varde":"70200"}]}""");
        return body.Contains("Juridisk form", StringComparison.Ordinal)
            ? Json("""{"VardeLista":[{"Varde":"49"}]}""")
            : Json("""{"VardeLista":[{"Varde":"0180"}]}""");
    }

    // #712: seed count ≤ cap, but the FIRST hamtaforetag (main stream) fails; the retry-wave fetch succeeds.
    private sealed class HamtaRecoversOnSecondCallHandler : HttpMessageHandler
    {
        private int _hamtaCalls;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            if (SeedCodeTable(path, body) is { } seed) return seed;
            if (path.EndsWith("raknaforetag", StringComparison.Ordinal))
                return Json("2"); // seed under cap
            if (path.EndsWith("hamtaforetag", StringComparison.Ordinal))
                return ++_hamtaCalls == 1 ? BadRequest() : Json(HamtaJson);
            return Json("[]");
        }
    }

    // #712: the FIRST raknaforetag (main-stream seed count) fails; the retry-wave re-count succeeds.
    private sealed class RaknaRecoversOnSecondCallHandler : HttpMessageHandler
    {
        private int _raknaCalls;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            if (SeedCodeTable(path, body) is { } seed) return seed;
            if (path.EndsWith("raknaforetag", StringComparison.Ordinal))
                return ++_raknaCalls == 1 ? BadRequest() : Json("2");
            if (path.EndsWith("hamtaforetag", StringComparison.Ordinal))
                return Json(HamtaJson);
            return Json("[]");
        }
    }

    // #712: raknaforetag always fails (persistent) → residual > 0, run stays truncated, 5705 escalates.
    private sealed class RaknaAlwaysFailsHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            if (SeedCodeTable(path, body) is { } seed) return seed;
            if (path.EndsWith("raknaforetag", StringComparison.Ordinal))
                return BadRequest();
            if (path.EndsWith("hamtaforetag", StringComparison.Ordinal))
                return Json(HamtaJson);
            return Json("[]");
        }
    }

    // #712 (the 2214 case): the 2-digit partition's count FAILS mid-run; on retry it returns an over-cap
    // count (2214) and re-splits to its niva-3 children (1500 + 714 = 2214, both ≤ cap), which are fetched.
    private sealed class OverCapReSplitsOnRetryHandler : HttpMessageHandler
    {
        private int _twoDigitRaknaCalls;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            if (SeedCodeTable(path, body) is { } seed) return seed;
            if (path.EndsWith("raknaforetag", StringComparison.Ordinal))
            {
                if (body.Contains("70100", StringComparison.Ordinal)) return Json("1500"); // niva-3 leaf ≤ cap
                if (body.Contains("70200", StringComparison.Ordinal)) return Json("714");  // niva-3 leaf ≤ cap
                if (body.Contains("2-siffrig bransch 1", StringComparison.Ordinal))
                    return ++_twoDigitRaknaCalls == 1 ? BadRequest() : Json("2214"); // main fails, retry over-cap
                return Json("3000"); // seed / form → over cap
            }
            if (path.EndsWith("hamtaforetag", StringComparison.Ordinal))
                return Json(HamtaJson);
            return Json("[]");
        }
    }

    // #712 (Major-1): a niva-3 (Latch-rung) child count fails on the main stream while a GENUINE no-subcode
    // gap exists (survivors 70200=800 sum below the 2-digit parent 2214). On retry 70100 recovers to 1000
    // (1000+800=1800 < 2214), so the wave's re-run of the reconciliation fires a hard-latching SNI gap.
    private sealed class GenuineGapMaskedByTransientFailureHandler : HttpMessageHandler
    {
        private int _niva70100RaknaCalls;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            if (SeedCodeTable(path, body) is { } seed) return seed;
            if (path.EndsWith("raknaforetag", StringComparison.Ordinal))
            {
                // niva-3 leaf 70100: fails on the main stream, recovers to 1000 on the wave.
                if (body.Contains("70100", StringComparison.Ordinal))
                    return ++_niva70100RaknaCalls == 1 ? BadRequest() : Json("1000");
                if (body.Contains("70200", StringComparison.Ordinal))
                    return Json("800"); // survivor niva-3 leaf (1000 + 800 = 1800 < 2214 → genuine gap)
                return Json("2214");    // seed / form / 2-digit division — all over cap, equal (no observe gap)
            }
            if (path.EndsWith("hamtaforetag", StringComparison.Ordinal))
                return Json(HamtaJson);
            return Json("[]");
        }
    }

    // Minimal ILogger spy capturing EventId per log call — for asserting the 5705 escalation fired.
    private sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<(int EventId, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((eventId.Id, formatter(state, exception)));
    }
}
