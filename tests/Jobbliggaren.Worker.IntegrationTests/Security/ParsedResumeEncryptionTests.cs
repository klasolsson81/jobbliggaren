using System.Data.Common;
using System.Security.Cryptography;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Security;

/// <summary>
/// F4-8 (ADR 0074 Invariant 3) — the <see cref="ParsedResume"/> staging aggregate's
/// CV-PII field-encryption against REAL Postgres (Testcontainers via
/// <see cref="WorkerTestFixture"/>; InMemory forbidden — the interceptor↔Npgsql
/// materialization order is load-bearing). Mirrors the C4.4 mechanics of
/// <see cref="ResumeContentEncryptionTests"/>: <see cref="ParsedResume.Content"/> is
/// EF-Ignore'd → Form B encrypted shadow <c>parsed_content_enc</c>, and
/// <see cref="ParsedResume.RawText"/> → Form A in-place <c>raw_text</c>. Both require a
/// WARM owner DEK in the write/read scope (encrypt-/decrypt-on-write/read).
/// Non-PII metadata (<c>parse_confidence</c>, <c>personnummer_scan</c>) is plain jsonb.
///
/// <para>SPEC-DRIVEN against the contract surface (ADR 0074), not a line-by-line
/// confirmation of the impl. The <c>v1:</c> ciphertext sentinel + the PII-marker
/// absence on disk are the load-bearing security assertions.</para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class ParsedResumeEncryptionTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private const string ContactNameMarker = "PII-NAMN-CONTACT-7731";
    private const string RawTextMarker = "PII-RÅTEXT-MARKÖR-9914";

    // ── Seeding ──────────────────────────────────────────────────────────

    private async Task<JobSeeker> SeedJobSeekerAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = JobSeeker.Register(
            Guid.NewGuid(), "F4-8 Test", new FixedClock(DateTimeOffset.UtcNow)).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    private static ParsedResumeContent RichContent() =>
        new(
            new ParsedContact(ContactNameMarker, "anna@example.com", "070-1234567", "Stockholm"),
            profile: "Erfaren backend-utvecklare.",
            experience:
            [
                new ParsedExperience(
                    "Backend-utvecklare", "Acme AB", "2021–2024",
                    "Backend-utvecklare, Acme AB, 2021–2024"),
            ],
            education:
            [
                new ParsedEducation("KTH", "Civilingenjör", "2016–2021", "KTH 2016–2021"),
            ],
            skills: ["C#", "PostgreSQL"],
            languages: ["Svenska", "Engelska"]);

    private static ParseConfidence ConfidentConfidence() =>
        ParseConfidence.FromSections(
        [
            new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, ["name extracted"]),
            new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, ["1 entries"]),
        ]);

    private async Task<(ParsedResumeId Id, JobSeekerId Owner)> SeedParsedResumeAsync(
        PersonnummerScanOutcome personnummer, CancellationToken ct)
    {
        var seeker = await SeedJobSeekerAsync(ct);
        var clock = new FixedClock(DateTimeOffset.UtcNow);

        ParsedResumeId id;
        using (var scope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scope, seeker.Id, ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var parsed = ParsedResume.Create(
                seeker.Id,
                "anna-cv.pdf",
                "application/pdf",
                ResumeLanguage.Sv,
                RichContent(),
                rawText: $"Anna Andersson\n{RawTextMarker}\nBackend-utvecklare, Acme AB",
                ConfidentConfidence(),
                personnummer,
                [new ProposedOccupation("q8wL_kdi_WaW", "Systemutvecklare", "Backend-utvecklare")],
                clock).Value;

            id = parsed.Id;
            db.ParsedResumes.Add(parsed);
            await db.SaveChangesAsync(ct);
        }

        return (id, seeker.Id);
    }

    private static async Task PrefetchOwnerDekAsync(
        IServiceScope scope, JobSeekerId owner, CancellationToken ct)
    {
        var dataKeyStore = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
        var currentDataOwner = scope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
        currentDataOwner.SetOwner(owner);
        var dek = await dataKeyStore.GetOrCreateDataKeyAsync(owner, ct);
        CryptographicOperations.ZeroMemory(dek);
    }

    // Raw column read past EF (bypasses the decrypt interceptor) — proves on-disk state.
    private static async Task<string?> RawScalarAsync(
        AppDbContext db, string sql, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
        await using DbCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var raw = await cmd.ExecuteScalarAsync(ct);
        return raw is null or DBNull ? null : raw.ToString();
    }

    private static void ShouldDeepEqual(ParsedResumeContent actual, ParsedResumeContent expected)
    {
        actual.Contact.ShouldBe(expected.Contact); // record value-equality (no collections)
        actual.Profile.ShouldBe(expected.Profile);

        actual.Experience.Count.ShouldBe(expected.Experience.Count);
        for (var i = 0; i < expected.Experience.Count; i++)
            actual.Experience[i].ShouldBe(expected.Experience[i]);

        actual.Education.Count.ShouldBe(expected.Education.Count);
        for (var i = 0; i < expected.Education.Count; i++)
            actual.Education[i].ShouldBe(expected.Education[i]);

        actual.Skills.ShouldBe(expected.Skills);
        actual.Languages.ShouldBe(expected.Languages);
    }

    // ── 1. Round-trip deep equality + RawText (fresh scope, AsNoTracking) ─

    [Fact]
    public async Task RoundTrip_ContentAndRawText_DeepEqualByValue_FreshScope()
    {
        var ct = TestContext.Current.CancellationToken;
        var expectedContent = RichContent();

        var (id, owner) = await SeedParsedResumeAsync(PersonnummerScanOutcome.None, ct);

        using var readScope = _fixture.Services.CreateScope();
        await PrefetchOwnerDekAsync(readScope, owner, ct);
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var parsed = await readDb.ParsedResumes
            .AsNoTracking()
            .SingleAsync(p => p.Id == id, ct);

        parsed.Content.ShouldNotBeNull(
            "Content must materialize via the Form-B read interceptor (decrypt → FromJson)");
        ShouldDeepEqual(parsed.Content, expectedContent);
        parsed.RawText.ShouldContain(RawTextMarker);
    }

    // ── 2. Ciphertext on disk (both PII columns; PII markers absent) ─────

    [Fact]
    public async Task Ciphertext_ContentEncAndRawText_V1Sentinel_NoPiiMarkersOnDisk()
    {
        var ct = TestContext.Current.CancellationToken;

        var (id, _) = await SeedParsedResumeAsync(PersonnummerScanOutcome.None, ct);

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var contentEnc = await RawScalarAsync(
            verifyDb, $"SELECT parsed_content_enc FROM parsed_resumes WHERE id = '{id.Value}'", ct);
        contentEnc.ShouldNotBeNull();
        contentEnc.ShouldStartWith("v1:");
        contentEnc.ShouldNotContain(ContactNameMarker, Case.Sensitive);

        var rawText = await RawScalarAsync(
            verifyDb, $"SELECT raw_text FROM parsed_resumes WHERE id = '{id.Value}'", ct);
        rawText.ShouldNotBeNull();
        rawText.ShouldStartWith("v1:");
        rawText.ShouldNotContain(RawTextMarker, Case.Sensitive);
    }

    // ── 3. Non-PII jsonb metadata persists and re-reads ─────────────────

    [Fact]
    public async Task Metadata_ConfidenceAndPersonnummerScan_PersistAsPlainJsonb_AndRoundTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var flagged = PersonnummerScanOutcome.FromMatches(
            PersonnummerScanner.Scan("Pnr 811218-9876 i CV."));

        var (id, owner) = await SeedParsedResumeAsync(flagged, ct);

        // Plain jsonb on disk (no encryption sentinel) — non-PII metadata.
        using (var verifyScope = _fixture.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pnrJson = await RawScalarAsync(
                verifyDb, $"SELECT personnummer_scan FROM parsed_resumes WHERE id = '{id.Value}'", ct);
            pnrJson.ShouldNotBeNull();
            pnrJson.ShouldNotStartWith("v1:"); // metadata is NOT encrypted
        }

        // Round-trip: Confidence.Overall + personnummer outcome survive.
        using var readScope = _fixture.Services.CreateScope();
        await PrefetchOwnerDekAsync(readScope, owner, ct);
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var parsed = await readDb.ParsedResumes.AsNoTracking().SingleAsync(p => p.Id == id, ct);
        parsed.Confidence.Overall.ShouldBe(OverallConfidenceLevel.Confident);
        parsed.Personnummer.Found.ShouldBeTrue();
        parsed.Personnummer.Count.ShouldBe(1);
        parsed.OccupationProposals.ShouldHaveSingleItem().Label.ShouldBe("Systemutvecklare");
    }

    // ── 4. Fail-closed parity: write without a warm DEK throws ───────────

    [Fact]
    public async Task Write_WithoutWarmDek_FailsClosed_CryptographicException()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        var clock = new FixedClock(DateTimeOffset.UtcNow);

        using var scope = _fixture.Services.CreateScope();
        // Authenticated owner scope but NO prefetched DEK ⇒ the SaveChanges interceptor
        // must fail-closed (CryptographicException) before any plaintext DML.
        var owner = scope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
        owner.SetOwner(seeker.Id);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var parsed = ParsedResume.Create(
            seeker.Id, "cv.pdf", "application/pdf", ResumeLanguage.Sv,
            RichContent(), $"raw {RawTextMarker}", ConfidentConfidence(),
            PersonnummerScanOutcome.None, [], clock).Value;
        db.ParsedResumes.Add(parsed);

        var ex = await Record.ExceptionAsync(async () => await db.SaveChangesAsync(ct));

        ex.ShouldNotBeNull();
        ex.ShouldBeOfType<CryptographicException>(
            "F4-8 CV-PII write without a warm owner DEK must fail-closed (Invariant 3)");

        // Nothing persisted.
        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await RawScalarAsync(
            verifyDb, $"SELECT count(*) FROM parsed_resumes WHERE id = '{parsed.Id.Value}'", ct);
        count.ShouldBe("0");
    }
}
