using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

/// <summary>
/// Fas 4b PR-6b (#655, ADR 0093 §D4) — the nullable <c>layout_metrics</c> jsonb column added by
/// migration <c>20260706024719_AddParsedResumeLayoutMetrics</c> round-trips through REAL Postgres
/// (Testcontainers, never EF-InMemory — parity OccupationExperienceDeriverIntegrationTests). Two
/// facts the EF value-converter must honour:
///   • a non-null Analyzed <see cref="CvLayoutMetrics"/> saves and reloads with every component
///     intact (status + page count + file size + tightest margin);
///   • a null <see cref="ParsedResume.LayoutMetrics"/> saves as SQL NULL and reloads as null (the
///     converter runs only on a non-null value — a pre-PR-6b/non-analyzed import stays absent).
/// A bare AppDbContext (no field-encryption interceptor) is used deliberately: this test isolates
/// the layout_metrics MAPPING, not the CV-PII encryption (which is exercised elsewhere) — the
/// non-PII plaintext columns (parsed_content_enc is NULLABLE) tolerate the interceptor's absence.
/// </summary>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class ParsedResumeLayoutMetricsPersistenceTests : IAsyncLifetime
{
    private readonly SharedPostgresFixture _postgres;
    private string _connectionString = string.Empty;

    private ServiceProvider _provider = default!;

    private static readonly FakeDateTimeProvider Clock =
        new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public ParsedResumeLayoutMetricsPersistenceTests(SharedPostgresFixture postgres) => _postgres = postgres;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await _postgres.CreateDatabaseAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options
                .UseNpgsql(_connectionString,
                    npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention());
        _provider = services.BuildServiceProvider();

    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DropDatabaseAsync(_connectionString);
        GC.SuppressFinalize(this);
    }

    private static ParsedResume NewParsedResume(CvLayoutMetrics? layoutMetrics) =>
        ParsedResume.Create(
            JobSeekerId.New(),
            "CV_Anna_Andersson.pdf",
            "application/pdf",
            ResumeLanguage.Sv,
            ParsedResumeContent.Empty,
            "råtext",
            ParseConfidence.Failed(ParseFallbackReason.ExtractionFailed),
            PersonnummerScanOutcome.None,
            [],
            Clock,
            layoutMetrics: layoutMetrics).Value;

    // A fresh scope ⇒ a fresh DbContext ⇒ the reload comes from Postgres, not the change tracker.
    private async Task<ParsedResume?> ReloadAsync(ParsedResumeId id, CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ParsedResumes.SingleOrDefaultAsync(p => p.Id == id, ct);
    }

    [Fact]
    public async Task AnalyzedLayoutMetrics_RoundTripsIntact_ThroughTheJsonbColumn()
    {
        var ct = TestContext.Current.CancellationToken;
        var metrics = CvLayoutMetrics.Analyzed(fileSizeBytes: 262_144, pageCount: 2, minMarginPoints: 56.7);
        var parsed = NewParsedResume(metrics);

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ParsedResumes.Add(parsed);
            await db.SaveChangesAsync(ct);
        }

        var reloaded = await ReloadAsync(parsed.Id, ct);

        reloaded.ShouldNotBeNull();
        reloaded!.LayoutMetrics.ShouldNotBeNull();
        // Value equality on the record proves every component survived the jsonb serialization.
        reloaded.LayoutMetrics.ShouldBe(metrics);
        reloaded.LayoutMetrics!.GeometryStatus.ShouldBe(LayoutGeometryStatus.Analyzed);
        reloaded.LayoutMetrics.PageCount.ShouldBe(2);
        reloaded.LayoutMetrics.FileSizeBytes.ShouldBe(262_144);
        reloaded.LayoutMetrics.MinMarginPoints.ShouldBe(56.7);
    }

    [Fact]
    public async Task NullLayoutMetrics_SavesAsNull_AndReloadsAsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var parsed = NewParsedResume(layoutMetrics: null);

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ParsedResumes.Add(parsed);
            await db.SaveChangesAsync(ct);
        }

        var reloaded = await ReloadAsync(parsed.Id, ct);
        reloaded.ShouldNotBeNull();
        reloaded!.LayoutMetrics.ShouldBeNull();

        // And the physical column is SQL NULL (not a serialized "null" string) — proving the
        // converter did not run on the absent value.
        using var scope2 = _provider.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var isNull = await db2.Database
            .SqlQueryRaw<bool>(
                "SELECT layout_metrics IS NULL AS \"Value\" FROM parsed_resumes WHERE id = {0}",
                parsed.Id.Value)
            .SingleAsync(ct);
        isNull.ShouldBeTrue();
    }
}
