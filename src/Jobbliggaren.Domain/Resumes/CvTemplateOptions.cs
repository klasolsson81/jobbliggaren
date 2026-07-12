namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// Display/template configuration for a CV (Fas 4b PR-3, ADR 0096 — design handoff
/// §5.5/§8: "Parametrar per mall: accentfärg, foto (av/på + form), typsnittspar,
/// täthet"). An immutable value object of six co-changing, fully enumerated settings —
/// every member is a SmartEnum or bool, deliberately NO free-text member, so the
/// plain-column mapping is provably non-PII (CTO-bind D1/D5d; the arch-test
/// <c>ResumeRootPlainColumnGuardTests</c> pins this by reflection). Owned VO on the
/// <see cref="Resume"/> root, persisted as separate plain columns (ADR 0059 parity,
/// ManualPosting/AdSnapshot precedent) — never inside the encrypted content shadow.
/// </summary>
/// <remarks>
/// A positional record with no cross-field invariant (the types already guarantee
/// valid values — <c>Preferences</c> precedent); null-member protection lives in
/// <see cref="Resume.ChangeTemplateOptions"/>, the only mutation path. Defaults per
/// the handoff: Klar (§8 "Default"), Marinblå accent, Modern, Normal density, photo
/// OFF (FOTO-ETIK / kunskapsbank <c>foto_default=false</c>), Circle preset. The
/// chosen <see cref="Template"/> IS the issue's "vald mall" — there is deliberately
/// no duplicate template/mall column in the provenance fields (DRY; CTO re-bind
/// 2026-07-05 dropped the always-null <c>MallId</c>). Rendering consumes this in
/// PR-8b.
/// </remarks>
public sealed record CvTemplateOptions(
    CvTemplate Template,
    CvAccentColor AccentColor,
    CvFontPair FontPair,
    CvDensity Density,
    bool PhotoEnabled,
    CvPhotoShape PhotoShape)
{
    /// <summary>
    /// The handoff-bound defaults every new Resume starts from (and legacy rows
    /// backfill to). Deliberately a FRESH instance per access, not a cached
    /// singleton: the VO is EF-mapped as an owned entity (reference-identity
    /// tracked), and a single shared instance assigned to two aggregates would make
    /// EF re-key the tracked owned entry on the second owner ("property
    /// 'CvTemplateOptions.ResumeId' is part of a key and so cannot be modified" —
    /// found by the PR-3 handler-test suite, 2026-07-05). Value equality is
    /// unaffected (record semantics), so <c>options == CvTemplateOptions.Default</c>
    /// still compares by value.
    /// </summary>
    public static CvTemplateOptions Default => new(
        CvTemplate.Klar,
        CvAccentColor.NavyBlue,
        CvFontPair.Modern,
        CvDensity.Normal,
        PhotoEnabled: false,
        CvPhotoShape.Circle);

    /// <summary>True when every member is set — the completeness guard <see cref="Resume.ChangeTemplateOptions"/> enforces.</summary>
    public bool IsComplete =>
        Template is not null
        && AccentColor is not null
        && FontPair is not null
        && Density is not null
        && PhotoShape is not null;

    /// <summary>
    /// The composed, honest ATS-safety verdict for this configuration:
    /// <see cref="CvTemplate.AtsSafe"/> (the template renders a linear, single-column
    /// parseable layout) AND no photo (a photo further downgrades ATS-safety — its
    /// XML-doc: "the full verdict is <c>AtsSafe &amp;&amp; !effectivePhoto</c>, composed by
    /// the consumer"). This is the SINGLE honest source for the persisted-state ATS label,
    /// consumed by the query DTO (Fas 4b PR-8b 8b.2) and the per-render label (8b.3) alike —
    /// neither re-derives the rule, so the two surfaces can never disagree (P5). Advisory,
    /// never blocking: an ATS-safe plain version is always generated in parallel.
    /// EF-ignored (a computed member, never a column — <see cref="Resume.ChangeTemplateOptions"/> parity with <see cref="IsComplete"/>).
    /// </summary>
    public bool EffectiveAtsSafe => Template.AtsSafe && !PhotoEnabled;
}
