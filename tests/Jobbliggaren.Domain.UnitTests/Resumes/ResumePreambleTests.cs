using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Resumes;

/// <summary>
/// #1060 — <c>ResumeContent.Preamble</c>, the verbatim unclassified text an IMPORTED CV carried
/// above its first heading (#844, ADR 0109), projected onto the canonical CV at promote.
///
/// <para>The field's whole safety rests on three properties, and all three are aggregate
/// behaviour rather than convention, so all three are pinned here: it is WRITE-ONCE (only
/// <c>CreateFromParsed</c> may set it), it is BOUNDED like every other free-text field, and it
/// is NEVER LINEARIZED — the linearized text is what <c>/render</c>, the ATS view and the
/// citation substrate are built from, and an unclassified page number or address block must
/// never reach a document the user sends to employers.</para>
/// </summary>
public class ResumePreambleTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId Owner = new(Guid.NewGuid());
    private const string Preamble = "Erfaren backend-utvecklare med tio år i betalbranschen.";

    private static ResumeContent Content(string? preamble = null, string? summary = null) => new(
        new PersonalInfo("Klas Olsson", "klas@example.com", "070-123 45 67", "Stockholm"),
        experiences: [new Experience("Acme AB", "Backend-utvecklare", null, null, null, "2021 - 2024")],
        educations: [new Education("KTH", "Civilingenjör", null, null, "2016 - 2021")],
        skills: [new Skill("C#", 8)],
        summary: summary,
        preamble: preamble);

    private static Resume Imported(string? preamble = Preamble) =>
        Resume.CreateFromParsed(
            Owner, "Importerat CV", Content(preamble), new ParsedResumeId(Guid.NewGuid()), Clock).Value;

    // ---------------------------------------------------------------
    // Ingress — CreateFromParsed is the only one
    // ---------------------------------------------------------------

    [Fact]
    public void CreateFromParsed_CarriesThePreambleOntoTheMasterVersion()
    {
        Imported().MasterVersion.Content.Preamble.ShouldBe(Preamble);
    }

    [Fact]
    public void Create_TemplateOriginCv_HasNoPreamble()
    {
        // Null BY CONSTRUCTION, not from inability: an app-built CV is emitted with every
        // section under a heading (ADR 0097 §2), so it has no region above its first one.
        var resume = Resume.Create(Owner, "Mitt CV", "Klas Olsson", Clock).Value;

        resume.MasterVersion.Content.Preamble.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // Write-once — the invariant that stops #844 arriving one screen later
    // ---------------------------------------------------------------

    /// <summary>
    /// THE pin. <c>UpdateMasterContent</c> replaces content wholesale from a transport DTO, and
    /// the client's <c>resumeContentDtoSchema</c> is non-strict — <c>.parse()</c> silently
    /// STRIPS a key it does not model. So a CV editor that never heard of the preamble would
    /// round-trip content without it and erase, on the user's first edit, the text her file
    /// carried above its first heading. That is #844's drop, arriving one screen later.
    ///
    /// <para>Enforced in the aggregate rather than in a client contract (CLAUDE.md §2.2), which
    /// is what makes the erasure structurally impossible instead of merely test-covered, and
    /// what lets the FE write path stay untouched.</para>
    /// </summary>
    [Fact]
    public void UpdateMasterContent_WhenTransportOmitsThePreamble_KeepsTheStoredOne()
    {
        var resume = Imported();

        var result = resume.UpdateMasterContent(
            Content(preamble: null, summary: "En ny sammanfattning."), Clock);

        result.IsSuccess.ShouldBeTrue();
        resume.MasterVersion.Content.Preamble.ShouldBe(Preamble);
        resume.MasterVersion.Content.Summary.ShouldBe("En ny sammanfattning.");
    }

    /// <summary>
    /// The other half of write-once, and the one a "preserve if not supplied" implementation
    /// would fail: a transport that SUPPLIES a different value must not win either. The text is
    /// the file's, not the client's — no in-app path may author or alter it, which is what keeps
    /// ADR 0109's FAS-DEFERRED classify step deferred structurally (ADR 0112: read-only).
    /// </summary>
    [Fact]
    public void UpdateMasterContent_WhenTransportSuppliesADifferentPreamble_IgnoresIt()
    {
        var resume = Imported();

        var result = resume.UpdateMasterContent(Content(preamble: "Något användaren skrev in"), Clock);

        result.IsSuccess.ShouldBeTrue();
        resume.MasterVersion.Content.Preamble.ShouldBe(Preamble);
    }

    [Fact]
    public void UpdateMasterContent_OnATemplateOriginCv_LeavesThePreambleNull()
    {
        // The write-once rule must not INVENT one either: a CV with no preamble stays without
        // one however hard the transport pushes.
        var resume = Resume.Create(Owner, "Mitt CV", "Klas Olsson", Clock).Value;

        resume.UpdateMasterContent(Content(preamble: "Inskickat av klienten"), Clock)
            .IsSuccess.ShouldBeTrue();

        resume.MasterVersion.Content.Preamble.ShouldBeNull();
    }

    /// <summary>
    /// A tailored version does not inherit it, and that is a decision rather than an omission:
    /// the preamble is provenance about the imported FILE, and a variant composed for one advert
    /// has no source file. Explicit in the aggregate so the value cannot arrive by accident from
    /// a round-tripped Master DTO.
    /// </summary>
    [Fact]
    public void CreateTailored_DoesNotInheritThePreamble()
    {
        var resume = Imported();

        var versionId = resume.CreateTailored(Content(preamble: Preamble), Clock);

        versionId.IsSuccess.ShouldBeTrue();
        var tailored = resume.Versions.Single(v => v.Id == versionId.Value);
        tailored.Content.Preamble.ShouldBeNull();
        // ...and the Master keeps its own.
        resume.MasterVersion.Content.Preamble.ShouldBe(Preamble);
    }

    // ---------------------------------------------------------------
    // Bounded, like every other free-text field
    // ---------------------------------------------------------------

    /// <summary>
    /// The cap is the Domain's, and the argument that trips it is hand-built — so §5's `Tests:`
    /// rule applies and the actor is named rather than left implicit.
    ///
    /// <para><b>No path in `src/` produces a preamble longer than 2 000 today.</b> The only
    /// writer is <c>PreambleResidue.Extract</c>, which truncates at its own
    /// <c>MaxPreambleChars = 2000</c> before the value ever reaches
    /// <c>AutoPromoteContentMapper</c>, and that truncation is pinned where the writer lives —
    /// <c>PreambleResidueTests.Segment_HeadinglessCv_CarriesAtMostTheCap</c> and
    /// <c>…_TruncatesOnALineBoundary_NeverMidSentence</c>. The field has no other ingress:
    /// <c>UpdateMasterContent</c> is write-once and <c>CreateTailored</c> clears it.</para>
    ///
    /// <para>That makes this the AUTHORITY half of a mirrored pair, not a live rejection path —
    /// the same relationship <c>Resume.cs</c> already documents for the 100/200-char caps the
    /// client zod schemas mirror, where the Domain holds the number and the upstream writer
    /// respects it. What is asserted here is the aggregate's OWN predicate over its OWN
    /// argument; nothing downstream is claimed, and no production behaviour is inferred from an
    /// impossible premise. It is what keeps the number honest if a SECOND ingress is ever added
    /// — which is the only circumstance in which it can fire.</para>
    /// </summary>
    [Fact]
    public void CreateFromParsed_WithOverLongPreamble_ReturnsFailure_NoResume()
    {
        var result = Resume.CreateFromParsed(
            Owner, "Importerat CV", Content(preamble: new string('a', 2_001)),
            new ParsedResumeId(Guid.NewGuid()), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.PreambleTooLong");
    }

    [Fact]
    public void CreateFromParsed_WithPreambleExactlyAtTheCap_Succeeds()
    {
        // 2 000 IS producible — it is exactly what PreambleResidue emits for a headingless CV,
        // whose preamble is the whole document. So this row is the boundary the two caps share,
        // and it must not be off by one in either direction: a Domain cap one char tighter than
        // the writer's would block the most common shape the writer produces.
        var result = Resume.CreateFromParsed(
            Owner, "Importerat CV", Content(preamble: new string('a', 2_000)),
            new ParsedResumeId(Guid.NewGuid()), Clock);

        result.IsSuccess.ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // Never rendered
    // ---------------------------------------------------------------

    /// <summary>
    /// The linearized text is the substrate <c>/render</c>, the ATS view and every citation are
    /// built from, so "not in the linearizer output" is the Domain-level form of "never reaches
    /// a document the user sends to employers". ADR 0109's accepted junk cost is DISPLAY, never
    /// RENDER: an OCR header, a page number or an address block riding an unclassified region
    /// must not become part of the CV itself.
    ///
    /// <para>The linearizer enumerates fields explicitly and reflects over nothing, so this is
    /// true the moment the field is added. The test is what keeps it true — it is the thing that
    /// goes red when someone "completes" the enumeration without reading why it is incomplete.
    /// It deliberately asserts the opposite of <c>ResumeContentLinearizerTests</c>'s
    /// citation-losslessness measurement, and that opposition is the point: every OTHER
    /// user-authored text unit must be locatable in the linearized text; this one must not,
    /// because the user never authored it as CV content.</para>
    /// </summary>
    [Fact]
    public void Linearize_NeverEmitsThePreamble()
    {
        const string distinctive = "Zebrafisk-kvartalsrapport 1998";
        var content = Content(preamble: distinctive, summary: "En helt vanlig sammanfattning.");

        var linearized = ResumeContentLinearizer.Linearize(content);

        linearized.Text.ShouldNotContain(distinctive);
        // The positive control: without it this test would also pass on an empty linearizer.
        linearized.Text.ShouldContain("En helt vanlig sammanfattning.");
    }

    [Fact]
    public void ApplyDenormalizedProjection_NeverDerivesFromThePreamble()
    {
        // The denormalized projection lands in PLAINTEXT columns (ADR 0059), outside the DEK
        // envelope — the one place CV-PII must never be derived into by accident.
        var resume = Imported($"Klas Olsson, Storgatan 1, {Preamble}");

        resume.LatestRole.ShouldBe("Backend-utvecklare");
        resume.TopSkills.ShouldNotBeNull();
        string.Join(" ", resume.TopSkills!).ShouldNotContain("Storgatan");
        (resume.LatestRole ?? string.Empty).ShouldNotContain("Storgatan");
    }
}
