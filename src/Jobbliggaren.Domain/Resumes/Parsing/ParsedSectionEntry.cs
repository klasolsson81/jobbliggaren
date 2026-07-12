namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// One entry inside a free <see cref="ParsedSection"/> — split on blank lines, the same rule the
/// typed Experience/Education sections already use (#815).
///
/// <para><see cref="Title"/> is deliberately nullable and deliberately tolerant: when the entry
/// opens with a bullet or a plain sentence there IS no title, and the parser will not invent one.
/// In that case every line lives in <see cref="Lines"/>. Nothing is synthesised (ADR 0071) —
/// an honest "no title" beats a guessed one.</para>
/// </summary>
public sealed record ParsedSectionEntry(string? Title, IReadOnlyList<string> Lines);
