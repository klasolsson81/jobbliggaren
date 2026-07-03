using System.Security.Cryptography;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
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
/// Fas 4 STEG 10 (F4-10, ADR 0074 Invariant 1/3) Phase A — the deterministic CV-improve
/// engine reads the <see cref="ParsedResume"/>'s CV-PII (encrypted <c>parsed_content_enc</c>
/// Form B + encrypted <c>raw_text</c> Form A) ONLY through the warmed field-decryption
/// pipeline, against REAL Postgres (Testcontainers via <see cref="WorkerTestFixture"/>;
/// InMemory forbidden — the interceptor↔Npgsql materialization order is load-bearing).
/// Mirrors <see cref="CvReviewEncryptionTests"/>.
///
/// The four load-bearing security assertions (ADR 0074):
///   1. With a WARM owner DEK the engine receives a ParsedResume whose Content + RawText
///      materialize decrypted, and proposes changes over the known cliché/weak-verb markers
///      (round-trip OK).
///   2. WITHOUT a warm DEK the read fails-closed (CryptographicException) — the engine never
///      sees plaintext on an unauthorized/cold scope (Invariant 3, not loosened for improve).
///   3. The personnummer change cites the count/structure only — NEVER the raw value or an
///      offset (Inv.1); and every proposed-change text quotes only spans the user actually
///      wrote (no synthesised prose, §5).
///   4. No PII marker leaks into a leaky channel — the engine has no logger by design, so the
///      surface area for a PII leak is the proposed-change text + evidence, which (3) guards.
///
/// SPEC-DRIVEN against the contract surface. RED until the engine + ICvImprovementEngine ship
/// and AddCvImprovement() is wired into the fixture.
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class CvImprovementEncryptionTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private const string ContactNameMarker = "PII-NAMN-CONTACT-F410-5521";
    private const string RawTextMarker = "PII-RÅTEXT-MARKÖR-F410-8842";

    // A real cliché phrase + a real weak verb (from the committed KB) seeded into the
    // CV so the engine has something concrete to propose against the decrypted aggregate.
    private const string SeededCliche = "Driven lagspelare";
    private const string SeededWeakVerb = "Var ansvarig för";
    private const string RawPersonnummer = "811218-9876";

    // ── Seeding ──────────────────────────────────────────────────────────

    private async Task<JobSeeker> SeedJobSeekerAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = JobSeeker.Register(
            Guid.NewGuid(), "F4-10 Improve Test", new FixedClock(DateTimeOffset.UtcNow)).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    private static ParsedResumeContent RichContent() =>
        new(
            new ParsedContact(ContactNameMarker, "anna@example.com", "070-1234567", "Stockholm"),
            profile: $"{SeededCliche}. Levererade 3 plattformsmigrationer 2024.",
            experience:
            [
                new ParsedExperience(
                    "Backend-utvecklare", "Acme AB", "01/2022 – 06/2024",
                    $"{SeededWeakVerb} ett område utan tydligt resultat."),
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
                rawText: $"Anna Andersson\n{RawTextMarker}\n{SeededWeakVerb} ett område.",
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

    // ── 1. WARM DEK → engine proposes over the DECRYPTED aggregate ───────

    [Fact]
    public async Task SuggestAsync_WithWarmDek_ReadsDecryptedContentAndRawText_ProposesChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, owner) = await SeedParsedResumeAsync(PersonnummerScanOutcome.None, ct);

        using var readScope = _fixture.Services.CreateScope();
        await PrefetchOwnerDekAsync(readScope, owner, ct);
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var engine = readScope.ServiceProvider.GetRequiredService<ICvImprovementEngine>();

        var parsed = await readDb.ParsedResumes.AsNoTracking().SingleAsync(p => p.Id == id, ct);

        // The decrypt interceptor materialized the CV-PII via the warm DEK.
        parsed.Content.Contact.FullName.ShouldBe(ContactNameMarker);
        parsed.RawText.ShouldContain(RawTextMarker);

        var result = await engine.SuggestAsync(parsed, review: null, RenderProfile.Ats, ct);

        result.ShouldNotBeNull();
        result.RubricVersion.ToString().ShouldNotBeNullOrWhiteSpace();
        result.Changes.ShouldNotBeEmpty(
            "Motorn ska föreslå ändringar över det dekrypterade CV:t (drop-in-säkert svagt verb).");
        // #495: the seeded cliché "Driven lagspelare" has no genuine drop-in in cliche-list.v2.json,
        // so it is flagged by A7 but never rewritten — the drop-in-safe weak verb "Var ansvarig för"
        // is what proves the engine proposed over the DECRYPTED aggregate (the point of this test).
        result.Changes.ShouldContain(c => c.Kind == ProposedChangeKind.WeakVerbUpgrade);
    }

    // ── 2. COLD scope (no warm DEK) → fail-closed ────────────────────────

    [Fact]
    public async Task ReadParsedResume_WithoutWarmDek_FailsClosed_CryptographicException()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, owner) = await SeedParsedResumeAsync(PersonnummerScanOutcome.None, ct);

        // An AUTHED read scope (owner set) but with the DEK deliberately NOT warmed ⇒
        // materializing the encrypted CV-PII shadows must fail-closed before the engine can
        // ever see plaintext (Invariant 3; parity CvReviewEncryptionTests — the no-owner
        // system scope passes ciphertext through, so the owner is required to exercise the
        // fail-closed path).
        using var coldScope = _fixture.Services.CreateScope();
        coldScope.ServiceProvider.GetRequiredService<ICurrentDataOwner>().SetOwner(owner);
        var coldDb = coldScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ex = await Record.ExceptionAsync(async () =>
            await coldDb.ParsedResumes.AsNoTracking().SingleAsync(p => p.Id == id, ct));

        ex.ShouldNotBeNull();
        ex.ShouldBeOfType<CryptographicException>(
            "F4-10 CV-improve read without a warm owner DEK must fail-closed (Invariant 3).");
    }

    // ── 3. Personnummer strip cites count only; no proposed text echoes PII ──

    [Fact]
    public async Task SuggestAsync_WhenPersonnummerFlagged_StripCitesCountOnly_NoRawValueLeak()
    {
        var ct = TestContext.Current.CancellationToken;
        var flagged = PersonnummerScanOutcome.FromMatches(
            PersonnummerScanner.Scan($"Pnr {RawPersonnummer} i CV."));
        var (id, owner) = await SeedParsedResumeAsync(flagged, ct);

        using var readScope = _fixture.Services.CreateScope();
        await PrefetchOwnerDekAsync(readScope, owner, ct);
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var engine = readScope.ServiceProvider.GetRequiredService<ICvImprovementEngine>();

        var parsed = await readDb.ParsedResumes.AsNoTracking().SingleAsync(p => p.Id == id, ct);
        parsed.Personnummer.Found.ShouldBeTrue();

        var result = await engine.SuggestAsync(parsed, review: null, RenderProfile.Ats, ct);

        // A PersonnummerStrip change is proposed — a pure removal citing the count/structure,
        // never the raw value (Inv.1).
        var strip = result.Changes.Single(c => c.Kind == ProposedChangeKind.PersonnummerStrip);
        strip.Replacement.ShouldBeNull("PersonnummerStrip är en ren borttagning — ingen ersättningstext.");
        strip.Evidence.ShouldBeOfType<StructuralEvidence>(
            "B4 citerar antalet strukturellt — aldrig råvärdet eller offset (Inv.1).");

        // No proposed-change text (evidence, replacement, rationale) echoes the raw
        // personnummer or any PII marker — the engine quotes only spans the user wrote (§5).
        foreach (var change in result.Changes)
        {
            EvidenceText(change.Evidence).ShouldNotContain(RawPersonnummer, Case.Sensitive);
            EvidenceText(change.Evidence).ShouldNotContain(ContactNameMarker, Case.Sensitive);
            change.Rationale.ShouldNotContain(RawPersonnummer, Case.Sensitive);
            change.Replacement?.Before.ShouldNotContain(RawPersonnummer, Case.Sensitive);
            change.Replacement?.After.ShouldNotContain(RawPersonnummer, Case.Sensitive);
        }
    }

    private static string EvidenceText(CitedEvidence evidence) => evidence switch
    {
        TextSpanEvidence span => $"{span.Span.Quote} {span.Note}",
        StructuralEvidence structural => structural.Observation,
        _ => string.Empty,
    };
}
