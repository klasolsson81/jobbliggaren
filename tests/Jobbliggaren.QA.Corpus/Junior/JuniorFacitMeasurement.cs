using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.JobAds.Junior;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Jobbliggaren.QA.Corpus.Junior;

/// <summary>
/// #2264 — measures the junior rules against Klas's hand reading, on the app's own ad texts: every ad comes from
/// JobTech through the app's own client and <c>PlatsbankenJobSource</c>, and through <see cref="JobAd.Import"/>, so
/// the classifier reads exactly the post-scrub title and text the app stores. Opt-in: skipped unless
/// <c>JBL_JUNIOR_FACIT_DIR</c> names the local reading folder (outside the repository; never written to).
///
/// <para>
/// <b>Counts only.</b> No ad text, title, employer, note or id is printed, logged or asserted on — an assertion on text
/// would print it on failure. The reading's note field is not even deserialised. The report goes to the file named by
/// <c>JBL_JUNIOR_MEASURE_OUT</c> (a scratch path), else to the test output. Every logger is the null logger, because
/// the source logs external ids; a per-ad failure is counted by its error code or exception type, never its message.
/// </para>
/// </summary>
public sealed class JuniorFacitMeasurement
{
    private const string FacitDirVariable = "JBL_JUNIOR_FACIT_DIR";
    private const string OutVariable = "JBL_JUNIOR_MEASURE_OUT";
    private const double Threshold = 0.95;
    private const int QueueDays = 14;

    private static readonly HashSet<string> ShownLabels = new(StringComparer.Ordinal)
    {
        "junior", "trainee", "ingen-erfarenhet", "bada",
    };

    private static readonly string[] LabelOrder =
        ["junior", "trainee", "bada", "ingen-erfarenhet", "inte-junior", "handleda", "kraver-3-plus"];

    // Diagnostics only (never production data): which part of the candidate signal brings a listed ad in.
    private static readonly (string Name, System.Text.RegularExpressions.Regex Pattern)[] CandidateTerms =
        new[] { "junior", "trainee", "graduate", "nyexam", "nyutexam", @"nyexad\b", "entry[- ]level", "internship",
            @"praktik\b", @"praktikant\b", @"praktikplats\b", "early career", "början av karriären", "början av din karriär",
            "första jobb", "student" }
        .Select(t => (t, new System.Text.RegularExpressions.Regex(@"\b(" + t + ")",
            System.Text.RegularExpressions.RegexOptions.NonBacktracking | System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant)))
        .ToArray();

    private static readonly TimeZoneInfo Sweden = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    [Fact]
    public async Task Measure_the_junior_rules_against_the_reading()
    {
        var facitDir = Environment.GetEnvironmentVariable(FacitDirVariable);
        if (string.IsNullOrWhiteSpace(facitDir))
            Assert.Skip($"{FacitDirVariable} is not set; the measurement runs only on request, locally.");

        var ct = TestContext.Current.CancellationToken;
        var facit = ReadFacit(facitDir);
        var ruleSet = JuniorRuleSetLoader.LoadDataIt();
        var groups = DataItGroups(ruleSet.OccupationFieldConceptId);

        await using var services = BuildServices();
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<IJobSource>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var now = clock.UtcNow;

        // The reading as shipped in the rule file (junior ids only, Klas 2026-11-20) decides the /junior projection.
        var readingRuleSet = ruleSet;

        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        var facitResults = new Dictionary<string, Measured>(StringComparer.Ordinal);
        var population = new List<Measured>();
        var listedFacit = new HashSet<string>(StringComparer.Ordinal);
        var snapshotItems = 0;
        var snapshotWithFlag = 0;

        await foreach (var item in source.FetchSnapshotAsync(new SnapshotOutcomeRecorder(), ct))
        {
            snapshotItems++;
            if (item.Facets.ExperienceRequired is not null)
                snapshotWithFlag++;

            var inField = item.Facets.OccupationGroupConceptId is { } g && groups.Contains(g);
            var inFacit = facit.ContainsKey(item.ExternalId);
            if (!inField && !inFacit)
                continue;

            var measured = Measure(item, ruleSet, clock, failures);
            if (measured is null)
                continue;

            if (inFacit)
                facitResults[item.ExternalId] = measured;
            if (inField && (item.ExpiresAt is null || item.ExpiresAt >= now))
            {
                population.Add(measured);
                if (inFacit)
                    listedFacit.Add(item.ExternalId);
            }
        }

        var unavailable = 0;
        foreach (var id in facit.Keys.Where(id => !facitResults.ContainsKey(id)).ToList())
        {
            var item = await source.RefetchByExternalIdAsync(id, ct);
            var measured = item is null ? null : Measure(item, ruleSet, clock, failures);
            if (measured is null)
                unavailable++;
            else
                facitResults[id] = measured;
        }

        var (streamUpserts, streamWithFlag) = await CountStreamFlagAsync(source, now, ct);

        var report = new StringBuilder();
        WriteReport(report, ruleSet, readingRuleSet.Reading, facit, facitResults, unavailable, population, now,
            snapshotItems, snapshotWithFlag, streamUpserts, streamWithFlag, listedFacit);
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Per-ad failures, by code or type: {(failures.Count == 0 ? "none" : string.Join(", ", failures.Select(f => $"{f.Key} {f.Value}")))}.");
        await EmitAsync(report.ToString(), ct);

        facitResults.Count.ShouldBeGreaterThan(0);
    }

    private static Measured? Measure(JobAdImportItem item, JuniorRuleSet rules, IDateTimeProvider clock, Dictionary<string, int> failures)
    {
        try
        {
            return MeasureOne(item, rules, clock, failures);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Count(failures, e.GetType().Name);
            return null;
        }
    }

    private static void Count(Dictionary<string, int> tally, string key) =>
        tally[key] = tally.TryGetValue(key, out var n) ? n + 1 : 1;

    private static Measured? MeasureOne(JobAdImportItem item, JuniorRuleSet rules, IDateTimeProvider clock, Dictionary<string, int> failures)
    {
        // The app's own funnel: the scrub is an aggregate invariant of Import, so title and description below are the
        // stored, post-scrub values.
        var company = Company.Create(item.CompanyName);
        var external = ExternalReference.Create(JobSource.Platsbanken, item.ExternalId);
        if (company.IsFailure || external.IsFailure)
        {
            Count(failures, company.IsFailure ? company.Error.Code : external.Error.Code);
            return null;
        }

        var imported = JobAd.Import(
            item.Title, company.Value, item.Description, item.Url, external.Value, item.SanitizedRawPayload,
            item.Facets, item.DeclaredContacts, item.PublishedAt, item.ExpiresAt, clock,
            static (_, _) => ExtractedTerms.Empty, item.ApplicationUrl);
        if (imported.IsFailure)
        {
            Count(failures, imported.Error.Code);
            return null;
        }

        var ad = imported.Value;
        var input = new JuniorClassificationInput(item.ExternalId, ad.Title, ad.Description, item.Facets.ExperienceRequired);
        var orgNr = item.Facets.OrganizationNumber;
        return new Measured(
            JuniorClassifier.Evaluate(rules, input, useReading: false),
            input,
            item.PublishedAt,
            new EmployerShape(
                PersonnummerShaped: orgNr is not null && OrganizationNumber.FromTrusted(orgNr).IsPersonnummerShaped(),
                Missing: orgNr is null,
                NotTenCharacters: orgNr is not null && orgNr.Length != 10),
            item.Facets.MunicipalityConceptId);
    }

    private static async Task<(int Upserts, int WithFlag)> CountStreamFlagAsync(IJobSource source, DateTimeOffset now, CancellationToken ct)
    {
        var upserts = 0;
        var withFlag = 0;
        await foreach (var change in source.StreamChangesAsync(now.AddHours(-2), ct))
        {
            if (change is not JobAdUpsert upsert)
                continue;
            upserts++;
            if (upsert.Item.Facets.ExperienceRequired is not null)
                withFlag++;
        }

        return (upserts, withFlag);
    }

    private static void WriteReport(
        StringBuilder r,
        JuniorRuleSet ruleSet,
        JuniorReading reading,
        Dictionary<string, FacitRow> facit,
        Dictionary<string, Measured> results,
        int unavailable,
        List<Measured> population,
        DateTimeOffset now,
        int snapshotItems,
        int snapshotWithFlag,
        int streamUpserts,
        int streamWithFlag,
        HashSet<string> listedFacit)
    {
        var measured = results.Select(kv => (Label: facit[kv.Key].Label, Shown: ShownLabels.Contains(facit[kv.Key].Label), Result: kv.Value, Row: facit[kv.Key])).ToList();
        var shownTotal = measured.Count(m => m.Shown);
        var notShownTotal = measured.Count - shownTotal;

        r.AppendLine(CultureInfo.InvariantCulture, $"# Junior rules vs the reading — rule set {ruleSet.Version}, measured {now:yyyy-MM-dd HH:mm}Z");
        r.AppendLine();
        r.AppendLine(CultureInfo.InvariantCulture, $"Reading: {facit.Count} ads; measured {measured.Count} ({shownTotal} shown labels, {notShownTotal} not shown); no longer retrievable {unavailable}.");
        r.AppendLine(CultureInfo.InvariantCulture, $"Candidate filter catches {measured.Count(m => m.Result.Evaluation.IsCandidate)} of {measured.Count} read ads ({measured.Count(m => m.Shown && m.Result.Evaluation.IsCandidate)} of {shownTotal} shown).");
        r.AppendLine();

        r.AppendLine("## Per rule, standalone (the rule's conditions hold, regardless of order)");
        r.AppendLine();
        r.AppendLine("| Rule | Says | Fires | Right | Precision | Wilson 95 % low | Coverage | Wrong, by label |");
        r.AppendLine("|---|---|---:|---:|---:|---:|---:|---|");
        var stats = new List<RuleStats>();
        foreach (var rule in ruleSet.Rules)
        {
            var fired = measured.Where(m => m.Result.Evaluation.MatchingRuleIds.Contains(rule.Id)).ToList();
            var saysJunior = rule.Verdict == JuniorVerdict.Junior;
            var right = fired.Count(m => m.Shown == saysJunior);
            var wrong = fired.Where(m => m.Shown != saysJunior).GroupBy(m => m.Label).OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key} {g.Count()}");
            var precision = fired.Count == 0 ? 0 : (double)right / fired.Count;
            var low = Wilson(right, fired.Count);
            var coverage = (double)right / (saysJunior ? shownTotal : notShownTotal);
            stats.Add(new RuleStats(rule.Id, fired.Count, precision, low));
            r.AppendLine(CultureInfo.InvariantCulture,
                $"| {rule.Id} | {(saysJunior ? "junior" : "inte junior")} | {fired.Count} | {right} | {Pct(precision, fired.Count)} | {Pct(low, fired.Count)} | {Pct(coverage, 1)} | {string.Join(", ", wrong)} |");
        }

        r.AppendLine();
        WriteYears(r, measured);

        var pointAuto = stats.Where(s => s.Fires >= 10 && s.Precision >= Threshold).Select(s => s.RuleId).ToHashSet(StringComparer.Ordinal);
        var wilsonAuto = stats.Where(s => s.Fires > 0 && s.WilsonLow >= Threshold).Select(s => s.RuleId).ToHashSet(StringComparer.Ordinal);

        var shipped = ruleSet.Rules.Where(x => x.Auto).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        r.AppendLine(CultureInfo.InvariantCulture,
            $"Rules that meet Klas's criterion (≥ 95 % on ≥ 10 cases, 2026-11-20): {string.Join(", ", ruleSet.Rules.Where(x => pointAuto.Contains(x.Id)).Select(x => x.Id))}. Automatic in the file: {string.Join(", ", ruleSet.Rules.Where(x => x.Auto).Select(x => x.Id))}. Wilson lower bound ≥ 95 %: {(wilsonAuto.Count == 0 ? "none" : string.Join(", ", wilsonAuto))}.");
        r.AppendLine();

        foreach (var (name, auto) in new[] { ("as shipped (the file's auto flags)", shipped) })
        {
            var simulated = ruleSet with { Rules = ruleSet.Rules.Select(x => x with { Auto = auto.Contains(x.Id) }).ToList() };
            r.AppendLine(CultureInfo.InvariantCulture, $"## Criterion {name}");
            r.AppendLine();
            r.AppendLine(CultureInfo.InvariantCulture, $"Automatic rules: {(auto.Count == 0 ? "none" : string.Join(", ", ruleSet.Rules.Where(x => auto.Contains(x.Id)).Select(x => x.Id)))}.");
            r.AppendLine();
            WriteSequence(r, simulated, measured, shownTotal);
            WriteListedReading(r, simulated, facit, results, listedFacit);
            WritePopulation(r, simulated, reading, population, now);
        }

        WriteCandidateDiagnostics(r, population);

        r.AppendLine("## The experience mark at the source");
        r.AppendLine();
        r.AppendLine(CultureInfo.InvariantCulture, $"Snapshot: {snapshotWithFlag} of {snapshotItems} ads carry it. Stream, last two hours: {streamWithFlag} of {streamUpserts} upserts carry it.");
    }

    private static void WriteYears(StringBuilder r, List<(string Label, bool Shown, Measured Result, FacitRow Row)> measured)
    {
        var bothNull = measured.Count(m => m.Result.Evaluation.YearsRequired is null && m.Row.ArMin is null);
        var equal = measured.Count(m => m.Result.Evaluation.YearsRequired is { } y && m.Row.ArMin == y);
        var readNone = measured.Count(m => m.Result.Evaluation.YearsRequired is null && m.Row.ArMin is not null);
        var readExtra = measured.Count(m => m.Result.Evaluation.YearsRequired is not null && m.Row.ArMin is null);
        var differ = measured.Count(m => m.Result.Evaluation.YearsRequired is { } y && m.Row.ArMin is { } a && a != y);
        r.AppendLine("## The years reader vs the reading's minimum years");
        r.AppendLine();
        r.AppendLine(CultureInfo.InvariantCulture, $"Same value {equal} · both none {bothNull} · reader found none where the reading has one {readNone} · reader found one where the reading has none {readExtra} · different values {differ}.");
        r.AppendLine();
    }

    private static void WriteSequence(StringBuilder r, JuniorRuleSet simulated, List<(string Label, bool Shown, Measured Result, FacitRow Row)> measured, int shownTotal)
    {
        var verdicts = measured.Select(m => (m.Label, m.Shown, Verdict: JuniorClassifier.Evaluate(simulated, m.Result.Input, useReading: false).Classification.Verdict)).ToList();

        r.AppendLine("| Label | Junior | Not junior | Unclear |");
        r.AppendLine("|---|---:|---:|---:|");
        foreach (var label in LabelOrder)
        {
            var rows = verdicts.Where(v => v.Label == label).ToList();
            r.AppendLine(CultureInfo.InvariantCulture,
                $"| {label} | {rows.Count(v => v.Verdict == JuniorVerdict.Junior)} | {rows.Count(v => v.Verdict == JuniorVerdict.NotJunior)} | {rows.Count(v => v.Verdict == JuniorVerdict.Unclear)} |");
        }

        var autoJunior = verdicts.Where(v => v.Verdict == JuniorVerdict.Junior).ToList();
        var autoNot = verdicts.Where(v => v.Verdict == JuniorVerdict.NotJunior).ToList();
        var junRight = autoJunior.Count(v => v.Shown);
        var notRight = autoNot.Count(v => !v.Shown);
        r.AppendLine();
        r.AppendLine(CultureInfo.InvariantCulture,
            $"Shown automatically: {autoJunior.Count}, right {junRight} ({Pct(autoJunior.Count == 0 ? 0 : (double)junRight / autoJunior.Count, autoJunior.Count)}); coverage {Pct((double)junRight / shownTotal, 1)} of the shown labels. " +
            $"Left out automatically: {autoNot.Count}, right {notRight} ({Pct(autoNot.Count == 0 ? 0 : (double)notRight / autoNot.Count, autoNot.Count)}). " +
            $"Unclear (the queue): {verdicts.Count(v => v.Verdict == JuniorVerdict.Unclear)} of {verdicts.Count}.");
        r.AppendLine();
    }

    // What the reading still decides today: the read ads listed now, by what the rules alone would say of them.
    private static void WriteListedReading(
        StringBuilder r, JuniorRuleSet simulated, Dictionary<string, FacitRow> facit, Dictionary<string, Measured> results, HashSet<string> listedFacit)
    {
        var listed = listedFacit.Where(results.ContainsKey)
            .Select(id => (Shown: ShownLabels.Contains(facit[id].Label), Verdict: JuniorClassifier.Evaluate(simulated, results[id].Input, useReading: false).Classification.Verdict))
            .ToList();
        string Row(bool shown) => string.Join(", ", new[] { JuniorVerdict.Junior, JuniorVerdict.NotJunior, JuniorVerdict.Unclear }
            .Select(v => $"{v} {listed.Count(x => x.Shown == shown && x.Verdict == v)}"));
        r.AppendLine(CultureInfo.InvariantCulture,
            $"Read ads listed now: {listed.Count}. Shown labels by the rules alone: {Row(true)}. Not-shown labels by the rules alone: {Row(false)}.");
        r.AppendLine();
    }

    private static void WritePopulation(StringBuilder r, JuniorRuleSet simulated, JuniorReading reading, List<Measured> population, DateTimeOffset now)
    {
        var rulesOnly = population.Select(p => (p.PublishedAt, JuniorClassifier.Evaluate(simulated, p.Input, useReading: false).Classification.Verdict)).ToList();
        var withReadingSet = simulated with { Reading = reading };
        var shown = population.Where(p => JuniorClassifier.Classify(withReadingSet, p.Input).Verdict == JuniorVerdict.Junior).ToList();
        var listJunior = shown.Count;
        var perKommun = shown.GroupBy(p => p.MunicipalityConceptId).Where(g => g.Key is not null).Select(g => g.Count()).ToList();

        r.AppendLine(CultureInfo.InvariantCulture,
            $"Listed Data/IT ads now: {population.Count}. Rules alone: junior {rulesOnly.Count(x => x.Verdict == JuniorVerdict.Junior)}, not junior {rulesOnly.Count(x => x.Verdict == JuniorVerdict.NotJunior)}, unclear {rulesOnly.Count(x => x.Verdict == JuniorVerdict.Unclear)}. " +
            $"**/junior would show {listJunior}** (rules plus the reading as rule 0, before any queue decision).");
        r.AppendLine();
        r.AppendLine(CultureInfo.InvariantCulture,
            $"The shown set's employers (M4 b): personnummer-shaped organisation number {shown.Count(p => p.Employer.PersonnummerShaped)}, without one {shown.Count(p => p.Employer.Missing)}, not ten characters {shown.Count(p => p.Employer.NotTenCharacters)}; kommuner {perKommun.Count}, of which with exactly one ad {perKommun.Count(c => c == 1)}; without a kommun {shown.Count(p => p.MunicipalityConceptId is null)}.");
        r.AppendLine();

        var today = LocalDate(now);
        r.AppendLine("Unclear per publication day, rules alone (the future queue per day; older days undercount ads that have already closed):");
        r.AppendLine();
        r.AppendLine("| Day | Published | Unclear |");
        r.AppendLine("|---|---:|---:|");
        var lastWeek = new List<int>();
        for (var d = 1; d <= QueueDays; d++)
        {
            var day = today.AddDays(-d);
            var onDay = rulesOnly.Where(x => LocalDate(x.PublishedAt) == day).ToList();
            var unclear = onDay.Count(x => x.Verdict == JuniorVerdict.Unclear);
            if (d <= 7)
                lastWeek.Add(unclear);
            r.AppendLine(CultureInfo.InvariantCulture, $"| {day:yyyy-MM-dd} {day.DayOfWeek.ToString()[..3]} | {onDay.Count} | {unclear} |");
        }

        r.AppendLine();
        r.AppendLine(CultureInfo.InvariantCulture,
            $"Last seven full days: mean {lastWeek.Average():0.0} unclear a day, min {lastWeek.Min()}, max {lastWeek.Max()}.");
        r.AppendLine();
    }

    private static void WriteCandidateDiagnostics(StringBuilder r, List<Measured> population)
    {
        var candidates = population.Where(p => p.Evaluation.IsCandidate).ToList();
        r.AppendLine("## What makes a listed Data/IT ad a candidate");
        r.AppendLine();
        r.AppendLine(CultureInfo.InvariantCulture, $"Candidates: {candidates.Count} of {population.Count}. Marked 'no experience required': {population.Count(p => p.Input.ExperienceRequired == false)} (of which candidates only through the mark: {candidates.Count(p => p.Input.ExperienceRequired == false && !CandidateTerms.Any(t => t.Pattern.IsMatch(p.Input.Title) || t.Pattern.IsMatch(p.Input.Description)))}).");
        r.AppendLine();
        r.AppendLine("| Term | In title | In text | The only term |");
        r.AppendLine("|---|---:|---:|---:|");
        foreach (var (name, pattern) in CandidateTerms)
        {
            var inTitle = population.Count(p => pattern.IsMatch(p.Input.Title));
            var inText = population.Count(p => pattern.IsMatch(p.Input.Description));
            var only = population.Count(p =>
                (pattern.IsMatch(p.Input.Title) || pattern.IsMatch(p.Input.Description))
                && CandidateTerms.Count(t => t.Pattern.IsMatch(p.Input.Title) || t.Pattern.IsMatch(p.Input.Description)) == 1);
            r.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {inTitle} | {inText} | {only} |");
        }

        r.AppendLine();
    }

    private static DateOnly LocalDate(DateTimeOffset at) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, Sweden).DateTime);

    private static double Wilson(int right, int n)
    {
        if (n == 0)
            return 0;
        const double z = 1.96;
        var p = (double)right / n;
        var denominator = 1 + z * z / n;
        var centre = p + z * z / (2 * n);
        var margin = z * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n));
        return (centre - margin) / denominator;
    }

    private static string Pct(double value, int n) =>
        n == 0 ? "–" : (value * 100).ToString("0.0", CultureInfo.InvariantCulture) + " %";

    private static async Task EmitAsync(string report, CancellationToken ct)
    {
        var outPath = Environment.GetEnvironmentVariable(OutVariable);
        if (!string.IsNullOrWhiteSpace(outPath))
            await File.WriteAllTextAsync(outPath, report, ct);
        else
            TestContext.Current.TestOutputHelper?.WriteLine(report);
    }

    private static ServiceProvider BuildServices()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        // The source logs external ids; nothing it logs may reach the transcript.
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddJobSources(configuration);
        return services.BuildServiceProvider();
    }

    private static HashSet<string> DataItGroups(string fieldConceptId)
    {
        var field = TaxonomySnapshotLoader.Load().OccupationFields.Single(f => f.ConceptId == fieldConceptId);
        return (field.OccupationGroups ?? []).Select(g => g.ConceptId).ToHashSet(StringComparer.Ordinal);
    }

    private static Dictionary<string, FacitRow> ReadFacit(string dir)
    {
        using var stream = File.OpenRead(Path.Combine(dir, "junior-facit.json"));
        var file = JsonSerializer.Deserialize<FacitFile>(stream) ?? throw new InvalidOperationException("The reading is empty.");
        return file.Annonser.ToDictionary(a => a.Id, StringComparer.Ordinal);
    }

    private sealed record Measured(
        JuniorEvaluation Evaluation,
        JuniorClassificationInput Input,
        DateTimeOffset PublishedAt,
        EmployerShape Employer,
        string? MunicipalityConceptId);

    private sealed record EmployerShape(bool PersonnummerShaped, bool Missing, bool NotTenCharacters);

    private sealed record RuleStats(string RuleId, int Fires, double Precision, double WilsonLow);

    // Only the fields the measurement reads are mapped; the reading's note is never deserialised.
    private sealed record FacitFile([property: JsonPropertyName("annonser")] IReadOnlyList<FacitRow> Annonser);

    private sealed record FacitRow(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("etikett")] string Label,
        [property: JsonPropertyName("arMin")] int? ArMin);


}
