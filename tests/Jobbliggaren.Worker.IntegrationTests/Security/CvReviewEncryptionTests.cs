using System.Security.Cryptography;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
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
/// Fas 4 STEG 9 (F4-9, ADR 0074 Invariant 3) — the deterministic CV-review engine reads the
/// <see cref="ParsedResume"/>'s CV-PII (encrypted <c>parsed_content_enc</c> Form B +
/// encrypted <c>raw_text</c> Form A) ONLY through the warmed field-decryption pipeline,
/// against REAL Postgres (Testcontainers via <see cref="WorkerTestFixture"/>; InMemory
/// forbidden — the interceptor↔Npgsql materialization order is load-bearing). Mirrors the
/// mechanics of <see cref="ParsedResumeEncryptionTests"/>.
///
/// The three load-bearing security assertions (ADR 0074):
///   1. With a WARM owner DEK in the read scope the engine receives a ParsedResume whose
///      Content + RawText materialize decrypted, and produces a review (round-trip OK).
///   2. WITHOUT a warm DEK the read fails-closed (CryptographicException) — the engine
///      never sees plaintext on an unauthorized/cold scope (CTO #3(iv) not loosened for the
///      review path).
///   3. The review never surfaces CV-PII back into a leaky channel: the verdict evidence
///      cites spans/structure, and the personnummer outcome is read PII-safe (count/kind),
///      never the raw value (Inv.1).
///
/// SPEC-DRIVEN against the contract surface. RED until the engine + ICvReviewEngine ship and
/// the review query/handler is wired.
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class CvReviewEncryptionTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private const string ContactNameMarker = "PII-NAMN-CONTACT-F49-7731";
    private const string RawTextMarker = "PII-RÅTEXT-MARKÖR-F49-9914";

    // ── Seeding ──────────────────────────────────────────────────────────

    private async Task<JobSeeker> SeedJobSeekerAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = JobSeeker.Register(
            Guid.NewGuid(), "F4-9 Review Test", new FixedClock(DateTimeOffset.UtcNow)).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    private static ParsedResumeContent RichContent() =>
        new(
            new ParsedContact(ContactNameMarker, "anna@example.com", "070-1234567", "Stockholm"),
            profile: "Erfaren backend-utvecklare. Levererade 3 plattformsmigrationer 2024.",
            experience:
            [
                new ParsedExperience(
                    "Backend-utvecklare", "Acme AB", "01/2022 – 06/2024",
                    "Ledde teamet om 8 och ökade konverteringen med 23 % 2024."),
            ],
            education: [new ParsedEducation("KTH", "Civilingenjör", "2016–2021", "KTH 2016–2021")],
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
                seeker.Id, "CV_Anna.pdf", "application/pdf", ResumeLanguage.Sv,
                RichContent(),
                rawText: $"Anna Andersson\n{RawTextMarker}\nBackend-utvecklare, Acme AB",
                ConfidentConfidence(), personnummer, [], clock).Value;

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

    // ── 1. WARM DEK → engine reviews the DECRYPTED aggregate ─────────────

    [Fact]
    public async Task ReviewAsync_WithWarmDek_ReadsDecryptedContentAndRawText_ProducesReview()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, owner) = await SeedParsedResumeAsync(PersonnummerScanOutcome.None, ct);

        using var readScope = _fixture.Services.CreateScope();
        await PrefetchOwnerDekAsync(readScope, owner, ct);
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var engine = readScope.ServiceProvider.GetRequiredService<ICvReviewEngine>();

        var parsed = await readDb.ParsedResumes.AsNoTracking().SingleAsync(p => p.Id == id, ct);

        // The decrypt interceptor materialized the CV-PII via the warm DEK.
        parsed.Content.Contact.FullName.ShouldBe(ContactNameMarker);
        parsed.RawText.ShouldContain(RawTextMarker);

        var result = await engine.ReviewAsync(CvReviewContext.FromParsed(parsed), RenderProfile.Ats, ct);

        result.ShouldNotBeNull();
        result.RubricVersion.ToString().ShouldNotBeNullOrWhiteSpace();
        result.Verdicts.ShouldNotBeEmpty(
            "Motorn ska producera verdikt över det dekrypterade CV:t.");
    }

    // ── 2. COLD scope (no warm DEK) → fail-closed ────────────────────────

    [Fact]
    public async Task ReadParsedResume_WithoutWarmDek_FailsClosed_CryptographicException()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, owner) = await SeedParsedResumeAsync(PersonnummerScanOutcome.None, ct);

        // An AUTHED read scope (owner set) but with the DEK deliberately NOT warmed ⇒
        // materializing the encrypted CV-PII shadows must fail-closed before the engine can
        // ever see plaintext (Invariant 3; parity ParsedResumeEncryptionTests fail-closed —
        // the no-owner system scope passes ciphertext through, so the owner is required to
        // exercise the fail-closed path).
        using var coldScope = _fixture.Services.CreateScope();
        coldScope.ServiceProvider.GetRequiredService<ICurrentDataOwner>().SetOwner(owner);
        var coldDb = coldScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ex = await Record.ExceptionAsync(async () =>
            await coldDb.ParsedResumes.AsNoTracking().SingleAsync(p => p.Id == id, ct));

        ex.ShouldNotBeNull();
        ex.ShouldBeOfType<CryptographicException>(
            "F4-9 CV-review read without a warm owner DEK must fail-closed (Invariant 3).");
    }

    // ── 3. Personnummer outcome read PII-safe (count/kind, never raw) ────

    [Fact]
    public async Task ReviewAsync_WhenPersonnummerFlagged_SurfacesPiiSafeOutcome_NotRawValue()
    {
        var ct = TestContext.Current.CancellationToken;
        var flagged = PersonnummerScanOutcome.FromMatches(
            PersonnummerScanner.Scan("Pnr 811218-9876 i CV."));
        var (id, owner) = await SeedParsedResumeAsync(flagged, ct);

        using var readScope = _fixture.Services.CreateScope();
        await PrefetchOwnerDekAsync(readScope, owner, ct);
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var engine = readScope.ServiceProvider.GetRequiredService<ICvReviewEngine>();

        var parsed = await readDb.ParsedResumes.AsNoTracking().SingleAsync(p => p.Id == id, ct);

        // The engine reads the PII-safe outcome (count/found), never the raw value.
        parsed.Personnummer.Found.ShouldBeTrue();

        var result = await engine.ReviewAsync(CvReviewContext.FromParsed(parsed), RenderProfile.Ats, ct);

        // B4 (Personnummer ej angivet) is a critical FAIL when flagged — and it surfaces in
        // CriticalFails citing the count/structure, never the raw personnummer (Inv.1).
        var b4 = result.Verdicts.Single(v => v.CriterionId == "B4");
        b4.Verdict.ShouldBe(CriterionVerdict.Fail);
        result.CriticalFails.ShouldContain(v => v.CriterionId == "B4");

        // No verdict evidence echoes the raw personnummer digits (PII-safe by design).
        foreach (var verdict in result.Verdicts)
        {
            foreach (var evidence in verdict.Evidence)
            {
                if (evidence is TextSpanEvidence span)
                {
                    // Verdict-evidence får aldrig eka råa personnummer (Inv.1).
                    span.Span.Quote.ShouldNotContain("811218-9876", Case.Sensitive);
                }
            }
        }
    }
}
