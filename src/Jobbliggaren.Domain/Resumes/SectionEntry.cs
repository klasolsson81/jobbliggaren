namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// One entry inside a dynamic profession-driven CV section (Fas 4b AppCopy superset,
/// ADR 0093 D1 / LRM ADR 0095 D-B). <paramref name="Title"/> is the entry heading (e.g. a
/// project name, a certification); <paramref name="Lines"/> are the body lines (design
/// handoff §5.2 "titel/underrad/metabricka" collapses into title + lines — the semantic
/// shape, never the visual one, which is a rendering concern). Both are CV-PII free text
/// scanned by <c>ResumeContentPersonnummerGuard</c>.
///
/// <para><b><paramref name="Title"/> is optional (#815).</b> It used to be required, and that was a
/// defect in the MODEL, not in the documents: "Referenser / Lämnas på begäran." and a bulleted
/// "Intressen" are perfectly ordinary CV sections, and an entry there simply has no heading. The
/// asymmetry was already visible — an entry with a title and ZERO lines has always been legal — and
/// only the mirror image was forbidden. Requiring a title also put pressure on the deterministic
/// parser to INVENT one (ADR 0071 forbids exactly that), which is how the defect surfaced.
/// The real invariant, enforced by the aggregate (<c>Resume.ValidateContent</c>, CLAUDE.md §2.2),
/// is: an entry must carry EITHER a title OR lines — never neither.</para>
///
/// <para>Consumers must test with <c>IsNullOrWhiteSpace</c>, never <c>is not null</c>: the guide
/// sends <c>""</c> for an absent title (controlled input) while the parser sends <c>null</c>. Both
/// mean "no title", and a null-check alone is a latent bug.</para>
/// </summary>
public sealed record SectionEntry(string? Title, IReadOnlyList<string> Lines);
