namespace Jobbliggaren.QA.Corpus.Layout;

/// <summary>One employment block. <paramref name="Marker"/> is the EMPLOYER string, and it is
/// the marker because it is the field that survives the whole chain into
/// <c>ExperienceDto.Company</c> (<c>AutoPromoteContentMapper.ToContentDto</c>) and therefore into
/// the promoted CV. Tracing it end-to-end is what turns "content loss" from a count into a
/// per-employment fact.</summary>
public sealed record EmploymentBlock(string Marker, string Role, string Period, string Bullet);

/// <summary>One education block. Marker = the institution string (reaches
/// <c>EducationDto.Institution</c>).</summary>
public sealed record EducationBlock(string Marker, string Degree, string Period);

/// <summary>The heading words a case renders. Swapping this record is what makes the English
/// case the SAME renderer over a different vocabulary rather than a second renderer.</summary>
public sealed record HeadingVocabulary(
    string Profile, string Experience, string Education, string Skills, string Languages,
    string KnownProjects, string UnknownProjects);

/// <summary>
/// The structured CV every renderer renders FROM. One model, many containers: the bytes and the
/// ground truth cannot desynchronise, because the ground truth is <i>derived</i> from this record
/// (<c>Employments.Count</c>) and never typed a second time. A corpus whose expected counts are
/// hand-written constants measures its own constants.
///
/// <para><b>Cardinalities are deliberately all-distinct — 5 / 3 / 7 / 2 / 4.</b> A count-only
/// oracle over equal cardinalities cannot tell which side it measured: if experience and
/// education were both 2, a bug that read the education list while reporting experience would
/// score green. This repo has that lesson written down (an asymmetric seed, never 1 and 1).</para>
///
/// <para>Content is synthetic throughout. The person, employers, schools and projects are
/// invented; the only personnummer that appears anywhere is the synthetic Luhn-valid one on the
/// pnr-bearing case, and it is never printed to the report, a log, or the committed baseline.</para>
/// </summary>
public sealed record CvModel(
    string PersonName,
    string Email,
    string Phone,
    string City,
    IReadOnlyList<string> ProfileLines,
    IReadOnlyList<EmploymentBlock> Employments,
    IReadOnlyList<EducationBlock> Educations,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> ProjectLines,
    HeadingVocabulary Headings,
    string? SyntheticPersonnummer = null)
{
    /// <summary>Ground truth, DERIVED. Never a literal.</summary>
    public int GroundTruthEmployments => Employments.Count;

    /// <summary>Ground truth, DERIVED. Never a literal.</summary>
    public int GroundTruthEducations => Educations.Count;

    /// <summary>Every employer marker, in authored order.</summary>
    public IReadOnlyList<string> EmploymentMarkers => [.. Employments.Select(e => e.Marker)];

    /// <summary>Every institution marker, in authored order.</summary>
    public IReadOnlyList<string> EducationMarkers => [.. Educations.Select(e => e.Marker)];

    /// <summary>The Swedish baseline model. 5 employments, 3 educations, 7 skills, 2 languages,
    /// 4 project lines.</summary>
    public static CvModel Swedish { get; } = new(
        PersonName: "Anna Andersson",
        Email: "anna.andersson@example.com",
        Phone: "070-123 45 67",
        City: "Göteborg",
        ProfileLines:
        [
            "Erfaren backend-utvecklare med tyngdpunkt på betalflöden och integrationer.",
            "Har lett tre team genom plattformsmigrationer med mätbara driftresultat.",
            "Trivs närmast produktionen, med ansvar för både arkitektur och jour.",
            "Söker nu en roll där teknisk höjd och arkitekturansvar kan kombineras.",
        ],
        Employments:
        [
            new("Klarna AB", "Senior backend-utvecklare", "2021 - 2026",
                "Ansvarig för betalflödets tjänstelager och dess kapacitetsplanering."),
            new("Volvo Cars", "Backend-utvecklare", "2018 - 2021",
                "Byggde telemetri-ingången som tog emot fordonsdata i realtid."),
            new("Västra Götalandsregionen", "Systemutvecklare", "2015 - 2018",
                "Journalintegrationer mot regionens vårdsystem, med hög spårbarhet."),
            new("Consid AB", "Utvecklare", "2012 - 2015",
                "Konsultuppdrag inom e-handel och logistik för fyra kunder."),
            new("Sigma IT", "Junior utvecklare", "2010 - 2012",
                "Förvaltning och vidareutveckling av interna administrativa system."),
        ],
        Educations:
        [
            new("Chalmers tekniska högskola", "Civilingenjör, datateknik", "2005 - 2010"),
            new("Göteborgs universitet", "Fristående kurser i systemvetenskap", "2003 - 2005"),
            new("Hvitfeldtska gymnasiet", "Naturvetenskapligt program", "2000 - 2003"),
        ],
        Skills: ["C#", ".NET", "PostgreSQL", "Kubernetes", "Terraform", "React", "TypeScript"],
        Languages: ["Svenska - modersmål", "Engelska - flytande"],
        ProjectLines:
        [
            "Jobbliggaren - deterministisk CV-granskare i .NET och Next.js.",
            "Kartkollen - öppen data om kommunala beslut, byggd på PostGIS.",
            "Turlistan - reseplanerare för kollektivtrafik i Västsverige.",
            "Bokhyllan - katalogtjänst för folkbibliotekens fjärrlån.",
        ],
        Headings: new HeadingVocabulary(
            Profile: "PROFIL",
            Experience: "ARBETSLIVSERFARENHET",
            Education: "UTBILDNING",
            Skills: "TEKNISKA KOMPETENSER",
            Languages: "SPRÅK",
            KnownProjects: "PROJEKT",
            UnknownProjects: "PROJEKT (URVAL)"));

    /// <summary>
    /// The English model. Same person, same cardinalities, same section set and — load-bearing for
    /// pin P5 — the same section ORDER. Section set and order hold STRUCTURALLY, because both the
    /// Swedish and English cases run the same renderer method and a renderer emits its sections in
    /// one order; only the cardinalities need a runtime check, and
    /// <c>LayoutCaseCatalog.ValidateModelSymmetry</c> is exactly that and nothing more. Stated
    /// precisely rather than generously: if this record drifts, P5 stops being a non-difference
    /// claim and becomes permanent noise.
    /// </summary>
    public static CvModel English { get; } = Swedish with
    {
        ProfileLines =
        [
            "Experienced backend developer focused on payment flows and integrations.",
            "Has led three teams through platform migrations with measurable results.",
            "Most at home close to production, owning both architecture and on-call.",
            "Now looking for a role combining technical depth with architecture ownership.",
        ],
        Employments =
        [
            new("Klarna AB", "Senior Backend Developer", "2021 - 2026",
                "Owned the payment flow service tier and its capacity planning."),
            new("Volvo Cars", "Backend Developer", "2018 - 2021",
                "Built the telemetry ingest that received vehicle data in real time."),
            new("Region Vastra Gotaland", "Systems Developer", "2015 - 2018",
                "Health record integrations with strong traceability requirements."),
            new("Consid AB", "Developer", "2012 - 2015",
                "Consulting assignments across e-commerce and logistics for four clients."),
            new("Sigma IT", "Junior Developer", "2010 - 2012",
                "Maintenance and extension of internal administrative systems."),
        ],
        Educations =
        [
            new("Chalmers University of Technology", "MSc, Computer Science", "2005 - 2010"),
            new("University of Gothenburg", "Courses in Information Systems", "2003 - 2005"),
            new("Hvitfeldtska Upper Secondary", "Natural Sciences Programme", "2000 - 2003"),
        ],
        Languages = ["Swedish - native", "English - fluent"],
        ProjectLines =
        [
            "Jobbliggaren - a deterministic CV reviewer in .NET and Next.js.",
            "Kartkollen - open data on municipal decisions, built on PostGIS.",
            "Turlistan - a public transport journey planner for western Sweden.",
            "Bokhyllan - a catalogue service for public library interlibrary loans.",
        ],
        Headings = new HeadingVocabulary(
            Profile: "SUMMARY",
            Experience: "EXPERIENCE",
            Education: "EDUCATION",
            Skills: "SKILLS",
            Languages: "LANGUAGES",
            KnownProjects: "PROJECTS",
            UnknownProjects: "SELECTED PROJECTS (SHORTLIST)"),
    };
}
