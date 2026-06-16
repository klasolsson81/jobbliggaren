using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.QA.Corpus.Generation;

/// <summary>
/// The deterministic, seeded, stratified corpus generator (Fas 4 STEG C, ADR 0071 — NO
/// AI/LLM). Produces two corpora that edge-stress the REAL engines:
/// <list type="bullet">
/// <item>a TITLE corpus for <c>IOccupationCodeDeriver.DeriveAsync</c> (deriver frontend, PR 2);</item>
/// <item>a CV corpus of real <see cref="ParsedResume"/> aggregates for
/// <c>ICvReviewEngine.ReviewAsync</c> (reviewer frontend, PR 3).</item>
/// </list>
///
/// <para><b>Determinism (CTO Fork 2 = 2B):</b> every case is a pure function of
/// (seed, stratum, local index) via <see cref="DeterministicRng"/> — re-running with the
/// same <see cref="CorpusConfig"/> yields a byte-identical corpus, and a quota bump never
/// shifts an existing case (the determinism tests prove both).</para>
///
/// <para><b>Anti-stale (CTO Fork 2):</b> the facit-bearing strata consume INJECTED
/// <see cref="OccupationGroundTruth"/> pairs — the deriver frontend supplies the real pairs
/// derived live from the seeded taxonomy + frozen map; nothing is hard-coded here.</para>
///
/// <para><b>CV construction</b> goes through the real <c>ParsedResume.Create</c> factory
/// (the same path the import handler uses, DRY per CTO Fork 1), and the personnummer guard
/// runs on the assembled free text exactly as <c>ImportResumeCommandHandler</c> does — so a
/// <see cref="CorpusStratum.FakePersonnummer"/> case carries a real, PII-safe scan outcome.</para>
/// </summary>
public sealed class CorpusGenerator(CorpusConfig? config = null)
{
    private readonly CorpusConfig _config = config ?? CorpusConfig.Default;

    /// <summary>The configuration this generator runs with (seed + quotas + scale).</summary>
    public CorpusConfig Config => _config;

    // ── Title corpus (deriver) ───────────────────────────────────────────

    /// <summary>
    /// Generates the title corpus. The facit-bearing strata (Clean/Inflected) yield cases
    /// only when <paramref name="groundTruth"/> is non-empty; all other strata are
    /// self-contained.
    /// </summary>
    public IReadOnlyList<GeneratedTitleCase> GenerateTitleCorpus(
        IReadOnlyList<OccupationGroundTruth> groundTruth)
    {
        ArgumentNullException.ThrowIfNull(groundTruth);
        var cases = new List<GeneratedTitleCase>();
        foreach (var stratum in CorpusStrata.Title)
        {
            var count = EffectiveCount(stratum, groundTruth);
            for (var i = 0; i < count; i++)
                cases.Add(BuildTitleCase(stratum, i, DeterministicRng.For(_config.Seed, stratum, i), groundTruth));
        }

        return cases;
    }

    // ── CV corpus (reviewer) ─────────────────────────────────────────────

    /// <summary>Generates the CV corpus of real <see cref="ParsedResume"/> aggregates.</summary>
    public IReadOnlyList<GeneratedCvCase> GenerateCvCorpus(
        IReadOnlyList<OccupationGroundTruth> groundTruth)
    {
        ArgumentNullException.ThrowIfNull(groundTruth);
        var cases = new List<GeneratedCvCase>();
        foreach (var stratum in CorpusStrata.Cv)
        {
            var count = EffectiveCount(stratum, groundTruth);
            for (var i = 0; i < count; i++)
                cases.Add(BuildCvCase(stratum, i, DeterministicRng.For(_config.Seed, stratum, i), groundTruth));
        }

        return cases;
    }

    private int EffectiveCount(CorpusStratum stratum, IReadOnlyList<OccupationGroundTruth> groundTruth) =>
        NeedsGroundTruth(stratum) && groundTruth.Count == 0 ? 0 : _config.CountFor(stratum);

    private static bool NeedsGroundTruth(CorpusStratum stratum) =>
        stratum is CorpusStratum.CleanExactTitle
            or CorpusStratum.InflectedTitle
            or CorpusStratum.MultiTrack;

    // ── Title case builders ──────────────────────────────────────────────

    private static GeneratedTitleCase BuildTitleCase(
        CorpusStratum stratum, int index, Random rng, IReadOnlyList<OccupationGroundTruth> gt)
    {
        switch (stratum)
        {
            case CorpusStratum.CleanExactTitle:
                {
                    var pair = Pick(gt, rng);
                    return new GeneratedTitleCase(index, stratum, pair.OccupationName,
                        DerivationExpectation.ResolvesToFacitGroup, pair.ExpectedSsyk4ConceptId);
                }

            case CorpusStratum.InflectedTitle:
                {
                    var pair = Pick(gt, rng);
                    var title = Inflect(pair.OccupationName, Pick(SwedishCorpusLexicon.InflectionSuffixes, rng));
                    return new GeneratedTitleCase(index, stratum, title,
                        DerivationExpectation.NonEmptyCandidates, null);
                }

            case CorpusStratum.MultiTrack:
                {
                    var pair = Pick(gt, rng);
                    var title = pair.OccupationName
                        + Pick(SwedishCorpusLexicon.TrackConnectors, rng)
                        + Pick(SwedishCorpusLexicon.SecondaryTracks, rng);
                    return new GeneratedTitleCase(index, stratum, title, DerivationExpectation.AnyOutcome, null);
                }

            case CorpusStratum.EmptyOrWeakSignal:
                {
                    var title = rng.Next(3) switch
                    {
                        0 => string.Empty,
                        1 => new string(' ', 1 + rng.Next(3)),
                        _ => Pick(SwedishCorpusLexicon.WeakTokens, rng),
                    };
                    return new GeneratedTitleCase(index, stratum, title, DerivationExpectation.AnyOutcome, null);
                }

            case CorpusStratum.LifeSituationGap:
                return new GeneratedTitleCase(index, stratum,
                    Pick(SwedishCorpusLexicon.LifeSituations, rng),
                    DerivationExpectation.NeverResolvesToSsyk, null);

            case CorpusStratum.NonStandardOrEnglishTitle:
                return new GeneratedTitleCase(index, stratum,
                    Pick(SwedishCorpusLexicon.NonStandardTitles, rng),
                    DerivationExpectation.AnyOutcome, null);

            case CorpusStratum.NoiseOrOcr:
                {
                    var title = Pick(SwedishCorpusLexicon.NoiseFragments, rng)
                        + Pick(SwedishCorpusLexicon.WeakTokens, rng)
                        + Pick(SwedishCorpusLexicon.NoiseFragments, rng);
                    return new GeneratedTitleCase(index, stratum, title, DerivationExpectation.AnyOutcome, null);
                }

            case CorpusStratum.NewlyArrived:
                // A title-shaped newly-arrived signal: a non-standard/English title.
                return new GeneratedTitleCase(index, stratum,
                    Pick(SwedishCorpusLexicon.NonStandardTitles, rng),
                    DerivationExpectation.AnyOutcome, null);

            case CorpusStratum.Adversarial:
                return new GeneratedTitleCase(index, stratum, AdversarialText(rng),
                    DerivationExpectation.AnyOutcome, null);

            default:
                // FakePersonnummer is CV-only; never reached for the title corpus.
                return new GeneratedTitleCase(index, stratum, string.Empty,
                    DerivationExpectation.AnyOutcome, null);
        }
    }

    // ── CV case builders ─────────────────────────────────────────────────

    private static GeneratedCvCase BuildCvCase(
        CorpusStratum stratum, int index, Random rng, IReadOnlyList<OccupationGroundTruth> gt)
    {
        var (content, rawText, language) = stratum switch
        {
            CorpusStratum.CleanExactTitle => StrongCv(Pick(gt, rng).OccupationName, rng),
            CorpusStratum.InflectedTitle => StrongCv(
                Inflect(Pick(gt, rng).OccupationName, Pick(SwedishCorpusLexicon.InflectionSuffixes, rng)), rng),
            CorpusStratum.MultiTrack => MultiTrackCv(Pick(gt, rng).OccupationName, rng),
            CorpusStratum.EmptyOrWeakSignal => EmptyCv(rng),
            CorpusStratum.LifeSituationGap => LifeSituationCv(rng),
            CorpusStratum.NonStandardOrEnglishTitle => EnglishCv(rng),
            CorpusStratum.NoiseOrOcr => NoiseCv(rng),
            CorpusStratum.NewlyArrived => NewlyArrivedCv(rng),
            CorpusStratum.Adversarial => AdversarialCv(rng),
            CorpusStratum.FakePersonnummer => FakePersonnummerCv(rng),
            _ => EmptyCv(rng),
        };

        // Run the real personnummer guard over the assembled free text — exactly as
        // ImportResumeCommandHandler does (Normalize → Scan → FromMatches). The outcome is
        // PII-safe (Found/Count/Kinds only; never the raw value).
        var scanCopy = PersonnummerTextNormalizer.Normalize(CollectFreeText(content, rawText));
        var personnummer = PersonnummerScanOutcome.FromMatches(PersonnummerScanner.Scan(scanCopy));

        var confidence = ConfidenceFor(stratum, content);
        var created = ParsedResume.Create(
            JobSeekerId.New(),
            $"CV_{stratum}_{index}.pdf",
            "application/pdf",
            language,
            content,
            rawText,
            confidence,
            personnummer,
            [],
            FixedClock.Default);

        if (created.IsFailure)
            // The generator controls all structural inputs; a failure here is a generator
            // bug, surfaced loudly (never a silent half-built case).
            throw new InvalidOperationException(
                $"Corpus generator produced a structurally invalid CV for {stratum}#{index}: {created.Error}");

        return new GeneratedCvCase(index, stratum, created.Value, personnummer.Found);
    }

    private static (ParsedResumeContent Content, string RawText, ResumeLanguage Language) StrongCv(
        string title, Random rng)
    {
        var org = Pick(SwedishCorpusLexicon.Organizations, rng);
        var verb = Pick(SwedishCorpusLexicon.ActionVerbs, rng);
        var bullet = SwedishCorpusLexicon.StrongBullet(verb, org);
        var content = new ParsedResumeContent(
            new ParsedContact("Anna Andersson", "anna@example.com", "070-123 45 67",
                Pick(SwedishCorpusLexicon.Locations, rng)),
            $"Erfaren {title.ToLowerInvariant()} med åtta års erfarenhet.",
            [new ParsedExperience(title, org, "2021–2024", $"{title}, {org}, 2021–2024. {bullet}")],
            [new ParsedEducation("KTH", "Civilingenjör", "2016–2021", "KTH Civilingenjör 2016–2021")],
            [.. SwedishCorpusLexicon.Skills.Take(3)],
            ["Svenska", "Engelska"]);
        var rawText = $"Anna Andersson\n{title}\n{bullet}";
        return (content, rawText, ResumeLanguage.Sv);
    }

    private static (ParsedResumeContent, string, ResumeLanguage) MultiTrackCv(string title, Random rng)
    {
        var secondary = Pick(SwedishCorpusLexicon.SecondaryTracks, rng);
        var org = Pick(SwedishCorpusLexicon.Organizations, rng);
        var content = new ParsedResumeContent(
            new ParsedContact("Bo Berg", "bo@example.com", "070-000 00 00", Pick(SwedishCorpusLexicon.Locations, rng)),
            $"Bakgrund som {title.ToLowerInvariant()} och {secondary}.",
            [
                new ParsedExperience(title, org, "2020–2024", $"{title}, {org}, 2020–2024."),
                new ParsedExperience(secondary, "Tidigare AB", "2012–2019", $"{secondary}, Tidigare AB, 2012–2019."),
            ],
            [new ParsedEducation("Yrkeshögskola", "Diplom", "2010–2012", "Yrkeshögskola Diplom 2010–2012")],
            [.. SwedishCorpusLexicon.Skills.Take(2)],
            ["Svenska"]);
        return (content, $"Bo Berg\n{title} och {secondary}", ResumeLanguage.Sv);
    }

    private static (ParsedResumeContent, string, ResumeLanguage) EmptyCv(Random rng) =>
        rng.Next(2) == 0
            ? (ParsedResumeContent.Empty, string.Empty, ResumeLanguage.Sv)
            : (new ParsedResumeContent(ParsedContact.Empty, null, [], [],
                [Pick(SwedishCorpusLexicon.WeakTokens, rng)], []), " ", ResumeLanguage.Sv);

    private static (ParsedResumeContent, string, ResumeLanguage) LifeSituationCv(Random rng)
    {
        var phrase = Pick(SwedishCorpusLexicon.LifeSituations, rng);
        var content = new ParsedResumeContent(
            new ParsedContact("Cecilia Carlsson", "cc@example.com", null, null),
            phrase,
            [new ParsedExperience(phrase, null, "2022–2024", $"{phrase} 2022–2024")],
            [], [], ["Svenska"]);
        return (content, $"Cecilia Carlsson\n{phrase}", ResumeLanguage.Sv);
    }

    private static (ParsedResumeContent, string, ResumeLanguage) EnglishCv(Random rng)
    {
        var title = Pick(SwedishCorpusLexicon.NonStandardTitles, rng);
        var content = new ParsedResumeContent(
            new ParsedContact("John Smith", "john@example.com", "+44 20 1234 5678", "London"),
            $"Experienced {title} delivering scalable systems.",
            [new ParsedExperience(title, "Globex", "2019-2024", $"{title} at Globex, 2019-2024. Led a team of 8.")],
            [new ParsedEducation("MIT", "B.Sc.", "2015-2019", "MIT B.Sc. 2015-2019")],
            ["C#", "Go", "Kubernetes"], ["English"]);
        return (content, $"John Smith\n{title}", ResumeLanguage.En);
    }

    private static (ParsedResumeContent, string, ResumeLanguage) NoiseCv(Random rng)
    {
        var noise = Pick(SwedishCorpusLexicon.NoiseFragments, rng);
        var content = new ParsedResumeContent(
            new ParsedContact($"D{noise}avid", $"d{noise}@example.com", "070 11 22 33", $"Stockholm{noise}"),
            $"Profil{noise} med erfarenhet{noise}.",
            [new ParsedExperience($"Tekniker{noise}", $"Org{noise}", "20â€”21", $"Tekniker{noise} 20â€”21")],
            [], [Pick(SwedishCorpusLexicon.Skills, rng)], []);
        return (content, $"D{noise}avid\nTekniker{noise}{noise}", ResumeLanguage.Sv);
    }

    private static (ParsedResumeContent, string, ResumeLanguage) NewlyArrivedCv(Random rng)
    {
        var name = Pick(SwedishCorpusLexicon.NonSwedishNames, rng);
        var content = new ParsedResumeContent(
            new ParsedContact(name, "newcomer@example.com", null, Pick(SwedishCorpusLexicon.Locations, rng)),
            "Recently arrived. Söker arbete. Open to work.",
            [new ParsedExperience(Pick(SwedishCorpusLexicon.SecondaryTracks, rng), null, null, "Tidigare arbete utomlands")],
            [], [Pick(SwedishCorpusLexicon.Skills, rng)], ["Engelska", "Arabiska"]);
        return (content, $"{name}\nSöker arbete", ResumeLanguage.En);
    }

    private static (ParsedResumeContent, string, ResumeLanguage) AdversarialCv(Random rng)
    {
        var rawText = AdversarialText(rng);
        var content = new ParsedResumeContent(
            new ParsedContact("XY", "a@b.c", "\t\t\t", new string('Z', 500)),
            new string('å', 1000),
            [new ParsedExperience(new string('q', 2000), "Org", "<script>alert(1)</script>", rawText[..Math.Min(rawText.Length, 4000)])],
            [], [], []);
        return (content, rawText, ResumeLanguage.Sv);
    }

    private static (ParsedResumeContent, string, ResumeLanguage) FakePersonnummerCv(Random rng)
    {
        var pnr = Pick(SwedishCorpusLexicon.FakePersonnummer, rng);
        var org = Pick(SwedishCorpusLexicon.Organizations, rng);
        var content = new ParsedResumeContent(
            new ParsedContact("Erik Eriksson", "erik@example.com", "070-555 00 00", "Göteborg"),
            $"Systemvetare. Personnummer: {pnr}.",
            [new ParsedExperience("Systemvetare", org, "2020–2024", $"Systemvetare, {org}. Pnr {pnr}.")],
            [new ParsedEducation("GU", "Kandidat", "2016–2019", "GU Kandidat 2016–2019")],
            ["SQL", "C#"], ["Svenska"]);
        return (content, $"Erik Eriksson\n{pnr}\nSystemvetare", ResumeLanguage.Sv);
    }

    private static ParseConfidence ConfidenceFor(CorpusStratum stratum, ParsedResumeContent content) =>
        stratum switch
        {
            CorpusStratum.EmptyOrWeakSignal => ParseConfidence.Failed(ParseFallbackReason.NoSectionsDetected),
            CorpusStratum.Adversarial => ParseConfidence.Failed(ParseFallbackReason.EncodingSuspect),
            CorpusStratum.NoiseOrOcr => ParseConfidence.FromSections(
            [
                new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Degraded, ["brus"]),
            ]),
            CorpusStratum.NewlyArrived or CorpusStratum.LifeSituationGap => ParseConfidence.FromSections(
            [
                new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, ["kontakt"]),
                new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Degraded, ["gles"]),
            ]),
            _ => ParseConfidence.FromSections(
            [
                new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, ["kontakt hittad"]),
                new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, ["1 post"]),
            ]),
        };

    // ── Shared helpers ───────────────────────────────────────────────────

    /// <summary>Concatenates every user-bearing field so the personnummer guard scans a
    /// BROADER surface than the import handler (which scans only <c>extraction.RawText</c>) —
    /// the wider scan guarantees a fake pnr is caught wherever the generator places it
    /// (raw text or any structured field). The scan/normalise chain itself is identical to
    /// <c>ImportResumeCommandHandler</c> (Normalize → Scan → FromMatches); the outcome stays
    /// PII-safe (Found/Count/Kinds only).</summary>
    private static string CollectFreeText(ParsedResumeContent content, string rawText)
    {
        var parts = new List<string?> { rawText, content.Profile,
            content.Contact.FullName, content.Contact.Email, content.Contact.Phone, content.Contact.Location };
        foreach (var e in content.Experience) { parts.Add(e.Title); parts.Add(e.Organization); parts.Add(e.Period); parts.Add(e.RawText); }
        foreach (var ed in content.Education) { parts.Add(ed.Institution); parts.Add(ed.Degree); parts.Add(ed.Period); parts.Add(ed.RawText); }
        parts.AddRange(content.Skills);
        parts.AddRange(content.Languages);
        return string.Join('\n', parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    private static string AdversarialText(Random rng) => rng.Next(4) switch
    {
        0 => new string('x', 50_000 + rng.Next(50_000)),                 // very long
        1 => "titel\u0000\u0001\u0002 med\tkontroll\u0007tecken",  // real control / NUL bytes (escaped in source)
        2 => "'; DROP TABLE taxonomy_concepts; -- {{7*7}} <script>",     // injection-shaped
        _ => string.Concat(Enumerable.Range(0, 200).Select(_ => Pick(SwedishCorpusLexicon.NoiseFragments, rng))),
    };

    private static string Inflect(string baseName, string suffix) =>
        suffix.StartsWith(' ') ? baseName + suffix : baseName.ToLowerInvariant() + suffix;

    private static T Pick<T>(IReadOnlyList<T> items, Random rng) => items[rng.Next(items.Count)];
}
