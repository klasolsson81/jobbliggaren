using System.IO;
using System.Reflection;
using Jobbliggaren.Application.Applications.Queries.GetApplicationById;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Resumes.Commands.CreateResume;
using Jobbliggaren.Application.Resumes.Commands.ImportResume;
using Jobbliggaren.Application.Resumes.Commands.UpdateMasterContent;
using Jobbliggaren.Application.Resumes.Queries.GetResumeById;
using Jobbliggaren.Application.Resumes.Rendering.Queries.RenderResume;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// TD-13 (ADR 0049 Mekanik-not 4, CTO Approach A/D-komplement 2026-05-18).
///
/// <para>
/// Invariant: en query/command vars resultat härleds från en krypterad
/// kolumn (<c>cover_letter</c>/<c>application_notes.content</c>/
/// <c>follow_ups.note</c>/<c>resume_versions.content</c>) får INTE
/// SQL-projicera det krypterade fältet (`.Select(... x.CoverLetter ...)`) —
/// då kringgås <c>FieldDecryptionMaterializationInterceptor</c> (EF Core 10:
/// interceptorn triggar endast vid entitets-materialisering) och ciphertext
/// läcker oläst till DTO:n. Sådana meddelanden måste (a) bära
/// <see cref="IRequiresFieldEncryptionKey"/> så
/// <c>FieldEncryptionKeyPrefetchBehavior</c> värmer ägar-DEK, och (b) materialisera
/// den ägande entiteten i handlern (verifieras av integrationssviten
/// <c>FieldEncryptionInterceptorTests</c> — denna arch-test är
/// regressions-tripwire per CTO "precision över bredd").
/// </para>
///
/// Allowlist nedan utökas medvetet när nya handlers rör krypterade kolumner
/// (C4: Resume-detalj-vägen — redan konform via Include+in-memory-map).
/// </summary>
public class EncryptedFieldProjectionGuardTests
{
    [Fact]
    public void GetApplicationByIdQuery_MustCarry_IRequiresFieldEncryptionKey()
    {
        // GetApplicationById materialiserar Application+Notes+FollowUps
        // (krypterade kolumner) → MÅSTE bära markören så prefetch-behavior
        // värmer ägar-DEK före materialisering (annars fail-closed-kast).
        typeof(IRequiresFieldEncryptionKey)
            .IsAssignableFrom(typeof(GetApplicationByIdQuery))
            .ShouldBeTrue(
                "GetApplicationByIdQuery returnerar dekrypterade fält " +
                "(CoverLetter/Notes.Content/FollowUps.Note) — måste implementera " +
                "IRequiresFieldEncryptionKey (ADR 0049 Mekanik-not 4). Utan markören " +
                "värmer FieldEncryptionKeyPrefetchBehavior aldrig ägar-DEK och " +
                "FieldDecryptionMaterializationInterceptor fail-closed-kastar.");
    }

    /// <summary>
    /// CTO #3 (iv) tripwire 2026-05-18 (ADR 0049 Mekanik-not 5b).
    ///
    /// <para>
    /// Invariant: en system-/non-<c>IAuthenticatedRequest</c>-command-handler
    /// (eller worker-jobb-port) som materialiserar <c>Application</c>/
    /// <c>ApplicationNote</c>/<c>FollowUp</c> får INTE referera de krypterade
    /// properties (<c>CoverLetter</c>/<c>Content</c>/<c>Note</c>) i sin
    /// handler-/job-kropp. Motivering: system-scopet har ingen
    /// <c>ICurrentDataOwner</c> ⇒ <c>FieldDecryptionMaterializationInterceptor</c>
    /// lämnar fältet som ciphertext (CTO #3 (iv) passthrough, ingen kast). Om
    /// ett system-jobb läser fältet får det tyst ciphertext — en data-
    /// korruptions-/läckage-risk. <c>MarkGhostedCommandHandler</c> +
    /// <c>AccountHardDeleter</c> är referens-mönstret (rör aldrig
    /// klartext-fältet).
    /// </para>
    ///
    /// <para>
    /// Form (CTO "precision över bredd"): allowlist-baserad källtext-scan av de
    /// kända system-handlers som materialiserar de krypterade aggregaten. En
    /// exakt IL-/method-body-reflektion är brittle i .NET och ger falska
    /// positiv på t.ex. nameof/loggning ⇒ medvetet fokuserad variant +
    /// dokumenterad invariant. Allowlisten utökas när nya system-jobb rör
    /// dessa aggregat (samma ratchet-mönster som projektions-allowlisten ovan).
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(
        "src/Jobbliggaren.Application/Applications/Commands/MarkGhosted/MarkGhostedCommandHandler.cs")]
    [InlineData(
        "src/Jobbliggaren.Infrastructure/Auth/AccountHardDeleter.cs")]
    public void SystemScopeHandler_MustNotReference_EncryptedProperties(
        string handlerRelativePath)
    {
        // De tre krypterade entitets-properties (EncryptedFieldRegistry
        // on-disk 2026-05-18). System-handlers som materialiserar aggregaten
        // får läsa FK/Id/Status men ALDRIG dessa.
        string[] encryptedMembers = [".CoverLetter", ".Content", ".Note"];

        var repoRoot = FindRepoRoot();
        var handlerPath = Path.Combine(
            repoRoot, handlerRelativePath.Replace('/', Path.DirectorySeparatorChar));

        File.Exists(handlerPath).ShouldBeTrue(
            $"arch-test-allowlist pekar på en fil som inte finns: {handlerPath}. " +
            "Uppdatera allowlisten om handlern flyttats/döpts om.");

        var source = File.ReadAllText(handlerPath);

        var violations = encryptedMembers
            .Where(member => source.Contains(member, StringComparison.Ordinal))
            .ToList();

        violations.ShouldBeEmpty(
            $"{handlerRelativePath} är ett system-scope-mönster (ingen " +
            "ICurrentDataOwner) — materialiserar krypterade aggregat men FÅR " +
            "INTE läsa de krypterade fälten (CTO #3 (iv): interceptorn lämnar " +
            "ciphertext orört i system-scope ⇒ en läsning ger TYST CIPHERTEXT). " +
            "Referens-mönster: MarkGhostedCommandHandler/AccountHardDeleter rör " +
            "aldrig klartext-fältet. Brott: " + string.Join(", ", violations));
    }

    /// <summary>
    /// TD-13 C4.3 (ADR 0049 Mekanik-not 6 #1c). Resume-detalj-/skriv-vägen
    /// materialiserar/skriver dekrypterad <c>ResumeVersion.Content</c>
    /// (krypterad text-shadow <c>content_enc</c>) — måste bära markören så
    /// <c>FieldEncryptionKeyPrefetchBehavior</c> värmer ägar-DEK före
    /// decrypt-on-read (GetResumeById) resp. encrypt-on-write (CreateResume/
    /// UpdateMasterContent). Utan markören → ingen prefetch → autentiserad
    /// scope utan cachad DEK → fail-closed-kast (read) eller
    /// CryptographicException FÖRE DML (write).
    /// </summary>
    [Theory]
    [InlineData(typeof(GetResumeByIdQuery))]
    [InlineData(typeof(CreateResumeCommand))]
    [InlineData(typeof(UpdateMasterContentCommand))]
    [InlineData(typeof(RenderResumeQuery))]
    public void ResumeEncryptedHandler_MustCarry_IRequiresFieldEncryptionKey(
        Type messageType)
    {
        typeof(IRequiresFieldEncryptionKey)
            .IsAssignableFrom(messageType)
            .ShouldBeTrue(
                $"{messageType.Name} rör dekrypterad/krypterad " +
                "ResumeVersion.Content (#1c content_enc) — måste implementera " +
                "IRequiresFieldEncryptionKey (ADR 0049 Mekanik-not 4/6). Utan " +
                "markören värmer FieldEncryptionKeyPrefetchBehavior aldrig " +
                "ägar-DEK och interceptor-paret fail-closed-kastar.");
    }

    /// <summary>
    /// TD-13 C4.3 tripwire (ADR 0049 Mekanik-not 5b/6). Resume-handlers som
    /// materialiserar <c>ResumeVersion</c> men INTE bär
    /// <see cref="IRequiresFieldEncryptionKey"/> (DeleteResume/
    /// DeleteResumeVersion/RenameResume — de rör aldrig <c>Content</c>, bara
    /// soft-delete/rename) får INTE referera <c>.Content</c>. Utan markör
    /// körs ingen prefetch → <c>ICurrentDataOwner</c> osatt →
    /// <c>FieldDecryptionMaterializationInterceptor</c> passthrough (Content
    /// förblir null, ingen kast — paritet system-scope). En läsning av
    /// <c>Content</c> i en sådan handler vore en NRE-/tyst-null-risk. Samma
    /// precision-över-bredd-källtextscan som
    /// <see cref="SystemScopeHandler_MustNotReference_EncryptedProperties"/>.
    /// </summary>
    [Theory]
    [InlineData(
        "src/Jobbliggaren.Application/Resumes/Commands/DeleteResume/DeleteResumeCommandHandler.cs")]
    [InlineData(
        "src/Jobbliggaren.Application/Resumes/Commands/DeleteResumeVersion/DeleteResumeVersionCommandHandler.cs")]
    [InlineData(
        "src/Jobbliggaren.Application/Resumes/Commands/RenameResume/RenameResumeCommandHandler.cs")]
    public void UnmarkedResumeHandler_MustNotReference_Content(
        string handlerRelativePath)
    {
        var repoRoot = FindRepoRoot();
        var handlerPath = Path.Combine(
            repoRoot, handlerRelativePath.Replace('/', Path.DirectorySeparatorChar));

        File.Exists(handlerPath).ShouldBeTrue(
            $"arch-test-allowlist pekar på en fil som inte finns: {handlerPath}. " +
            "Uppdatera allowlisten om handlern flyttats/döpts om.");

        var source = File.ReadAllText(handlerPath);

        source.Contains(".Content", StringComparison.Ordinal)
            .ShouldBeFalse(
                $"{handlerRelativePath} materialiserar ResumeVersion men bär " +
                "INTE IRequiresFieldEncryptionKey (ingen prefetch). " +
                "FieldDecryptionMaterializationInterceptor lämnar Content null " +
                "(passthrough) — en .Content-referens ger TYST NULL/NRE. Lägg " +
                "markören (och läs Content) ELLER rör aldrig Content (ADR 0049 " +
                "Mekanik-not 5b/6).");
    }

    /// <summary>
    /// TD-13 C4.3 (ADR 0049 Mekanik-not 6 #1c) — modell-invariant via
    /// källtextscan (paritet med husets source-scan-arch-tester). #1c kräver
    /// att <c>ResumeVersion.Content</c> är EF-<c>Ignore</c>:ad (interceptor-
    /// paret äger transformen, ingen JSON-ValueConverter — annars återinförs
    /// C4.0-RÖD) och att <c>ContentJsonOptions</c> är EN delad SPOT i
    /// <c>EncryptedFieldRegistry</c> (ej duplicerad i konfig:en).
    /// </summary>
    [Fact]
    public void ResumeVersionConfiguration_Honors_Number1c_Invariants()
    {
        var repoRoot = FindRepoRoot();
        var configPath = Path.Combine(repoRoot,
            "src", "Jobbliggaren.Infrastructure", "Persistence", "Configurations",
            "ResumeVersionConfiguration.cs");
        var registryPath = Path.Combine(repoRoot,
            "src", "Jobbliggaren.Infrastructure", "Security",
            "EncryptedFieldRegistry.cs");

        File.Exists(configPath).ShouldBeTrue(configPath);
        File.Exists(registryPath).ShouldBeTrue(registryPath);

        var configSrc = File.ReadAllText(configPath);
        var registrySrc = File.ReadAllText(registryPath);

        configSrc.Contains("builder.Ignore(rv => rv.Content)", StringComparison.Ordinal)
            .ShouldBeTrue(
                "#1c kräver builder.Ignore(rv => rv.Content) — Content får ej " +
                "vara EF-tracked (ValueComparer-frågan upphör, ADR 0049 " +
                "Mekanik-not 6).");

        configSrc.Contains("ValueConverter<ResumeContent", StringComparison.Ordinal)
            .ShouldBeFalse(
                "JSON-ValueConverter:n på Content MÅSTE vara borttagen — en VC " +
                "kör ConvertFromProvider FÖRE InitializedInstance (C4.0-RÖD " +
                "bekräftad) och skulle invalidera #1c-mekaniken.");

        configSrc.Contains("new JsonSerializerOptions", StringComparison.Ordinal)
            .ShouldBeFalse(
                "ContentJsonOptions får INTE duplikat-instansieras i " +
                "ResumeVersionConfiguration — SPOT:en bor i " +
                "EncryptedFieldRegistry (architect 2026-05-19).");

        registrySrc.Contains("ContentJsonOptions", StringComparison.Ordinal)
            .ShouldBeTrue(
                "ContentJsonOptions-SPOT förväntas i EncryptedFieldRegistry " +
                "(delad av Form B-delegater + interceptor-paret).");
    }

    /// <summary>
    /// F4-8 (ADR 0074 Invariant 3): <see cref="ImportResumeCommand"/> persists encrypted
    /// ParsedResume CV-PII (<c>parsed_content_enc</c> Form B + <c>raw_text</c> Form A) —
    /// must carry <see cref="IRequiresFieldEncryptionKey"/> so
    /// <c>FieldEncryptionKeyPrefetchBehavior</c> warms the owner DEK before the
    /// SaveChanges interceptor encrypts the shadows (else fail-closed before DML).
    /// </summary>
    [Fact]
    public void ImportResumeCommand_MustCarry_IRequiresFieldEncryptionKey()
    {
        typeof(IRequiresFieldEncryptionKey)
            .IsAssignableFrom(typeof(ImportResumeCommand))
            .ShouldBeTrue(
                "ImportResumeCommand persists encrypted ParsedResume CV-PII " +
                "(parsed_content_enc/raw_text) — must implement IRequiresFieldEncryptionKey " +
                "(ADR 0074 Invariant 3 / ADR 0049). Without it FieldEncryptionKeyPrefetchBehavior " +
                "never warms the owner DEK and the encrypt-on-write interceptor fail-closed-throws.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.ShouldNotBeNull(
            "kunde inte hitta repo-roten (CLAUDE.md) uppåt från test-bin — " +
            "arch-testet behöver källträdet för källtext-scan");
        return dir!.FullName;
    }

    [Fact]
    public void EveryMessage_Implementing_IRequiresFieldEncryptionKey_IsAuthenticated()
    {
        // Prefetch får aldrig köra KMS-op för en icke-auktoriserad principal
        // (§5.4) — markören förutsätter IAuthenticatedRequest (behaviorn körs
        // efter Authorization). Fångar en framtida markör utan auth-gate.
        var applicationAssembly = typeof(GetApplicationByIdQuery).Assembly;
        var authenticatedRequest = applicationAssembly
            .GetType("Jobbliggaren.Application.Common.Abstractions.IAuthenticatedRequest");
        authenticatedRequest.ShouldNotBeNull();

        var violations = applicationAssembly
            .GetTypes()
            .Where(t => typeof(IRequiresFieldEncryptionKey).IsAssignableFrom(t)
                        && t is { IsInterface: false, IsAbstract: false })
            .Where(t => !authenticatedRequest!.IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();

        violations.ShouldBeEmpty(
            "Meddelanden med IRequiresFieldEncryptionKey måste även vara " +
            "IAuthenticatedRequest (FieldEncryptionKeyPrefetchBehavior kör efter " +
            "Authorization; ingen KMS-op för ej auktoriserad principal, §5.4). " +
            "Brott: " + string.Join(", ", violations));
    }
}
