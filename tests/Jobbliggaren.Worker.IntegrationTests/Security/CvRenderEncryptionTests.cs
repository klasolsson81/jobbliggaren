using System.Security.Cryptography;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Resumes.Rendering.Abstractions;
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
/// Fas 4 STEG 10 (F4-10, ADR 0074 Invariant 3) Phase B — the deterministic QuestPDF CV renderer
/// reads the <see cref="ParsedResume"/>'s CV-PII (encrypted <c>parsed_content_enc</c> Form B +
/// <c>raw_text</c> Form A) ONLY through the warmed field-decryption pipeline, against REAL
/// Postgres (Testcontainers via <see cref="WorkerTestFixture"/>; InMemory forbidden). Mirrors
/// <see cref="CvImprovementEncryptionTests"/>. The rendered PDF is the MOST complete CV-PII
/// artifact, so the two load-bearing assertions are:
///   1. With a WARM owner DEK the renderer receives a decrypted aggregate and produces a valid
///      non-empty PDF (round-trip OK) — for both profiles.
///   2. WITHOUT a warm DEK the read fails-closed (CryptographicException) — the renderer never
///      sees plaintext on an unauthorized/cold scope, and (CTO Q6) the bytes are streamed,
///      never persisted.
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class CvRenderEncryptionTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private const string ContactNameMarker = "PII-NAMN-CONTACT-F410R-3317";
    private const string RawTextMarker = "PII-RÅTEXT-MARKÖR-F410R-6628";

    private static readonly byte[] PdfMagic = [0x25, 0x50, 0x44, 0x46]; // "%PDF"

    private static ParsedResumeContent RichContent() =>
        new(
            new ParsedContact(ContactNameMarker, "anna@example.com", "070-1234567", "Stockholm"),
            profile: "Erfaren backend-utvecklare. Levererade 3 plattformsmigrationer 2024.",
            experience:
            [
                new ParsedExperience("Backend-utvecklare", "Acme AB", "01/2022 – 06/2024",
                    "Ledde ett team på 8 och ökade konvertering med 23% 2024."),
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

    private async Task<JobSeeker> SeedJobSeekerAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = JobSeeker.Register(
            Guid.NewGuid(), "F4-10 Render Test", new FixedClock(DateTimeOffset.UtcNow)).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    private async Task<(ParsedResumeId Id, JobSeekerId Owner)> SeedParsedResumeAsync(CancellationToken ct)
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
                rawText: $"Anna Andersson\n{RawTextMarker}\nLedde ett team på 8.",
                ConfidentConfidence(), PersonnummerScanOutcome.None, [], clock).Value;

            id = parsed.Id;
            db.ParsedResumes.Add(parsed);
            await db.SaveChangesAsync(ct);
        }

        return (id, seeker.Id);
    }

    private static async Task PrefetchOwnerDekAsync(IServiceScope scope, JobSeekerId owner, CancellationToken ct)
    {
        var dataKeyStore = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
        var currentDataOwner = scope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
        currentDataOwner.SetOwner(owner);
        var dek = await dataKeyStore.GetOrCreateDataKeyAsync(owner, ct);
        CryptographicOperations.ZeroMemory(dek);
    }

    [Theory]
    [InlineData("Ats")]
    [InlineData("Visual")]
    public async Task RenderAsync_WithWarmDek_ReadsDecryptedContent_ProducesValidPdf(string profileName)
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, owner) = await SeedParsedResumeAsync(ct);
        var profile = Enum.Parse<RenderProfile>(profileName);

        using var readScope = _fixture.Services.CreateScope();
        await PrefetchOwnerDekAsync(readScope, owner, ct);
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var renderer = readScope.ServiceProvider.GetRequiredService<ICvRenderer>();

        var parsed = await readDb.ParsedResumes.AsNoTracking().SingleAsync(p => p.Id == id, ct);

        // The decrypt interceptor materialized the CV-PII via the warm DEK.
        parsed.Content.Contact.FullName.ShouldBe(ContactNameMarker);
        parsed.RawText.ShouldContain(RawTextMarker);

        var rendered = await renderer.RenderAsync(parsed, CvTemplateOptions.Default, profile, ct);

        rendered.PdfBytes.ShouldNotBeEmpty();
        rendered.PdfBytes.Take(4).ShouldBe(PdfMagic, "Utdata ska vara en giltig PDF (%PDF).");
        rendered.ContentType.ShouldBe("application/pdf");
        rendered.Profile.ShouldBe(profile);
    }

    [Fact]
    public async Task ReadParsedResume_WithoutWarmDek_FailsClosed_CryptographicException()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, owner) = await SeedParsedResumeAsync(ct);

        // An AUTHED read scope (owner set) but DEK deliberately NOT warmed ⇒ materializing the
        // encrypted CV-PII shadows must fail-closed before the renderer can ever see plaintext
        // (Invariant 3, parity CvImprovementEncryptionTests / CvReviewEncryptionTests).
        using var coldScope = _fixture.Services.CreateScope();
        coldScope.ServiceProvider.GetRequiredService<ICurrentDataOwner>().SetOwner(owner);
        var coldDb = coldScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ex = await Record.ExceptionAsync(async () =>
            await coldDb.ParsedResumes.AsNoTracking().SingleAsync(p => p.Id == id, ct));

        ex.ShouldNotBeNull();
        ex.ShouldBeOfType<CryptographicException>(
            "F4-10 CV-render read without a warm owner DEK must fail-closed (Invariant 3).");
    }
}
