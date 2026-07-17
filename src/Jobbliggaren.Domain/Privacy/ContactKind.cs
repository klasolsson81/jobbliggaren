namespace Jobbliggaren.Domain.Privacy;

/// <summary>
/// The kind of recruiter contact detail a deterministic detector span carries (#842 Tier A).
/// Exactly two — the bound detection surface is email + Swedish phone, and name-NER is a
/// deliberate rejection, not a deferral (ADR 0106 D5: the name is Tier B's population).
/// </summary>
public enum ContactKind
{
    Email,
    Phone,
}
