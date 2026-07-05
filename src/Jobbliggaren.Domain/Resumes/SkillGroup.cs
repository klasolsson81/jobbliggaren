namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// A named grouping of skills on a canonical CV (Fas 4b AppCopy superset
/// <c>kompetensgrupper</c>, ADR 0093 D1 / LRM ADR 0095 D-A). This is an
/// <b>overlay</b>, never a second skill store: <paramref name="Members"/> holds skill
/// <em>names</em> that must already appear in <c>ResumeContent.Skills</c> — the flat
/// <c>Skill</c> list stays the single authoritative store (it carries
/// <c>YearsExperience</c>; a group is presentation grouping/ordering over it, DRY —
/// one authoritative representation of the skill set). The membership invariant
/// (every member ∈ <c>Skills[].Name</c>, no dangling reference → no phantom skill the
/// user did not write, CLAUDE.md §5) is enforced by <c>Resume.ValidateContent</c>.
/// Not every skill need be grouped — ungrouped skills are legitimate (design handoff
/// P4, "the file wins").
/// </summary>
public sealed record SkillGroup(string Name, IReadOnlyList<string> Members);
