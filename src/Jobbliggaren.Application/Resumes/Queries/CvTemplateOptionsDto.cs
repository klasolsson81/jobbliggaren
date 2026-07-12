namespace Jobbliggaren.Application.Resumes.Queries;

/// <summary>
/// The persisted visual template options of a CV (Fas 4b PR-8b 8b.2, ADR 0096) — the six
/// non-PII member names plus the composed persisted ATS-safety verdict. Hydrates the
/// template builder's current selection (8b.3).
/// </summary>
/// <remarks>
/// <see cref="EffectiveAtsSafe"/> is the DOMAIN's composed verdict
/// (<c>CvTemplateOptions.EffectiveAtsSafe</c> = <c>Template.AtsSafe &amp;&amp; !PhotoEnabled</c>),
/// NOT the template-only <c>CvTemplate.AtsSafe</c> — the field is named identically to the
/// domain member so a reader cannot conflate the template half with the composed verdict.
/// It is the persisted-state label for headless/list surfaces; the per-render effective
/// value (with live builder options) is composed in 8b.3.
/// </remarks>
public sealed record CvTemplateOptionsDto(
    string Template,
    string AccentColor,
    string FontPair,
    string Density,
    bool PhotoEnabled,
    string PhotoShape,
    bool EffectiveAtsSafe);
