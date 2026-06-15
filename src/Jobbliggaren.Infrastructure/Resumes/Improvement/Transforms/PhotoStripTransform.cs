using Jobbliggaren.Application.Resumes.Improvement.Abstractions;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement.Transforms;

/// <summary>
/// Photo strip (Fas 4 STEG 10, F4-10) — NOT ASSESSED in v1. The deterministic text-only parse
/// (F4-8) cannot SEE a profile photo: there is no layout/image metadata in
/// <c>ParsedResume</c> v1. So this transform proposes NOTHING — an honest "ej bedömt v1", never
/// a fabricated edit against a signal that does not exist (CLAUDE.md §5; parity the F4-9
/// NoInputReason discipline). The locked enum member + transform exist so the contract is stable
/// when a future parse surfaces a photo signal (photo default OFF for the SE market, research §3.2).
/// </summary>
internal sealed class PhotoStripTransform : ICvTransform
{
    public ProposedChangeKind Kind => ProposedChangeKind.PhotoStrip;

    public IEnumerable<ProposedChange> Propose(CvImprovementContext context) =>
        [];
}
