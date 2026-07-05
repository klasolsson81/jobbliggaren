using Ardalis.SmartEnum;

namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// Layout density for a templated CV (Fas 4b PR-3, ADR 0096 — design handoff §5.5
/// TÄTHET: Luftig/Normal/Kompakt). Swedish display labels resolve via
/// <c>messages/sv.json</c>; the concrete spacing values are a rendering concern
/// (PR-8b), not stored here.
/// </summary>
public sealed class CvDensity : SmartEnum<CvDensity>
{
    /// <summary>Luftig.</summary>
    public static readonly CvDensity Airy = new(nameof(Airy), 1);

    /// <summary>Normal. Default.</summary>
    public static readonly CvDensity Normal = new(nameof(Normal), 2);

    /// <summary>Kompakt.</summary>
    public static readonly CvDensity Compact = new(nameof(Compact), 3);

    private CvDensity(string name, int value) : base(name, value) { }
}
