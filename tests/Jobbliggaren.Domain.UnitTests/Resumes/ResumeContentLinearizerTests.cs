using Jobbliggaren.Domain.Resumes;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Resumes;

/// <summary>
/// Fas 4b PR-4 (#653, ADR 0093 §D8) — the shared linearizer's contract tests, including
/// the bound DoD MEASUREMENT: the D1-B superset must be lossless for citation. Every
/// user-authored text unit in <see cref="ResumeContent"/> must be locatable verbatim in
/// <see cref="LinearizedResume.Text"/> (ordinal substring). If this measurement ever
/// trips, the documented fallback is a Form A RawText column on Resume (ADR 0093 §D8
/// trip-condition — a STOPP, never built preemptively).
/// </summary>
public class ResumeContentLinearizerTests
{
    // A maximal superset content: every field family populated, incl. an ongoing
    // (open-ended) experience, an entry without description, a memberless skill group,
    // multi-line descriptions, åäö and a custom §7 section with and without lines.
    private static ResumeContent MaximalContent() => new(
        new PersonalInfo("Anna Nordin-Svensson", "anna.nordin@example.com", "070-123 45 67", "Göteborg"),
        experiences:
        [
            new Experience(
                "Acme Betalsystem AB", "Senior backend-utvecklare",
                new DateOnly(2021, 3, 1), new DateOnly(2024, 5, 1),
                "Ledde teamet om 8 personer.\nÖkade konverteringen med 23 procent."),
            new Experience(
                "Nordic Tech HB", "Systemutvecklare",
                new DateOnly(2024, 6, 1), null, null),
        ],
        educations:
        [
            new Education("KTH", "Civilingenjör datateknik", new DateOnly(2016, 8, 1), new DateOnly(2021, 6, 1)),
        ],
        skills: [new Skill("C#", 8), new Skill("PostgreSQL", null), new Skill("Kubernetes", 3)],
        summary: "Erfaren backend-utvecklare inom betalsystem med fokus på åtkomst och säkerhet.",
        languages:
        [
            new SpokenLanguage("Svenska", LanguageProficiency.Native),
            new SpokenLanguage("Engelska", LanguageProficiency.Fluent),
        ],
        skillGroups:
        [
            new SkillGroup("Backendutveckling", ["C#", "PostgreSQL"]),
            new SkillGroup("Drift och infrastruktur", []),
        ],
        sections:
        [
            new ResumeSection("Certifieringar",
            [
                new SectionEntry("AWS Solutions Architect", ["Utfärdad 2023", "Giltig till 2026"]),
                new SectionEntry("Scrum Master", []),
            ]),
        ]);

    /// <summary>
    /// Every user-authored text unit the pnr-guard's CollectFreeText discipline covers
    /// (contact fields, summary, experience company/role/description, education
    /// institution/degree, skill names, language names, skill-group names + members,
    /// section headings/titles/lines) — the citable universe the measurement runs over.
    /// </summary>
    private static List<string> CitableUnits(ResumeContent content)
    {
        var units = new List<string?>
        {
            content.PersonalInfo.FullName,
            content.PersonalInfo.Email,
            content.PersonalInfo.Phone,
            content.PersonalInfo.Location,
            content.Summary,
        };

        foreach (var experience in content.Experiences)
        {
            units.Add(experience.Company);
            units.Add(experience.Role);
            units.Add(experience.Description);
        }

        foreach (var education in content.Educations)
        {
            units.Add(education.Institution);
            units.Add(education.Degree);
        }

        units.AddRange(content.Skills.Select(s => s.Name));
        units.AddRange(content.Languages.Select(l => l.Name));

        foreach (var group in content.SkillGroups)
        {
            units.Add(group.Name);
            units.AddRange(group.Members);
        }

        foreach (var section in content.Sections)
        {
            units.Add(section.Heading);
            foreach (var entry in section.Entries)
            {
                units.Add(entry.Title);
                units.AddRange(entry.Lines);
            }
        }

        return units.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u!).ToList();
    }

    [Fact]
    public void Linearize_MeasuredLossless_EveryCitableUnitIsLocatableVerbatim()
    {
        // THE bound D8 DoD measurement (CTO-bind PR-4 Q7 DoD 3).
        var content = MaximalContent();
        var linearized = ResumeContentLinearizer.Linearize(content);

        var units = CitableUnits(content);
        var missing = units
            .Where(u => linearized.Text.IndexOf(u, StringComparison.Ordinal) < 0)
            .ToList();

        units.Count.ShouldBeGreaterThan(25, "the maximal fixture must exercise every field family.");
        missing.ShouldBeEmpty(
            $"D8 citation-losslessness MEASUREMENT: {units.Count - missing.Count}/{units.Count} " +
            "citable units locatable — every user-authored text unit must appear verbatim in " +
            "the linearized text, or the review engine cannot cite it (ADR 0093 §D8). A " +
            "failure here TRIPS the documented Form A RawText fallback (STOPP — do not fix " +
            "by weakening this test). Missing: " + string.Join(" | ", missing));
    }

    [Fact]
    public void Linearize_IsDeterministic_SameContentSameText()
    {
        var first = ResumeContentLinearizer.Linearize(MaximalContent());
        var second = ResumeContentLinearizer.Linearize(MaximalContent());

        second.Text.ShouldBe(first.Text);
        second.Sections.ShouldBe(first.Sections);
    }

    [Fact]
    public void Linearize_SectionGeometry_SlicesAreValidAndOrdered()
    {
        var linearized = ResumeContentLinearizer.Linearize(MaximalContent());

        linearized.Sections.ShouldNotBeEmpty();
        var previousEnd = -1;
        foreach (var section in linearized.Sections)
        {
            section.Start.ShouldBeGreaterThan(previousEnd,
                "sections must be ordered and non-overlapping in the linear text.");
            section.Length.ShouldBeGreaterThan(0);
            (section.Start + section.Length).ShouldBeLessThanOrEqualTo(linearized.Text.Length);

            var slice = linearized.Text.Substring(section.Start, section.Length);
            slice.ShouldNotBeNullOrWhiteSpace();
            previousEnd = section.Start + section.Length;
        }

        linearized.Sections.Select(s => s.Kind).ShouldBe(
        [
            LinearSectionKind.Contact,
            LinearSectionKind.Summary,
            LinearSectionKind.Experience,
            LinearSectionKind.Education,
            LinearSectionKind.Skills,
            LinearSectionKind.Languages,
            LinearSectionKind.Custom,
        ]);
    }

    [Fact]
    public void Linearize_CustomSection_StartsWithItsUserHeading()
    {
        var linearized = ResumeContentLinearizer.Linearize(MaximalContent());

        var custom = linearized.Sections.Single(s => s.Kind == LinearSectionKind.Custom);
        linearized.Text.Substring(custom.Start, custom.Length)
            .ShouldStartWith("Certifieringar");
    }

    [Fact]
    public void Linearize_EmptySections_AreOmittedFromTextAndGeometry()
    {
        // A minimal content (name only) — no empty headings, no orphan separators.
        var minimal = ResumeContent.Empty("Anna Andersson");
        var linearized = ResumeContentLinearizer.Linearize(minimal);

        linearized.Sections.Select(s => s.Kind).ShouldBe([LinearSectionKind.Contact]);
        linearized.Text.ShouldBe("Anna Andersson");
    }

    [Fact]
    public void Linearize_OngoingExperience_RendersOpenEndedPeriod()
    {
        var linearized = ResumeContentLinearizer.Linearize(MaximalContent());

        // The ongoing role (EndDate null) renders an open right side — a derived date
        // line, deterministic and locale-neutral; never a citation target.
        linearized.Text.ShouldContain("2024-06 –");
        linearized.Text.ShouldContain("2021-03 – 2024-05");
    }

    [Fact]
    public void Linearize_NullContent_Throws()
    {
        Should.Throw<ArgumentNullException>(() => ResumeContentLinearizer.Linearize(null!));
    }

    // #815 — the least obvious consequence of a nullable entry title, and the most dangerous.
    // string.Join renders a null title as an EMPTY STRING, i.e. a BLANK LINE. A blank line is
    // exactly the block separator the citation substrate and the review rules split on (ADR 0093
    // §D8). An unguarded null title would therefore inject a PHANTOM BLOCK BOUNDARY into the very
    // evidence a CV verdict is cited from — the engine fabricating structure inside its own proof.
    [Fact]
    public void Linearize_SectionEntryWithoutTitle_DoesNotInjectAPhantomBlockBoundary()
    {
        var content = new ResumeContent(
            new PersonalInfo("Anna Andersson", null, null, null),
            sections:
            [
                new ResumeSection(
                    "Referenser",
                    [
                        new SectionEntry(null, ["Lamnas pa begaran."]),
                        new SectionEntry("Tidigare chef", ["Kontaktas via mig."]),
                    ]),
            ]);

        var text = ResumeContentLinearizer.Linearize(content).Text;

        // The section's own block must stay ONE block: no blank line inside it.
        var fromHeading = text[text.IndexOf("Referenser", StringComparison.Ordinal)..];
        var blockEnd = fromHeading.IndexOf("\n\n", StringComparison.Ordinal);
        var sectionBlock = blockEnd < 0 ? fromHeading : fromHeading[..blockEnd];

        sectionBlock.ShouldContain("Lamnas pa begaran.");
        sectionBlock.ShouldContain("Tidigare chef");
        sectionBlock.ShouldContain("Kontaktas via mig.");
    }
}
