using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// The ONE home for the knowledge piece "is this canonical entry buildable, and if not, which
/// code" (#1060 D3(β) CTO-bind §3.1, 2026-08-01). <see cref="Resume.ValidateContent"/> CALLS
/// these readers — it does not parallel them — so there is exactly one place the per-entry rule
/// is written (Hunt/Thomas 1999 ch. 7: DRY as one home per knowledge piece, not as
/// text deduplication).
///
/// <para><b>The surface is per ENTRY KIND, not per ARM (ISP — Martin 2017 ch. 10).</b> Two
/// readers, one per kind, each returning the same <see cref="Result"/> — the same
/// <see cref="DomainError"/>, the same code, the same Swedish message — the aggregate would have
/// returned for that entry. The individual constraint codes are not surfaced as an arm per
/// constraint: a caller asks "is this entry buildable" and receives one answer, never a surface
/// that mirrors this file's own implementation. Exposing an arm per constraint would hand every
/// client a surface it does not use and would freeze the arm list into the public API. The code
/// ITSELF is not internal and must not be described as though it were — it is a published Domain
/// contract: <c>AutoPromoteGate</c> transports it, the handler logs it as <c>BlockDetail</c>, the
/// layout corpus prints it in a <c>Domain code</c> column and tests assert on the string. It is
/// the SHAPE of this API that is per entry kind, never the visibility of the codes.</para>
///
/// <para><b>No aggregate invariant is relaxed, and that is the point of the split.</b>
/// <see cref="Resume.CreateFromParsed"/> still refuses content whose entries do not pass — this
/// type changes WHO ELSE can ask the question, never WHAT the aggregate accepts. A caller that
/// wants to act on a non-buildable entry (route it, report it) must do so BEFORE handing content
/// to the aggregate; the aggregate never sees a bad entry either way. That preserves CLAUDE.md
/// §2.2 exactly: the aggregate protects its invariant, the Application decides what to hand it.
/// The refused alternative was an Application-side pre-filter that re-types the predicate — a
/// hand copy of a Domain invariant outside the Domain (CLAUDE.md §2.2, §5), and the exact defect
/// removed from <c>WellFormedPromotedExperience</c> one PR earlier.</para>
///
/// <para><b>The discriminator for prose elsewhere, written down once and only here.</b> Text
/// saying <c>ValidateContent</c> REQUIRES or REFUSES something is still true after this split —
/// it calls this type, so it refuses exactly what it refused before, and the corpus report, the
/// block-reason enum and the client schema all rest on that gate reading. Text saying
/// <c>ValidateContent</c> DECLARES a per-entry code is false as of #1060 D3(β-2); those homes
/// were repaired in that PR. If you are auditing an attribution somewhere else in the tree, that
/// is the distinction to apply — and it lives here, beside the rule, rather than at the call site,
/// because a reader arriving from the corpus emitter or the client schema is looking for the
/// rule.</para>
///
/// <para><b>Whole-document rules are NOT here.</b> Name, contact, summary, preamble, skills,
/// languages, skill-group references and section entries are validated by
/// <c>ValidateContent</c> itself, because they are properties of the document rather than of an
/// entry. A caller that treats a success from this type as "the CV is valid" is reading a
/// per-entry answer as a document-level one.</para>
///
/// <para><b>Why public, when today the aggregate is the only caller.</b> The decomposition
/// stands on SRP alone (Martin 2017 ch. 7): <c>ValidateContent</c> mixed the per-entry rule
/// with the document-level ones, and separating them is justified whether or not anything
/// else ever asks. The intended second caller is #1060 D3(β)'s Application-side router, which
/// is NOT next and may never be built: since β-1 no layout class in the corpus blocks on
/// <c>IncompleteContent</c> at all, so the trigger that licenses routing (bind R2,
/// 2026-07-25) is unfired, and the class that would fire it is unrepresented until β-3
/// authors it. An earlier revision of this paragraph told a reader who found no caller
/// outside <c>Resume.ValidateContent</c> that the sequencing bind "was broken and this type
/// should be inlined back". That instruction is WITHDRAWN — the condition is the expected
/// state, not a breach (CTO-bind 2026-08-02 §5). The per-ENTRY-KIND surface is decided, not
/// provisional on the router.</para>
///
/// <para><b>Null is not guarded, and the reason is exposure rather than behaviour
/// preservation.</b> Every call site today is inside the aggregate's own loop over a list the
/// Domain owns, under solution-wide NRT with <c>TreatWarningsAsErrors</c>, and no <c>src/</c>
/// path produces a null element — <c>ResumeContentMapper.ToDomain</c> would already throw in the
/// mapper. Contrast <see cref="ResumeContentLinearizer.Linearize"/>, same folder and same
/// <c>public static</c> shape, which DOES <c>ArgumentNullException.ThrowIfNull</c>: it is the
/// entry point of a cross-layer API reached from outer layers, and that is the difference — not
/// a house style this file quietly departs from. <b>When a caller outside the Domain passes a
/// list an outer layer built, the exposure profile becomes <c>Linearize</c>'s and the guard
/// belongs here too — <c>ArgumentNullException.ThrowIfNull</c>, never a <c>Result</c> failure: a
/// null element is a programming error, not a content-validity fact.</b> An earlier revision
/// argued the absence from behaviour preservation instead; that reasoning was withdrawn because
/// the falsifier it appealed to — the layout corpus — never passes a null entry and therefore
/// cannot measure the question either way.</para>
/// </summary>
public static class ResumeEntryBuildability
{
    /// <summary>
    /// The per-entry rule for one work-experience entry, in the aggregate's own evaluation
    /// order. Returns the first failing constraint, or success.
    /// </summary>
    public static Result Validate(Experience experience)
    {
        if (string.IsNullOrWhiteSpace(experience.Company))
            return Result.Failure(DomainError.Validation(
                "Resume.ExperienceCompanyRequired", "Företagsnamn krävs på erfarenhet."));

        // #855: cap the label fields (200, client .max parity).
        if (experience.Company.Length > 200)
            return Result.Failure(DomainError.Validation(
                "Resume.ExperienceCompanyTooLong", "Företagsnamn får vara max 200 tecken."));

        if (string.IsNullOrWhiteSpace(experience.Role))
            return Result.Failure(DomainError.Validation(
                "Resume.ExperienceRoleRequired", "Roll krävs på erfarenhet."));

        if (experience.Role.Length > 200)
            return Result.Failure(DomainError.Validation(
                "Resume.ExperienceRoleTooLong", "Roll får vara max 200 tecken."));

        // #855: cap the description prose body (2000, client .max parity — and closes an
        // internal inconsistency: the sibling prose field Summary is already capped 2000).
        // Length-only; Description is optional. Inline, NOT coupled to Summary's 2000 (they may
        // diverge — spurious DRY otherwise).
        if (experience.Description is { Length: > 2_000 })
            return Result.Failure(DomainError.Validation(
                "Resume.ExperienceDescriptionTooLong",
                "Beskrivning får vara max 2 000 tecken."));

        // Honest date absence (CTO-bind 5a-pre): end-before-start is an error only when
        // BOTH are present. A null start with a set end ("examen 2020") VALIDATES, but
        // v1 display gates on StartDate-presence: a lone EndDate is stored yet not
        // rendered/linearized (RawPeriod or nothing shows instead) — degrades honestly,
        // never fabricates. Whether display should honor a lone EndDate is CTO-triage
        // for the auto-promote PR (which itself never emits end-only entries).
        if (experience.StartDate is { } start && experience.EndDate is { } end && end < start)
            return Result.Failure(DomainError.Validation(
                "Resume.ExperienceDatesInvalid",
                "Slutdatum får inte vara före startdatum."));

        if (experience.RawPeriod is { Length: > 100 })
            return Result.Failure(DomainError.Validation(
                "Resume.ExperienceRawPeriodTooLong",
                "Periodtext får vara max 100 tecken."));

        return Result.Success();
    }

    /// <summary>
    /// The per-entry rule for one education entry, in the aggregate's own evaluation order.
    /// Returns the first failing constraint, or success.
    /// </summary>
    public static Result Validate(Education education)
    {
        if (string.IsNullOrWhiteSpace(education.Institution))
            return Result.Failure(DomainError.Validation(
                "Resume.EducationInstitutionRequired", "Lärosäte krävs på utbildning."));

        // #855: cap the label fields (200, client .max parity).
        if (education.Institution.Length > 200)
            return Result.Failure(DomainError.Validation(
                "Resume.EducationInstitutionTooLong", "Lärosäte får vara max 200 tecken."));

        if (string.IsNullOrWhiteSpace(education.Degree))
            return Result.Failure(DomainError.Validation(
                "Resume.EducationDegreeRequired", "Examen krävs på utbildning."));

        if (education.Degree.Length > 200)
            return Result.Failure(DomainError.Validation(
                "Resume.EducationDegreeTooLong", "Examen får vara max 200 tecken."));

        // Honest date absence (CTO-bind 5a-pre) — parity with the experience rule above.
        if (education.StartDate is { } eduStart && education.EndDate is { } eduEnd
            && eduEnd < eduStart)
            return Result.Failure(DomainError.Validation(
                "Resume.EducationDatesInvalid",
                "Slutdatum får inte vara före startdatum."));

        if (education.RawPeriod is { Length: > 100 })
            return Result.Failure(DomainError.Validation(
                "Resume.EducationRawPeriodTooLong",
                "Periodtext får vara max 100 tecken."));

        return Result.Success();
    }
}
