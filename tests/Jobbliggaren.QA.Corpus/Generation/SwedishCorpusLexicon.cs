namespace Jobbliggaren.QA.Corpus.Generation;

/// <summary>
/// Own-authored, committed Swedish (and a little English) word banks for synthesising corpus
/// text (CTO Fork 3 = 3B: synthetic prose from in-repo lexica, NO new ad-text asset, fully
/// reproducible, zero PII surface). These are stress-fixtures, not the product knowledge bank:
/// quality thresholds are observe-only (CTO Fork 6), so this vocabulary need not mirror the
/// versioned rubric/cliché/verb assets — it only needs to exercise the engines' edges.
///
/// <para>All collections are fixed and ordered → selection by a deterministic index is
/// reproducible across runs (paired with <see cref="DeterministicRng"/>).</para>
/// </summary>
public static class SwedishCorpusLexicon
{
    /// <summary>Life-situation phrases that are NOT occupations (must never resolve to SSYK).</summary>
    public static readonly IReadOnlyList<string> LifeSituations =
    [
        "Föräldraledig", "Sjukskriven", "Arbetssökande", "Pensionär", "Studieledig",
        "Tjänstledig", "Mellan jobb", "Egenföretagare på paus", "Nyutexaminerad",
        "Vård av anhörig", "Värnpliktig", "Volontär", "Praktikant utan placering",
    ];

    /// <summary>Non-standard / English / fluff titles that Swedish stemming degrades.</summary>
    public static readonly IReadOnlyList<string> NonStandardTitles =
    [
        "Software Engineer", "Senior Software Engineer", "Full Stack Developer",
        "Ninja Developer", "Rockstar Coder", "Growth Hacker", "Code Wizard",
        "Chief Happiness Officer", "Data Scientist", "Product Owner", "Scrum Master",
        "DevOps Guru", "Cloud Architect", "Marketing Evangelist", "Sales Hustler",
    ];

    /// <summary>Secondary occupational fragments for multi-track titles/CVs.</summary>
    public static readonly IReadOnlyList<string> SecondaryTracks =
    [
        "snickare", "lärare", "industriarbetare", "lastbilschaufför", "kock",
        "undersköterska", "elektriker", "ekonomiassistent", "butikssäljare", "vaktmästare",
    ];

    /// <summary>Connectors used to stitch multi-track titles.</summary>
    public static readonly IReadOnlyList<string> TrackConnectors =
        [" och ", " / ", ", tidigare ", " samt ", " · "];

    /// <summary>Inflection suffixes appended to a base occupation-name (deterministic morphology).</summary>
    public static readonly IReadOnlyList<string> InflectionSuffixes =
        ["n", "en", "are", " (vikariat)", " m.fl.", "s"];

    /// <summary>Weak single-token signals.</summary>
    public static readonly IReadOnlyList<string> WeakTokens =
        ["arbete", "jobb", "tjänst", "uppdrag", "konsult", "specialist", "medarbetare"];

    /// <summary>Mojibake / OCR-noise fragments (UTF-8 mis-decode artefacts, stray glyphs).</summary>
    public static readonly IReadOnlyList<string> NoiseFragments =
        ["Ã¥Ã¤Ã¶", "â€”", "ï¿½", "Â ", "�", "  ", "\t\t", "—–·•", "Ã©Ã¨"];

    /// <summary>Non-Swedish given+family name fragments for the newly-arrived stratum.</summary>
    public static readonly IReadOnlyList<string> NonSwedishNames =
    [
        "Amir Haddad", "Lucía Fernández", "Nguyen Van An", "Olamide Adeyemi",
        "Fatima Al-Sayed", "Dmytro Kovalenko", "Mei Lin", "Rajesh Patel",
    ];

    /// <summary>Action verbs (own-authored subset) so the review engine's A2 rule fires.</summary>
    public static readonly IReadOnlyList<string> ActionVerbs =
        ["Ledde", "Byggde", "Levererade", "Ökade", "Minskade", "Införde", "Ansvarade för", "Drev"];

    /// <summary>Clichés (own-authored subset) so the review engine's A7 rule fires.
    /// Consumed by the reviewer frontend (PR 3); declared here with the rest of the lexicon.</summary>
    public static readonly IReadOnlyList<string> Cliches =
    [
        "teamplayer", "lösningsorienterad", "tänker utanför boxen", "driven och engagerad",
        "prestigelös", "social och utåtriktad",
    ];

    /// <summary>Organisations for experience entries.</summary>
    public static readonly IReadOnlyList<string> Organizations =
        ["Acme AB", "Volvo", "Region Skåne", "Klarna", "ICA", "Skanska", "Spotify", "Kommunen"];

    /// <summary>Locations for the contact section.</summary>
    public static readonly IReadOnlyList<string> Locations =
        ["Stockholm", "Göteborg", "Malmö", "Uppsala", "Umeå", "Linköping"];

    /// <summary>Skills for the skills section.</summary>
    public static readonly IReadOnlyList<string> Skills =
        ["C#", "PostgreSQL", "Excel", "Truckkort", "Svetsning", "Projektledning", "SQL", "Rettlearning"];

    /// <summary>Luhn-valid fake personnummer / samordningsnummer test values (from the domain
    /// test-suite; deterministic, never real persons). Used ONLY embedded in CV free text so the
    /// real personnummer guard trips; the raw value is never echoed to a label/report/log.</summary>
    public static readonly IReadOnlyList<string> FakePersonnummer =
        ["811218-9876", "811278-9873", "19811218-9876"];

    /// <summary>A high-quality Swedish CV bullet (quantified + action verb) for clean strata.</summary>
    public static string StrongBullet(string verb, string org) =>
        $"{verb} ett team om 8 personer hos {org} och ökade leveranstakten med 30 procent.";
}
