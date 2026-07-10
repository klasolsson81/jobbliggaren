namespace Jobbliggaren.Application.Resumes.Queries.GetResumeAtsText;

/// <summary>
/// One ATS text view (Fas 4b PR-8.2, CTO-bind Q3). <see cref="Source"/> is the claim
/// discriminator — "Linearized" (app-copy linearization) is the only value this endpoint
/// ever emits; a future parsed-RawText view would carry its own value so the FE can
/// never render the wrong banner over the wrong substrate (D5e: claims never conflated).
/// </summary>
public sealed record ResumeAtsTextDto(string Source, string Text);
