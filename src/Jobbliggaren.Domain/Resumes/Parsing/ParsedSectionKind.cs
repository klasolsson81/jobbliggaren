namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// The CV sections a deterministic parse targets (F4-8, BUILD §8). Swedish product
/// vocabulary: kontakt / profil / arbetslivserfarenhet / utbildning / kompetenser /
/// språk. Each section carries its own <see cref="SectionConfidence"/> so a degraded
/// parse is distinguishable per-section (OQ5).
/// </summary>
public enum ParsedSectionKind
{
    /// <summary>Kontakt — name, e-mail, phone, location.</summary>
    Contact,

    /// <summary>Profil / sammanfattning — the free-text summary.</summary>
    Profile,

    /// <summary>Arbetslivserfarenhet — work experience entries.</summary>
    Experience,

    /// <summary>Utbildning — education entries.</summary>
    Education,

    /// <summary>Kompetenser / färdigheter — skills.</summary>
    Skills,

    /// <summary>Språk — languages.</summary>
    Languages,
}
