using System.Diagnostics.CodeAnalysis;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes.Events;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Domain.Resumes;

[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "Resume är domänspråk per BUILD.md §5.1; VB-konflikt accepterad.")]
public sealed class Resume : AggregateRoot<ResumeId>
{
    public JobSeekerId JobSeekerId { get; private set; }
    public string Name { get; private set; } = null!;
    public ResumeLanguage Language { get; private set; } = ResumeLanguage.Sv;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    // Fas 4b PR-3 (ADR 0096, CTO-bind D1/D5d/D9): non-PII source metadata + template
    // options as plain columns on the root (ADR 0059 parity) — every field enumerated
    // or a timestamp, deliberately no free text (pinned by ResumeRootPlainColumnGuardTests).
    // Origin is set by construction only (CreateFromParsed => Import, Create => Template;
    // pre-PR-3 rows carry the honest Legacy default — provenance is never fabricated).
    public ResumeSourceOrigin Origin { get; private set; } = ResumeSourceOrigin.Legacy;

    // One-way adoption stamp (CTO-bind D9; DeletedAt/AppliedAt idiom — a nullable
    // timestamp self-documents the one-way semantics and keeps WHEN, which the
    // raise-only domain event would otherwise lose). Flipped exactly once via Adopt().
    public DateTimeOffset? AdoptedAt { get; private set; }

    /// <summary>True once the CV's design has been adopted (one-way, <see cref="Adopt"/>).</summary>
    public bool IsAdopted => AdoptedAt is not null;

    // Fas 4b PR-9c (ADR 0100 §D5 / ADR 0103, CTO-bind F1=L-B): the parsed-artifact this
    // canonical CV was promoted from — the provenance the DQ5b design carried transiently on
    // ResumeCreatedFromParsedResumeDomainEvent is now PERSISTED, because a consumer finally
    // exists: it is the cascade key that couples a promoted original file
    // (resume_files.parsed_resume_id, already indexed) to this Resume, so deleting the CV erases
    // its stored original (the resume-lifecycle link ADR 0100 §D5 named open). Set by
    // construction in CreateFromParsed (Origin/AdoptedAt idiom); null for Template/Legacy CVs
    // never promoted, AND for pre-PR-9c rows whose provenance was never persisted
    // (un-backfillable — those originals stay erasable via account-hard-delete only, ADR 0103
    // F2). Non-PII strongly-typed machine id — pinned by ResumeRootPlainColumnGuardTests.
    public ParsedResumeId? SourceParsedResumeId { get; private set; }

    public CvTemplateOptions TemplateOptions { get; private set; } = CvTemplateOptions.Default;

    // Denormaliserade projektion-fält per ADR 0059 — drivs av ADR 0049
    // envelope-encryption som gör Content opaque för SQL. Mutation sker
    // endast via ApplyDenormalizedProjection (synkront i samma aggregat-metod).
    public string? LatestRole { get; private set; }
    public int SectionCount { get; private set; }
    private readonly List<string> _topSkills = [];
    public IReadOnlyList<string> TopSkills => _topSkills.AsReadOnly();

    private readonly List<ResumeVersion> _versions = [];
    public IReadOnlyList<ResumeVersion> Versions => _versions.AsReadOnly();

    // Fas 4b PR-4 (ADR 0093 §D2(e), CTO-bind PR-4 Q1/Q2): the DEK-free finding-status
    // ledger — a child collection INSIDE the aggregate so a decision and the content
    // change that invalidates it serialize on the same xmin token (never eventual
    // consistency inside one aggregate). Bounded: ≤ criteria-count per rubric version
    // (user decisions + PR-8 engine-seeded Open rows, ADR 0097 amendment 2026-07-09).
    // Loaded explicitly (Include) on the write path and the review-merge read path
    // only — never materialized on list queries (§3.6; the hub badge is a translated
    // COUNT projection, not a load).
    private readonly List<ResumeFindingStatus> _findingStatuses = [];
    public IReadOnlyList<ResumeFindingStatus> FindingStatuses => _findingStatuses.AsReadOnly();

    // Fas 4b PR-8 (ADR 0093 §D5(b), CTO-bind PR-8 Q1): the rubric version the ledger was
    // last reconciled against (AdoptedAt idiom — a nullable machine token, no free text).
    // Null (pre-PR-8 rows / never-reconciled) is an honest "not reviewed": the hub badge
    // may render a count ONLY when this stamp equals the current rubric version, because
    // "0 Open rows" alone cannot distinguish reviewed-and-clean from never-reviewed
    // (§5 never mis-report). Stamped exclusively by ReconcileFindingStatuses.
    public string? ReviewedRubricVersion { get; private set; }

    /// <summary>
    /// Returnerar Master-versionen. Kastar <see cref="DomainException"/> om invarianten
    /// "exakt en aktiv Master" bryts (audit-trail-kontextuell signal istället för
    /// generic <c>InvalidOperationException</c> från <c>Single()</c>).
    /// </summary>
    public ResumeVersion MasterVersion
    {
        get
        {
            var masters = _versions
                .Where(v => v.Kind == ResumeVersionKind.Master && v.DeletedAt is null)
                .ToList();

            return masters.Count switch
            {
                1 => masters[0],
                0 => throw new DomainException(
                    "Resume.MasterInvariantBroken",
                    $"Resume {Id} saknar aktiv Master-version."),
                _ => throw new DomainException(
                    "Resume.MasterInvariantBroken",
                    $"Resume {Id} har {masters.Count} aktiva Master-versioner, exakt 1 förväntat."),
            };
        }
    }

    // EF Core constructor
    private Resume() { }

    private Resume(
        ResumeId id,
        JobSeekerId jobSeekerId,
        string name,
        ResumeSourceOrigin origin,
        DateTimeOffset now) : base(id)
    {
        JobSeekerId = jobSeekerId;
        Name = name;
        Origin = origin;
        // TemplateOptions: the property's field initializer already assigns a fresh
        // CvTemplateOptions.Default per instance.
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static Result<Resume> Create(
        JobSeekerId jobSeekerId,
        string? name,
        string? fullName,
        IDateTimeProvider clock)
    {
        if (jobSeekerId == default)
            return Result.Failure<Resume>(
                DomainError.Validation("Resume.JobSeekerIdRequired", "JobSeekerId krävs."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Resume>(
                DomainError.Validation("Resume.NameRequired", "Namn på CV är obligatoriskt."));

        if (name.Length > 200)
            return Result.Failure<Resume>(
                DomainError.Validation("Resume.NameTooLong", "Namn får vara max 200 tecken."));

        if (string.IsNullOrWhiteSpace(fullName))
            return Result.Failure<Resume>(
                DomainError.Validation("Resume.FullNameRequired", "Fullständigt namn krävs för initial Master-version."));

        if (fullName.Length > 200)
            return Result.Failure<Resume>(
                DomainError.Validation("Resume.FullNameTooLong", "Fullständigt namn får vara max 200 tecken."));

        var now = clock.UtcNow;
        var id = ResumeId.New();
        // Origin by construction (ADR 0096): Create is the in-app/template path — the
        // handoff's "mall"-CV ("Börja från profilen"). Immutable thereafter.
        var resume = new Resume(id, jobSeekerId, name.Trim(), ResumeSourceOrigin.Template, now);

        var initialContent = ResumeContent.Empty(fullName.Trim());
        var master = ResumeVersion.CreateMaster(initialContent, clock);
        resume._versions.Add(master);
        resume.ApplyDenormalizedProjection(initialContent);

        resume.RaiseDomainEvent(new ResumeCreatedDomainEvent(id, jobSeekerId, resume.Name, now));
        resume.RaiseDomainEvent(new ResumeVersionCreatedDomainEvent(
            id, master.Id, ResumeVersionKind.Master, now));

        return Result.Success(resume);
    }

    /// <summary>
    /// Skapar ett kanoniskt <c>Resume</c> genom att befordra en <c>ParsedResume</c>
    /// staging-artefakt (Fas 4 STEG A, CTO DQ5b). Till skillnad från <see cref="Create"/>
    /// (som föder en tom Master) konstrueras Master-versionen direkt ur det
    /// användar-godkända, gap-ifyllda <paramref name="content"/> i ETT validerat steg.
    /// Innehållet valideras mot samma strikta <see cref="ValidateContent"/> som
    /// Master-uppdateringar. Höjer ett provenance-event som länkar
    /// <paramref name="sourceParsedResumeId"/> till det nya CV:t OCH persisterar länken på
    /// <see cref="SourceParsedResumeId"/> (PR-9c, ADR 0100 §D5 — DQ5b:s "enbart event, ingen
    /// kolumn" vänd nu när en konsument finns: cascade-nyckeln för per-CV original-radering).
    /// Aggregatet konstrueras giltigt i ett steg (DDD §2.2).
    /// </summary>
    public static Result<Resume> CreateFromParsed(
        JobSeekerId jobSeekerId,
        string? name,
        ResumeContent content,
        ParsedResumeId sourceParsedResumeId,
        IDateTimeProvider clock)
    {
        if (jobSeekerId == default)
            return Result.Failure<Resume>(
                DomainError.Validation("Resume.JobSeekerIdRequired", "JobSeekerId krävs."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Resume>(
                DomainError.Validation("Resume.NameRequired", "Namn på CV är obligatoriskt."));

        if (name.Length > 200)
            return Result.Failure<Resume>(
                DomainError.Validation("Resume.NameTooLong", "Namn får vara max 200 tecken."));

        var contentValidation = ValidateContent(content);
        if (contentValidation.IsFailure)
            return Result.Failure<Resume>(contentValidation.Error);

        var now = clock.UtcNow;
        var id = ResumeId.New();
        // Origin by construction (ADR 0096): promoting a parsed import IS the import
        // path — "promote sets källa=import" is satisfied here, not by a setter.
        var resume = new Resume(id, jobSeekerId, name.Trim(), ResumeSourceOrigin.Import, now);
        // PR-9c (ADR 0100 §D5): persist the parsed→resume provenance the event carries
        // transiently — it is the cascade key for per-CV original-file erasure. Set by
        // construction (before events), mirroring Origin.
        resume.SourceParsedResumeId = sourceParsedResumeId;

        var master = ResumeVersion.CreateMaster(content, clock);
        resume._versions.Add(master);
        resume.ApplyDenormalizedProjection(content);

        resume.RaiseDomainEvent(new ResumeCreatedDomainEvent(id, jobSeekerId, resume.Name, now));
        resume.RaiseDomainEvent(new ResumeVersionCreatedDomainEvent(
            id, master.Id, ResumeVersionKind.Master, now));
        resume.RaiseDomainEvent(new ResumeCreatedFromParsedResumeDomainEvent(
            id, sourceParsedResumeId, jobSeekerId, now));

        return Result.Success(resume);
    }

    public Result Rename(string? name, IDateTimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(
                DomainError.Validation("Resume.NameRequired", "Namn på CV är obligatoriskt."));

        if (name.Length > 200)
            return Result.Failure(
                DomainError.Validation("Resume.NameTooLong", "Namn får vara max 200 tecken."));

        Name = name.Trim();
        UpdatedAt = clock.UtcNow;
        return Result.Success();
    }

    public Result UpdateMasterContent(ResumeContent content, IDateTimeProvider clock)
    {
        var validation = ValidateContent(content);
        if (validation.IsFailure)
            return validation;

        var master = MasterVersion;
        var now = clock.UtcNow;
        master.UpdateContent(content, clock);
        ApplyDenormalizedProjection(content);

        // Fas 4b PR-4 (ADR 0093 §D2(e), CTO-bind PR-4 Q2/Q3): a content change
        // invalidates previously-resolved findings — the text they were resolved
        // against may have moved. Stamped HERE, transactionally with the content
        // write, so a resolved badge can never survive a change it has not seen.
        // Only Resolved rows go stale (Ignored is a content-independent rule
        // opt-out; Open has nothing to invalidate). Requires the caller to have
        // loaded the FindingStatuses collection (write-path Include contract —
        // pinned by the UpdateMasterContent handler staleness test). The parent
        // UpdatedAt stamp below is what fires the xmin-checked UPDATE that
        // serializes this against a concurrent SetFindingStatus (CTO-bind Q2).
        foreach (var finding in _findingStatuses)
        {
            finding.MarkStaleIfResolved(now);
        }

        UpdatedAt = now;
        RaiseDomainEvent(new ResumeContentUpdatedDomainEvent(Id, master.Id, now));
        return Result.Success();
    }

    /// <summary>
    /// Skapar en ny Tailored-version (en variant skräddarsydd mot en specifik annons)
    /// med <paramref name="content"/>. Innehållet valideras mot samma strikta
    /// <see cref="ValidateContent"/> som Master-innehåll. Påverkar varken Master-versionen,
    /// invarianten "exakt en aktiv Master" eller de denormaliserade projektion-fälten
    /// (ADR 0059 — endast Master-innehåll driver dem). Returnerar den nya versionens id
    /// så att anroparen kan referera den (t.ex. <c>Application.AttachResumeVersion</c>).
    /// </summary>
    public Result<ResumeVersionId> CreateTailored(ResumeContent content, IDateTimeProvider clock)
    {
        var validation = ValidateContent(content);
        if (validation.IsFailure)
            return Result.Failure<ResumeVersionId>(validation.Error);

        var tailored = ResumeVersion.CreateTailored(content, clock);
        _versions.Add(tailored);
        UpdatedAt = clock.UtcNow;
        RaiseDomainEvent(new ResumeVersionCreatedDomainEvent(
            Id, tailored.Id, ResumeVersionKind.Tailored, clock.UtcNow));
        return Result.Success(tailored.Id);
    }

    public Result SetLanguage(ResumeLanguage language, IDateTimeProvider clock)
    {
        if (language is null)
            return Result.Failure(DomainError.Validation(
                "Resume.LanguageRequired", "Språk krävs."));

        if (Language == language)
            return Result.Success();

        Language = language;
        UpdatedAt = clock.UtcNow;
        RaiseDomainEvent(new ResumeLanguageChangedDomainEvent(Id, language, clock.UtcNow));
        return Result.Success();
    }

    /// <summary>
    /// Adopts the CV's design ("Adoptera min design", handoff §10.2 — Fas 4b CTO-bind
    /// D9, ADR 0096). ONE-WAY: stamps <see cref="AdoptedAt"/> exactly once; a second
    /// call is a Conflict (parity with <c>ParsedResume.Promote</c> — not idempotent).
    /// Precondition: only an imported CV can be adopted (adoption means recreating an
    /// UPLOADED file's design in-app; a Template-origin CV is already app-rendered,
    /// and a Legacy row's origin is unknown — both refused). The handoff's "Ångra
    /// adoptionen" is a pre-commit UI affordance, not a domain-state reversal (D9
    /// governs). Raise-only event; the audit row for the user action arrives with the
    /// Fas C adopt command (PR-11) — no command calls this yet.
    /// </summary>
    public Result Adopt(IDateTimeProvider clock)
    {
        if (AdoptedAt is not null)
            return Result.Failure(DomainError.Conflict(
                "Resume.AlreadyAdopted", "CV:t är redan adopterat."));

        if (Origin != ResumeSourceOrigin.Import)
            return Result.Failure(DomainError.Validation(
                "Resume.OnlyImportedCanBeAdopted",
                "Endast importerade CV:n kan adopteras."));

        var now = clock.UtcNow;
        AdoptedAt = now;
        UpdatedAt = now;
        RaiseDomainEvent(new ResumeAdoptedDomainEvent(Id, now));
        return Result.Success();
    }

    /// <summary>
    /// Replaces the CV's template/display options (Fas 4b PR-3, ADR 0096 — the other
    /// half of the <see cref="CvTemplateOptions"/> lifecycle; the builder UI arrives
    /// in PR-8b). The VO's members are type-guaranteed valid; the only reachable
    /// invalid states are a null options object or a null member (positional record —
    /// no ctor guard, <c>Preferences</c> precedent), both refused here at the single
    /// mutation path. Unchanged options are a no-op without an event
    /// (<see cref="SetLanguage"/> parity).
    /// </summary>
    public Result ChangeTemplateOptions(CvTemplateOptions? options, IDateTimeProvider clock)
    {
        if (options is null)
            return Result.Failure(DomainError.Validation(
                "Resume.TemplateOptionsRequired", "Mallinställningar krävs."));

        if (!options.IsComplete)
            return Result.Failure(DomainError.Validation(
                "Resume.TemplateOptionsIncomplete", "Alla mallinställningar måste anges."));

        if (TemplateOptions == options)
            return Result.Success();

        var now = clock.UtcNow;
        TemplateOptions = options;
        UpdatedAt = now;
        RaiseDomainEvent(new ResumeTemplateOptionsChangedDomainEvent(Id, now));
        return Result.Success();
    }

    /// <summary>
    /// Records the user's decision on one review finding (Fas 4b PR-4, ADR 0093 §D2(e);
    /// handoff §5.3 "markera som klar" / "ignorera regeln"). Upserts the ledger row keyed
    /// (<paramref name="rubricVersion"/>, <paramref name="criterionId"/>) — the
    /// USER-DECISION mutation path for the finding-status child collection (CTO-bind
    /// PR-4 Q1/Q2; since PR-8 the engine-driven counterpart is
    /// <see cref="ReconcileFindingStatuses"/>, ADR 0097 amendment 2026-07-09 — exactly
    /// these two paths mutate the collection). A fresh decision clears any staleness
    /// stamp. The fingerprint is server-derived by the command handler from the engine's
    /// current finding (never client-submitted, ADR 0074 Invariant 2); this method
    /// validates shape, not provenance.
    /// </summary>
    public Result SetFindingStatus(
        string? rubricVersion,
        string? criterionId,
        ReviewFindingStatus? status,
        string? targetFingerprint,
        IDateTimeProvider clock)
    {
        if (!IsValidRubricVersion(rubricVersion))
            return Result.Failure(DomainError.Validation(
                "Resume.RubricVersionInvalid", "Rubrikversion måste ha formen major.minor.patch."));

        if (!IsValidCriterionId(criterionId))
            return Result.Failure(DomainError.Validation(
                "Resume.CriterionIdInvalid", "Kriterie-id måste vara en bokstav följd av 1–2 siffror."));

        if (status is null)
            return Result.Failure(DomainError.Validation(
                "Resume.FindingStatusRequired", "Status krävs."));

        if (!IsValidFingerprint(targetFingerprint))
            return Result.Failure(DomainError.Validation(
                "Resume.FingerprintInvalid", "Fingeravtrycket måste vara 64 hexadecimala tecken."));

        var now = clock.UtcNow;
        var existing = _findingStatuses.FirstOrDefault(f =>
            f.RubricVersion == rubricVersion && f.CriterionId == criterionId);

        if (existing is null)
        {
            _findingStatuses.Add(ResumeFindingStatus.Create(
                rubricVersion!, criterionId!, status, targetFingerprint!, now));
        }
        else
        {
            existing.ChangeStatus(status, targetFingerprint!, now);
        }

        // The parent stamp is LOAD-BEARING for concurrency (CTO-bind Q2): touching the
        // resumes row is what makes EF issue the xmin-checked UPDATE, serializing a
        // concurrent SetFindingStatus/UpdateMasterContent pair on the aggregate token.
        // A child-only INSERT would bypass the token (the unique index is only the
        // degraded backstop).
        UpdatedAt = now;
        RaiseDomainEvent(new ResumeFindingStatusChangedDomainEvent(
            Id, rubricVersion!, criterionId!, status, now));
        return Result.Success();
    }

    /// <summary>
    /// Reconciles the finding-status ledger with the engine's current review after a
    /// content write (Fas 4b PR-8, ADR 0093 §D5(b); ADR 0097 amendment 2026-07-09). The
    /// engine-driven counterpart to the user-driven <see cref="SetFindingStatus"/>:
    /// seeds an Open row per actionable finding that has no row yet, refreshes the
    /// fingerprint on an Open row whose finding moved, drops Open rows whose finding is
    /// gone, and NEVER touches a user decision (Resolved/Ignored rows keep their
    /// lifecycle: <see cref="SetFindingStatus"/> + <c>MarkStaleIfResolved</c>). Scoped
    /// strictly to <paramref name="rubricVersion"/> — rows of other versions are never
    /// read or written (ADR 0097 §5 version-non-carry). Stamps
    /// <see cref="ReviewedRubricVersion"/> even when the actionable set is empty:
    /// reviewed-and-clean is a real, badge-visible state. Duplicate criterion ids in the
    /// input dedupe first-wins. All inputs are server-derived by the reconciler service
    /// (never client-submitted, ADR 0074 Invariant 2); this method validates shape, not
    /// provenance.
    /// </summary>
    public Result ReconcileFindingStatuses(
        string? rubricVersion,
        IReadOnlyList<ReviewFindingSnapshot>? actionableFindings,
        IDateTimeProvider clock)
    {
        if (!IsValidRubricVersion(rubricVersion))
            return Result.Failure(DomainError.Validation(
                "Resume.RubricVersionInvalid", "Rubrikversion måste ha formen major.minor.patch."));

        if (actionableFindings is null)
            return Result.Failure(DomainError.Validation(
                "Resume.ActionableFindingsRequired", "Fyndlistan krävs (tom lista är giltig)."));

        // Validate every snapshot BEFORE mutating anything (all-or-nothing).
        foreach (var snapshot in actionableFindings)
        {
            if (!IsValidCriterionId(snapshot.CriterionId))
                return Result.Failure(DomainError.Validation(
                    "Resume.CriterionIdInvalid", "Kriterie-id måste vara en bokstav följd av 1–2 siffror."));

            if (!IsValidFingerprint(snapshot.TargetFingerprint))
                return Result.Failure(DomainError.Validation(
                    "Resume.FingerprintInvalid", "Fingeravtrycket måste vara 64 hexadecimala tecken."));
        }

        // Dedupe first-wins: the reconciler unions verdicts per criterion, so a
        // duplicate is a caller artifact, never two distinct findings.
        var byCriterion = new Dictionary<string, ReviewFindingSnapshot>(StringComparer.Ordinal);
        foreach (var snapshot in actionableFindings)
            byCriterion.TryAdd(snapshot.CriterionId, snapshot);

        var now = clock.UtcNow;

        foreach (var snapshot in byCriterion.Values)
        {
            var existing = _findingStatuses.FirstOrDefault(f =>
                f.RubricVersion == rubricVersion && f.CriterionId == snapshot.CriterionId);

            if (existing is null)
            {
                _findingStatuses.Add(ResumeFindingStatus.Create(
                    rubricVersion!, snapshot.CriterionId, ReviewFindingStatus.Open,
                    snapshot.TargetFingerprint, now));
            }
            else if (existing.Status == ReviewFindingStatus.Open
                && existing.TargetFingerprint != snapshot.TargetFingerprint)
            {
                // The finding moved (same criterion, new evidence) — refresh the
                // identity, keep it Open. Resolved/Ignored rows are user decisions and
                // are NEVER touched here (their lifecycle is SetFindingStatus +
                // MarkStaleIfResolved).
                existing.ChangeStatus(ReviewFindingStatus.Open, snapshot.TargetFingerprint, now);
            }
        }

        // Drop Open rows whose finding is gone at THIS version (fixed by an edit or no
        // longer assessable). User decisions survive; other versions' rows are never
        // touched (ADR 0097 §5 version-non-carry).
        _findingStatuses.RemoveAll(f =>
            f.RubricVersion == rubricVersion
            && f.Status == ReviewFindingStatus.Open
            && !byCriterion.ContainsKey(f.CriterionId));

        // Reviewed-and-clean is a real state: the stamp is what lets the hub badge
        // honestly distinguish "0 att åtgärda" from "aldrig granskad" (§5).
        ReviewedRubricVersion = rubricVersion;

        // Parent stamp is LOAD-BEARING for concurrency (PR-4 CTO-bind Q2 parity): the
        // xmin-checked UPDATE serializes this against a concurrent SetFindingStatus.
        UpdatedAt = now;

        var openCount = _findingStatuses.Count(f =>
            f.RubricVersion == rubricVersion && f.Status == ReviewFindingStatus.Open);
        RaiseDomainEvent(new ResumeReviewReconciledDomainEvent(Id, rubricVersion!, openCount, now));
        return Result.Success();
    }

    // "major.minor.patch", each part 1–4 digits — a bounded machine token (parity with
    // the Application-side RubricVersion.TryParse), deliberately no free text.
    private static bool IsValidRubricVersion([NotNullWhen(true)] string? version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 14)
            return false;

        var parts = version.Split('.');
        return parts.Length == 3
            && parts.All(p => p.Length is >= 1 and <= 4 && p.All(char.IsAsciiDigit));
    }

    // One uppercase category letter + 1–2 digits ("A1".."E12") — the rubric's id shape.
    private static bool IsValidCriterionId([NotNullWhen(true)] string? id) =>
        id is { Length: 2 or 3 }
        && char.IsAsciiLetterUpper(id[0])
        && id[1..].All(char.IsAsciiDigit);

    // Full SHA-256 as lowercase hex (CTO-bind PR-4 Q4 — never truncated).
    private static bool IsValidFingerprint([NotNullWhen(true)] string? fingerprint) =>
        fingerprint is { Length: 64 }
        && fingerprint.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    /// <summary>
    /// Soft-raderar en version. Master-versionen kan aldrig raderas (bryter invarianten
    /// "exakt en Master"). Versioner som refereras av öppna ansökningar kan inte heller
    /// raderas — handlern är ansvarig för uppslag och passerar resultatet via flaggan.
    /// </summary>
    public Result DeleteVersion(
        ResumeVersionId versionId,
        bool isReferencedByOpenApplication,
        IDateTimeProvider clock)
    {
        var version = _versions.FirstOrDefault(v => v.Id == versionId && v.DeletedAt is null);
        if (version is null)
            return Result.Failure(DomainError.NotFound(nameof(ResumeVersion), versionId));

        if (version.Kind == ResumeVersionKind.Master)
            return Result.Failure(DomainError.Validation(
                "Resume.MasterCannotBeDeleted",
                "Master-versionen kan inte raderas. Radera hela CV:t istället."));

        if (isReferencedByOpenApplication)
            return Result.Failure(DomainError.Conflict(
                "Resume.VersionInUse",
                "Versionen är kopplad till en öppen ansökan och kan inte raderas."));

        version.SoftDelete(clock);
        UpdatedAt = clock.UtcNow;
        RaiseDomainEvent(new ResumeVersionDeletedDomainEvent(Id, version.Id, clock.UtcNow));
        return Result.Success();
    }

    public void SoftDelete(IDateTimeProvider clock)
    {
        if (DeletedAt.HasValue) return;

        DeletedAt = clock.UtcNow;
        foreach (var v in _versions)
            v.SoftDelete(clock);
        RaiseDomainEvent(new ResumeDeletedDomainEvent(Id, clock.UtcNow));
    }

    private static (string? latestRole, int sectionCount, IReadOnlyList<string> topSkills)
        ComputeDenormalizedProjection(ResumeContent content)
    {
        var latestRole = content.Experiences
            .OrderByDescending(e => e.StartDate)
            .FirstOrDefault()?.Role;

        var sectionCount =
            (!string.IsNullOrWhiteSpace(content.Summary) ? 1 : 0) +
            (content.Experiences.Count > 0 ? 1 : 0) +
            (content.Educations.Count > 0 ? 1 : 0) +
            (content.Skills.Count > 0 ? 1 : 0);

        var topSkills = content.Skills
            .Take(5)
            .Select(s => s.Name)
            .ToList();

        return (latestRole, sectionCount, topSkills);
    }

    private void ApplyDenormalizedProjection(ResumeContent content)
    {
        var (latestRole, sectionCount, topSkills) = ComputeDenormalizedProjection(content);
        LatestRole = latestRole;
        SectionCount = sectionCount;
        _topSkills.Clear();
        _topSkills.AddRange(topSkills);
    }

    private static Result ValidateContent(ResumeContent content)
    {
        if (content is null)
            return Result.Failure(DomainError.Validation(
                "Resume.ContentRequired", "Innehåll krävs."));

        if (string.IsNullOrWhiteSpace(content.PersonalInfo.FullName))
            return Result.Failure(DomainError.Validation(
                "Resume.FullNameRequired", "Fullständigt namn krävs."));

        if (content.PersonalInfo.FullName.Length > 200)
            return Result.Failure(DomainError.Validation(
                "Resume.FullNameTooLong", "Fullständigt namn får vara max 200 tecken."));

        if (content.Summary is { Length: > 2_000 })
            return Result.Failure(DomainError.Validation(
                "Resume.SummaryTooLong", "Sammanfattning får vara max 2 000 tecken."));

        foreach (var skill in content.Skills)
        {
            if (string.IsNullOrWhiteSpace(skill.Name))
                return Result.Failure(DomainError.Validation(
                    "Resume.SkillNameRequired", "Kompetensnamn krävs."));

            if (skill.YearsExperience is { } years && (years < 0 || years > 70))
                return Result.Failure(DomainError.Validation(
                    "Resume.SkillYearsOutOfRange",
                    "Antal år erfarenhet måste vara mellan 0 och 70."));
        }

        foreach (var exp in content.Experiences)
        {
            if (string.IsNullOrWhiteSpace(exp.Company))
                return Result.Failure(DomainError.Validation(
                    "Resume.ExperienceCompanyRequired", "Företagsnamn krävs på erfarenhet."));

            if (string.IsNullOrWhiteSpace(exp.Role))
                return Result.Failure(DomainError.Validation(
                    "Resume.ExperienceRoleRequired", "Roll krävs på erfarenhet."));

            if (exp.EndDate is { } end && end < exp.StartDate)
                return Result.Failure(DomainError.Validation(
                    "Resume.ExperienceDatesInvalid",
                    "Slutdatum får inte vara före startdatum."));
        }

        foreach (var edu in content.Educations)
        {
            if (string.IsNullOrWhiteSpace(edu.Institution))
                return Result.Failure(DomainError.Validation(
                    "Resume.EducationInstitutionRequired", "Lärosäte krävs på utbildning."));

            if (string.IsNullOrWhiteSpace(edu.Degree))
                return Result.Failure(DomainError.Validation(
                    "Resume.EducationDegreeRequired", "Examen krävs på utbildning."));

            if (edu.EndDate is { } end && end < edu.StartDate)
                return Result.Failure(DomainError.Validation(
                    "Resume.EducationDatesInvalid",
                    "Slutdatum får inte vara före startdatum."));
        }

        // Fas 4b AppCopy superset (ADR 0095 D-E). Validation parity with the existing
        // rules: label fields are required-only (no max, like Company/Role/Skill.Name);
        // prose bodies are capped like Summary. Proficiency is type-guaranteed valid
        // (SmartEnum) so it needs no check.
        foreach (var language in content.Languages)
        {
            if (string.IsNullOrWhiteSpace(language.Name))
                return Result.Failure(DomainError.Validation(
                    "Resume.LanguageNameRequired", "Språknamn krävs."));
        }

        // Grouped-skills overlay reference invariant (ADR 0095 D-A): every group member
        // must reference a skill that exists in the flat, authoritative Skills list — no
        // dangling reference, so no phantom skill the user did not write (CLAUDE.md §5).
        // Not every skill need be grouped (design handoff P4). Skill names were already
        // validated non-empty above, so the set is clean.
        var skillNames = new HashSet<string>(
            content.Skills.Select(s => s.Name), StringComparer.Ordinal);

        foreach (var group in content.SkillGroups)
        {
            if (string.IsNullOrWhiteSpace(group.Name))
                return Result.Failure(DomainError.Validation(
                    "Resume.SkillGroupNameRequired", "Namn på kompetensgrupp krävs."));

            foreach (var member in group.Members)
            {
                if (!skillNames.Contains(member))
                    return Result.Failure(DomainError.Validation(
                        "Resume.SkillGroupMemberUnknown",
                        "En kompetensgrupp får bara referera kompetenser som finns i CV:t."));
            }
        }

        foreach (var section in content.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.Heading))
                return Result.Failure(DomainError.Validation(
                    "Resume.SectionHeadingRequired", "Rubrik krävs på sektion."));

            foreach (var entry in section.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Title))
                    return Result.Failure(DomainError.Validation(
                        "Resume.SectionEntryTitleRequired", "Titel krävs på sektionspost."));

                if (entry.Lines.Sum(l => l?.Length ?? 0) > 2_000)
                    return Result.Failure(DomainError.Validation(
                        "Resume.SectionEntryTooLong",
                        "En sektionspost får innehålla max 2 000 tecken."));
            }
        }

        return Result.Success();
    }
}
