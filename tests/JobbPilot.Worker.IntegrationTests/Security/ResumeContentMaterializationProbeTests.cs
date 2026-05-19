using System.Reflection;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Domain.Resumes;
using JobbPilot.Infrastructure.Persistence;
using JobbPilot.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobbPilot.Worker.IntegrationTests.Security;

/// <summary>
/// TD-13 — <b>invariant-regressionsvakt</b> för EF Core 10:s
/// VC↔interceptor-materialiserings-ordning. <b>Inte längre en engångs-gate.</b>
///
/// <para>
/// <b>Historik:</b> startade som C4.0:s empiriska gate-PAR (architect-låst
/// design, agentId af92815b78c2fb817) för att avgöra C4.2:s interceptor-mekanik.
/// Gaten är <b>KÖRD</b> — utfall <b>RÖD bekräftat</b>: i
/// <see cref="IMaterializationInterceptor.InitializedInstance"/> ser
/// interceptorn <see cref="ResumeVersion.Content"/> som ett redan-deserialiserat
/// <see cref="ResumeContent"/>-objekt, dvs.
/// <c>ValueConverter.ConvertFromProvider</c> (<c>ResumeContent</c>↔JSON-<c>string</c>)
/// kör <b>FÖRE</b> <c>InitializedInstance</c> (instansen är fullt
/// materialiserad). Detta är architect-prediktionen Y (Microsoft Learn:
/// InitializedInstance anropas efter fullt materialiserad instans).
/// </para>
///
/// <para>
/// <b>Varför vakten är permanent:</b> architect-domen a946e2a66200dc5b8 låste
/// C4.2-mekaniken på RÖD (#1c dual-property-shadow). RÖD-ordningen
/// (<c>VC.ConvertFromProvider</c> kör FÖRE <c>InitializedInstance</c>) är
/// därmed en <b>invariant som C4.2:s #1c-mekanik förutsätter</b>, inte ett
/// engångs-faktum. ADR 0049 Mekanik-not 6 (#1c) bygger på denna ordning. En
/// framtida EF Core-uppgradering som ändrar VC↔interceptor-ordningen skulle
/// tyst invalidera #1c — exakt den regressionen denna vakt fångar. Gate-PAR-
/// formen (två symmetriska <c>[Fact]</c> där en alltid failar) ersatt av ETT
/// asserterande <c>[Fact]</c> som är GRÖNT mot nuvarande EF Core 10-beteende
/// och blir RÖTT vid ordnings-regression.
/// </para>
///
/// <para>
/// <b>Isolering (architect-spec punkt 3):</b> testet bygger en EGEN
/// <see cref="DbContextOptions{AppDbContext}"/> mot fixturens redan-migrerade
/// container-DB (<see cref="WorkerTestFixture.ConnectionString"/>) där ENBART
/// diagnostik-interceptorn <see cref="ResumeContentProbeInterceptor"/>
/// är aktiv — INTE C3:s krypto-interceptor-par. Ingen krypto i spel:
/// <see cref="ResumeVersion.Content"/> är klartext-jsonb (ej i
/// EncryptedFieldRegistry, ingen sentinel). Detta isolerar enbart
/// VC↔interceptor-materialiserings-ordningen. Prod-modellen
/// (<see cref="AppDbContext.OnModelCreating"/>) applicerar
/// <c>ResumeVersionConfiguration</c>:s JSON-VC automatiskt — samma
/// jsonb-mappning som prod.
/// </para>
///
/// <para>
/// <b>Probe-interceptorn lever i TESTPROJEKTET</b> (ren diagnostik, ingen
/// prodkods-ändring) och är nu <b>permanent regressions-infra</b> — exponerar
/// via static thread-static
/// <see cref="ResumeContentProbeInterceptor.ObservedContentRuntimeType"/> den
/// reflektions-lästa runtime-typen på materialiserat <c>Content</c>-värde.
/// Seed + Testcontainers/Npgsql-infran är likaledes permanent (ej engångs).
/// </para>
///
/// <para>
/// <b>Assertion:</b> ETT <c>[Fact]</c>
/// (<c>ResumeContentMaterialization_VcConvertFromProvider_RunsBeforeMaterializationInterceptor</c>)
/// materialiserar den seedade Master-raden via probe-aktiverad context och
/// assertar att
/// <see cref="ResumeContentProbeInterceptor.ObservedContentRuntimeType"/> är
/// <see cref="ResumeContent"/> (bekräftad RÖD-sanning). <c>Observed</c>-vakten
/// säkerställer att interceptorn faktiskt kördes — annars är observationen
/// ogiltig och testet failar tydligt (ingen grön-falsk-positiv). 0 alltid-röda
/// <c>[Fact]</c> kvarstår.
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class ResumeContentMaterializationProbeTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    // ── Seed: JobSeeker + Resume + Master-ResumeVersion med icke-tomt
    //    ResumeContent. Skrivs via befintlig EF-väg på fixturens crypto-aware
    //    context (ResumeVersion.Content är INTE i EncryptedFieldRegistry ⇒
    //    ingen prefetch krävs, JSON-VC serialiserar → klartext-jsonb). ────────
    private async Task<ResumeVersionId> SeedMasterVersionAsync(CancellationToken ct)
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var seeker = JobSeeker.Register(
            Guid.NewGuid(), "C4.0 Probe Seeker", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        var resume = Resume.Create(
            seeker.Id,
            name: "C4.0 Probe-CV",
            fullName: "Anna Andersson",
            clock).Value;

        // Icke-tomt innehåll (PersonalInfo + Experiences + Educations + Skills)
        // via domän-API:t — whole-replacement på Master-versionen.
        var content = new ResumeContent(
            new PersonalInfo("Anna Andersson", "anna@example.com", "070-1234567", "Stockholm"),
            experiences:
            [
                new Experience(
                    "Acme AB", "Backend-utvecklare",
                    new DateOnly(2021, 1, 1), new DateOnly(2024, 6, 30),
                    "Byggde betaltjänster i .NET."),
            ],
            educations:
            [
                new Education(
                    "KTH", "Civilingenjör Datateknik",
                    new DateOnly(2016, 8, 20), new DateOnly(2021, 6, 10)),
            ],
            skills:
            [
                new Skill("C#", 5),
                new Skill("PostgreSQL", 4),
            ],
            summary: "Erfaren backend-utvecklare med fokus på betaltjänster.");

        resume.UpdateMasterContent(content, clock).IsSuccess.ShouldBeTrue();

        db.Resumes.Add(resume);
        await db.SaveChangesAsync(ct);

        return resume.MasterVersion.Id;
    }

    /// <summary>
    /// Architect-spec punkt 3: isolerad probe-only context. Egen
    /// <see cref="DbContextOptions{AppDbContext}"/> mot fixturens redan-
    /// migrerade DB, snake_case + Npgsql exakt som prod-registreringen
    /// (DependencyInjection.cs rad 266-273) MEN med ENBART probe-interceptorn —
    /// ingen C3-krypto. Prod-<c>OnModelCreating</c> applicerar
    /// <c>ResumeVersionConfiguration</c>:s JSON-VC automatiskt.
    /// </summary>
    private AppDbContext CreateProbeOnlyContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                _fixture.ConnectionString,
                npgsql => npgsql.MigrationsAssembly(
                    typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new ResumeContentProbeInterceptor())
            .Options;

        return new AppDbContext(options);
    }

    private async Task<Type?> ObserveContentRuntimeTypeAsync(CancellationToken ct)
    {
        var versionId = await SeedMasterVersionAsync(ct);

        ResumeContentProbeInterceptor.Reset();

        await using var probeDb = CreateProbeOnlyContext();

        // AsNoTracking + ren materialisering av Master-raden via probe-context.
        // ResumeVersion är barn-entitet i Resume-aggregatet ⇒ projicera via
        // Resume.Versions (Include) och materialisera den seedade versionen.
        var resume = await probeDb.Resumes
            .AsNoTracking()
            .Include(r => r.Versions)
            .FirstOrDefaultAsync(
                r => r.Versions.Any(v => v.Id == versionId), ct);

        resume.ShouldNotBeNull(
            "seedad Resume måste materialiseras via probe-only context");
        resume.Versions.ShouldContain(
            v => v.Id == versionId,
            "Master-versionen måste ha materialiserats (probe ska ha observerat den)");

        ResumeContentProbeInterceptor.Observed.ShouldBeTrue(
            "probe-interceptorns InitializedInstance måste ha körts för " +
            "ResumeVersion — annars är observationen ogiltig");

        return ResumeContentProbeInterceptor.ObservedContentRuntimeType;
    }

    /// <summary>
    /// Invariant-regressionsvakt (TD-13 C4.0-utfall RÖD bekräftat,
    /// architect-dom a946e2a66200dc5b8, ADR 0049 Mekanik-not 6 #1c).
    ///
    /// <para>
    /// Assertar den BEKRÄFTADE RÖD-sanningen:
    /// <c>VC.ConvertFromProvider</c> (<c>ResumeContent</c>↔JSON-<c>string</c>)
    /// kör FÖRE <see cref="IMaterializationInterceptor.InitializedInstance"/>,
    /// dvs. interceptorn observerar ett redan-deserialiserat
    /// <see cref="ResumeContent"/>-objekt — INTE en JSON-<see cref="string"/>.
    /// </para>
    ///
    /// <para>
    /// GRÖNT mot nuvarande EF Core 10-beteende. Blir RÖTT om en framtida
    /// EF-uppgradering ändrar VC↔interceptor-ordningen — vilket tyst skulle
    /// invalidera C4.2:s #1c dual-property-shadow-mekanik. Exakt den
    /// regressionen denna vakt ska fånga.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ResumeContentMaterialization_VcConvertFromProvider_RunsBeforeMaterializationInterceptor()
    {
        var ct = TestContext.Current.CancellationToken;

        var observed = await ObserveContentRuntimeTypeAsync(ct);

        observed.ShouldBe(
            typeof(ResumeContent),
            "INVARIANT (C4.0-utfall RÖD, architect-dom a946e2a66200dc5b8): " +
            "InitializedInstance måste se Content som redan-deserialiserat " +
            "ResumeContent ⇒ VC.ConvertFromProvider körs FÖRE " +
            "InitializedInstance (fullt materialiserad instans). Om detta är " +
            "RÖTT har EF Core ändrat VC↔interceptor-ordningen och C4.2:s #1c " +
            "dual-property-shadow-mekanik (ADR 0049 Mekanik-not 6) är " +
            "invaliderad — åtgärda mekaniken, ändra INTE detta assertions-värde.");
    }

    /// <summary>
    /// Engångs-diagnostik-interceptor — ENDAST i testprojektet, ingen
    /// prodkods-ändring (architect-spec punkt 2). När den materialiserade
    /// entiteten är en <see cref="ResumeVersion"/>: läs via reflection runtime-
    /// <b>typen</b> på <see cref="ResumeVersion.Content"/>-värdet och exponera
    /// den statiskt. <see cref="ThreadStaticAttribute"/> + Worker-collection är
    /// seriell ⇒ deterministisk observation utan korsning mellan fakta.
    /// </summary>
    private sealed class ResumeContentProbeInterceptor : IMaterializationInterceptor
    {
        [ThreadStatic]
        private static Type? _observedContentRuntimeType;

        [ThreadStatic]
        private static bool _observed;

        private static readonly PropertyInfo ContentProperty =
            typeof(ResumeVersion).GetProperty(
                nameof(ResumeVersion.Content),
                BindingFlags.Public | BindingFlags.Instance)!;

        public static Type? ObservedContentRuntimeType => _observedContentRuntimeType;

        public static bool Observed => _observed;

        public static void Reset()
        {
            _observedContentRuntimeType = null;
            _observed = false;
        }

        public object InitializedInstance(
            MaterializationInterceptionData materializationData, object entity)
        {
            if (entity is ResumeVersion)
            {
                var value = ContentProperty.GetValue(entity);
                _observedContentRuntimeType = value?.GetType();
                _observed = true;
            }

            return entity;
        }
    }
}
