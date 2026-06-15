namespace Jobbliggaren.Application.Resumes.Improvement.Abstractions;

/// <summary>
/// A concrete before→after text edit (Fas 4 STEG 10, F4-10). <paramref name="Before"/> is the
/// verbatim quoted span (equals the <c>TextSpanEvidence.Quote</c> when the change is
/// text-grounded); <paramref name="After"/> is constrained by the change's
/// <see cref="ChangeProvenance"/> — a verbatim knowledge-bank value, or a pure transform of
/// <paramref name="Before"/>. A pure removal (personnummer / GPA) carries NO
/// <see cref="ProposedReplacement"/> at all (the change is the <c>Operation</c> alone).
/// </summary>
public sealed record ProposedReplacement(string Before, string After);
