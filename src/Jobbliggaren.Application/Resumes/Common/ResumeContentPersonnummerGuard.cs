using System.Text;
using Jobbliggaren.Application.Resumes.Queries;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Privacy;

namespace Jobbliggaren.Application.Resumes.Common;

/// <summary>
/// Shared personnummer guard for every write surface that accepts a user-submitted
/// <see cref="ResumeContentDto"/> (ADR 0074 Invariant 1 — "no MVP exception path";
/// CLAUDE.md §5, the highest-priority PII rule). Runs the SAME
/// <c>CollectFreeText → Normalize → Scan</c> chain the import/promote guard runs, over the
/// RAW submitted free text (so zero-width / NBSP / OCR-gap / Unicode-dash forms are all
/// covered by the Domain guard, #497/#498), and blocks with a Resume-scoped validation
/// failure when a personnummer/samordningsnummer is present. The outcome carries only
/// count/kinds, never the raw value. Deterministic, no AI (ADR 0071).
///
/// <para><b>Why the Application boundary, not the aggregate (CTO Q3 = Approach H):</b> this is a
/// cross-cutting input-sanitisation / at-rest policy over the RAW DTO free text — the same class
/// as anti-injection sanitisation — not a structural aggregate invariant (CLAUDE.md §2.2). The
/// <c>Resume</c> aggregate's <c>ValidateContent</c> owns structure; the guard operates on the
/// transport <see cref="ResumeContentDto"/> (the raw text the scanner needs), before
/// <c>ResumeContentMapper.ToDomain</c>. An architecture test
/// (<c>ResumeContentPersonnummerGuardTests</c>, #499/#650) requires EVERY command handler that
/// is a resume-content write surface to call this guard, keyed on the UNION of two probes:
/// its command carries a <see cref="ResumeContentDto"/> anywhere in the public property graph,
/// OR the handler (transitively, within the Application module) calls a <c>Resume</c>/
/// <c>ResumeVersion</c> member taking a Domain <c>ResumeContent</c> — the sink-keyed tripwire.
/// The sink key is what makes the backstop fail-closed for a future TargetId-based apply
/// command (ids + frame inputs only, no DTO on the command) that composes content server-side:
/// the aggregate sink call, not the command shape, is the invariant point. Backstopped by
/// per-handler unit tests; non-Mediator write paths, sink calls delegated to a Domain
/// collaborator (the walk is Application-bounded, issue #669), and non-devirtualized
/// interface dispatch remain outside the tripwire's subject set (known residuals, documented
/// in the test).</para>
/// </summary>
internal static class ResumeContentPersonnummerGuard
{
    /// <summary>
    /// Succeeds when <paramref name="content"/> carries no personnummer in any free-text field;
    /// otherwise fails with <c>Resume.PersonnummerMustBeRemoved</c>. The code is Resume-scoped
    /// (distinct from the parse-gate's <c>ParsedResume.PersonnummerMustBeRemoved</c>) so telemetry
    /// can tell the write-content guard from the parse gate; the user message is identical.
    /// </summary>
    public static Result Check(ResumeContentDto content)
    {
        var scanCopy = PersonnummerTextNormalizer.Normalize(CollectFreeText(content));
        var outcome = PersonnummerScanOutcome.FromMatches(PersonnummerScanner.Scan(scanCopy));

        return outcome.Found
            ? Result.Failure(DomainError.Validation(
                "Resume.PersonnummerMustBeRemoved",
                "Ta bort personnummer ur CV:t innan det kan användas."))
            : Result.Success();
    }

    /// <summary>
    /// Concatenates every user free-text field of the submitted content so the personnummer scan
    /// sees the whole surface (DQ6 — name/contact, summary, experience company/role/description,
    /// education institution/degree, skill names). Order is irrelevant — the scanner only flags.
    /// </summary>
    private static string CollectFreeText(ResumeContentDto content)
    {
        var sb = new StringBuilder();

        var pi = content.PersonalInfo;
        if (pi is not null)
        {
            sb.AppendLine(pi.FullName);
            sb.AppendLine(pi.Email);
            sb.AppendLine(pi.Phone);
            sb.AppendLine(pi.Location);
        }

        sb.AppendLine(content.Summary);

        foreach (var e in content.Experiences)
        {
            sb.AppendLine(e.Company);
            sb.AppendLine(e.Role);
            sb.AppendLine(e.Description);
        }

        foreach (var ed in content.Educations)
        {
            sb.AppendLine(ed.Institution);
            sb.AppendLine(ed.Degree);
        }

        foreach (var s in content.Skills)
            sb.AppendLine(s.Name);

        return sb.ToString();
    }
}
