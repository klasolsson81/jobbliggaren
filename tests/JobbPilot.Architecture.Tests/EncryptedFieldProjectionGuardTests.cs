using System.IO;
using System.Reflection;
using JobbPilot.Application.Applications.Queries.GetApplicationById;
using JobbPilot.Application.Common.Security;
using Shouldly;

namespace JobbPilot.Architecture.Tests;

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
        "src/JobbPilot.Application/Applications/Commands/MarkGhosted/MarkGhostedCommandHandler.cs")]
    [InlineData(
        "src/JobbPilot.Infrastructure/Auth/AccountHardDeleter.cs")]
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
            .GetType("JobbPilot.Application.Common.Abstractions.IAuthenticatedRequest");
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
