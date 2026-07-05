namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// The section vocabulary of a linearized canonical CV (Fas 4b PR-4, ADR 0093 §D8).
/// The four standard content sections plus contact/summary mirror
/// <see cref="ResumeContent"/>'s structure; <see cref="Custom"/> covers the dynamic
/// profession-driven <see cref="ResumeContent.Sections"/> (their user-authored heading
/// lives in the linear text itself, at the section's start offset).
/// </summary>
public enum LinearSectionKind
{
    Contact,
    Summary,
    Experience,
    Education,
    Skills,
    Languages,
    Custom,
}

/// <summary>
/// One section's span in the linearized text: <see cref="Start"/>/<see cref="Length"/>
/// index into <see cref="LinearizedResume.Text"/> (the section's own text, excluding the
/// inter-section separator). The geometry a cited span or a text view can anchor to.
/// </summary>
public sealed record LinearSection(LinearSectionKind Kind, int Start, int Length);

/// <summary>
/// The deterministic linear-text projection of a canonical <see cref="ResumeContent"/>
/// (Fas 4b PR-4, ADR 0093 §D8 — produced ONLY by <see cref="ResumeContentLinearizer"/>).
/// <see cref="Text"/> is the post-promote citation substrate (the canonical analogue of
/// <c>ParsedResume.RawText</c>) and the single source of truth the ATS text view and
/// ATS-PDF composer consume (D5e SPOT) — one linearization, multiple consumers, so
/// "what the review cites" and "what the ATS view shows" can never independently drift.
/// </summary>
public sealed record LinearizedResume(string Text, IReadOnlyList<LinearSection> Sections);
