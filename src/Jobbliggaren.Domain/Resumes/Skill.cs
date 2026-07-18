namespace Jobbliggaren.Domain.Resumes;

public sealed record Skill(string Name, int? YearsExperience)
{
    /// <summary>
    /// Max length of a scored-atom name — a skill or spoken-language <c>Name</c> (#855). The skill
    /// chip IS the scored unit in the matching engine, so a sentence let in as a "skill" poisons the
    /// atom the matcher scores; the client mirrors this as <c>.max(100)</c>, with the domain as the
    /// authority (the client mirrors the domain, never the reverse). <c>Resume.ValidateContent</c>
    /// caps both <c>Skill.Name</c> and <c>SpokenLanguage.Name</c> against this bound (both are scored
    /// atoms, both cap at 100). The deterministic segmenter cites the SAME bound as its "too long to
    /// be an atom" routing threshold (#856), so a token the domain would reject never becomes a chip.
    /// One definition, one home — DRY across the Domain↔Infrastructure boundary (public so the
    /// Infrastructure segmenter may cite it; that consumer lands in #856, the next PR in this STEG).
    /// </summary>
    public const int NameMaxLength = 100;
}
