namespace Jobbliggaren.Application.Resumes.Queries;

/// <summary>
/// The persisted visual template options of a CV (Fas 4b PR-8b 8b.2, ADR 0096) — the six
/// non-PII member names plus the composed persisted ATS-safety verdict. Drives the
/// persisted state that GET /{id}/render renders (the mallbyggare consumer was retired,
/// CV-pivot 2026-07-16, ADR 0112).
/// </summary>
/// <remarks>
/// <see cref="EffectiveAtsSafe"/> is the DOMAIN's composed verdict
/// (<c>CvTemplateOptions.EffectiveAtsSafe</c> = <c>Template.AtsSafe &amp;&amp; !PhotoEnabled</c>),
/// NOT the template-only <c>CvTemplate.AtsSafe</c> — the field is named identically to the
/// domain member so a reader cannot conflate the template half with the composed verdict.
/// It is the persisted-state label for headless/list surfaces; the per-render effective
/// value is composed from the persisted options at render time.
/// </remarks>
public sealed record CvTemplateOptionsDto(
    string Template,
    string AccentColor,
    string FontPair,
    string Density,
    bool PhotoEnabled,
    string PhotoShape,
    bool EffectiveAtsSafe);
