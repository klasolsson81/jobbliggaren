using Ardalis.SmartEnum;

namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// Crop shape for the optional CV photo (Fas 4b PR-3, ADR 0096 — design handoff §5.5
/// FOTO: form cirkel/rundad/fyrkant). Only the on/off + shape CONFIG ships in PR-3;
/// the photo image itself (upload, EXIF strip, storage) is deferred to PR-10 behind
/// the B2 DPIA gate (ADR 0093 D5f, ruling v). Photo default is OFF per Swedish norm
/// (handoff §5.5 FOTO-ETIK; kunskapsbank <c>foto_default=false</c>) — the shape is a
/// preset that only takes visual effect when the photo is enabled and present.
/// </summary>
public sealed class CvPhotoShape : SmartEnum<CvPhotoShape>
{
    /// <summary>Cirkel. Default preset.</summary>
    public static readonly CvPhotoShape Circle = new(nameof(Circle), 1);

    /// <summary>Rundad (rounded rectangle).</summary>
    public static readonly CvPhotoShape Rounded = new(nameof(Rounded), 2);

    /// <summary>Fyrkant.</summary>
    public static readonly CvPhotoShape Square = new(nameof(Square), 3);

    private CvPhotoShape(string name, int value) : base(name, value) { }
}
