using Ardalis.SmartEnum;

namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// Provenance of a canonical <see cref="Resume"/> (Fas 4b PR-3, ADR 0096 — the design
/// handoff §4 <c>kalla.typ</c>: <c>import</c> | <c>mall</c>). Set by construction only
/// (<see cref="Resume.CreateFromParsed"/> → <see cref="Import"/>;
/// <see cref="Resume.Create"/> → <see cref="Template"/>) — immutable thereafter, no
/// mutator. A closed, system-controlled vocabulary → SmartEnum, not a string
/// (CLAUDE.md §5). Non-PII plain column on the resumes root (ADR 0059 parity,
/// CTO-bind D1), persisted by Name-string (reorder-safe house default).
/// </summary>
/// <remarks>
/// <see cref="Legacy"/> is load-bearing honesty (ADR 0074 OQ3 parity with
/// <c>LanguageProficiency.NotStated</c>): rows created before Fas 4b PR-3 have an
/// unknowable origin, and fabricating <c>Import</c>/<c>Template</c> for them would
/// misreport provenance (never synthesise, CLAUDE.md §5). <see cref="Legacy"/> can
/// only occur on pre-PR-3 rows — every new Resume records a real origin at
/// construction. The <see cref="Resume.Adopt"/> precondition (Import-only) naturally
/// refuses Legacy rows.
/// </remarks>
public sealed class ResumeSourceOrigin : SmartEnum<ResumeSourceOrigin>
{
    /// <summary>Pre-Fas-4b row; origin was not recorded at creation and is never fabricated.</summary>
    public static readonly ResumeSourceOrigin Legacy = new(nameof(Legacy), 0);

    /// <summary>Created by promoting an imported, parsed CV file (<see cref="Resume.CreateFromParsed"/>).</summary>
    public static readonly ResumeSourceOrigin Import = new(nameof(Import), 1);

    /// <summary>
    /// Authored in-app from a template/profile (<see cref="Resume.Create"/>) — the handoff's
    /// <c>mall</c>-CV. Not to be confused with the chosen visual template, which lives in
    /// <see cref="CvTemplateOptions.Template"/> (<see cref="CvTemplate"/>).
    /// </summary>
    public static readonly ResumeSourceOrigin Template = new(nameof(Template), 2);

    private ResumeSourceOrigin(string name, int value) : base(name, value) { }
}
