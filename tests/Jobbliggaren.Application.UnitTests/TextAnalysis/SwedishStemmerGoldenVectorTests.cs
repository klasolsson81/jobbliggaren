using System.Collections.Concurrent;
using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.TextAnalysis;

// Fas 4 STEG 2 (F4-2) — svensk Snowball-stemmer (ADR 0074).
//
// RED PHASE: written BEFORE SnowballStemmer exists. References the
// to-be-created internal sealed type via InternalsVisibleTo (Infrastructure →
// Jobbliggaren.Application.UnitTests). Every behavioural test is expected to
// FAIL (compile-fail until the impl lands, then behaviour-fail until correct).
//
// These golden vectors are NOT hand-authored expectations — they are derived
// from the REAL PostgreSQL 18.3 to_tsvector('swedish') oracle (queried this
// session). They ARE the truth the libstemmer.net Snowball stemmer must match;
// any [InlineData] mismatch is the drift signal the CTO bound as a hard
// acceptance criterion (ADR 0074 — drift triggers a reactive STEG, never a TD).
//
// Naming: Method_Scenario_Expected.
public class SwedishStemmerGoldenVectorTests
{
    private static SnowballStemmer NewStemmer() => new();

    // ===============================================================
    // Golden vectors — word → expected stem (PG to_tsvector('swedish'))
    // ===============================================================
    //
    // NOTE the non-obvious definite-singular cases that MUST stay locked
    // (the stem KEEPS its form): systemet→systemet, arbetet→arbetet,
    // förskolan→förskolan, hälsan→hälsan, sjuksköterskan→sjuksköterskan,
    // ekonomin→ekonomin; and ansvar→ansv vs ansvarig→ansvar.
    [Theory]
    // lärare-familjen
    [InlineData("lärare", "lär")]
    [InlineData("läraren", "lär")]
    [InlineData("lärarens", "lär")]
    [InlineData("lärarna", "lär")]
    // utvecklare-familjen
    [InlineData("utvecklare", "utveckl")]
    [InlineData("utvecklaren", "utveckl")]
    [InlineData("utvecklarna", "utveckl")]
    // system — definite-singular keeps its form
    [InlineData("system", "system")]
    [InlineData("systemet", "systemet")]
    [InlineData("systemen", "system")]
    // arbete — arbetet keeps its form
    [InlineData("arbete", "arbet")]
    [InlineData("arbetet", "arbetet")]
    [InlineData("arbeten", "arbet")]
    [InlineData("arbeta", "arbet")]
    [InlineData("arbetar", "arbet")]
    [InlineData("arbetade", "arbet")]
    // förskola — åäö-bearing + definite-singular keeps its form
    [InlineData("förskola", "förskol")]
    [InlineData("förskolan", "förskolan")]
    [InlineData("förskolor", "förskol")]
    // hälsa — åäö-bearing + definite-singular keeps its form
    [InlineData("hälsa", "häls")]
    [InlineData("hälsan", "hälsan")]
    // sjuksköterska — definite-singular keeps its form
    [InlineData("sjuksköterska", "sjukskötersk")]
    [InlineData("sjuksköterskan", "sjuksköterskan")]
    [InlineData("sjuksköterskor", "sjukskötersk")]
    // ingenjör — åäö-bearing
    [InlineData("ingenjör", "ingenjör")]
    [InlineData("ingenjören", "ingenjör")]
    [InlineData("ingenjörer", "ingenjör")]
    // roll-titlar
    [InlineData("programmerare", "programmer")]
    [InlineData("projektledare", "projektled")]
    // ekonomi — ekonomin keeps its form
    [InlineData("ekonomi", "ekonomi")]
    [InlineData("ekonomin", "ekonomin")]
    [InlineData("ekonomisk", "ekonomisk")]
    // chef
    [InlineData("chef", "chef")]
    [InlineData("chefen", "chef")]
    [InlineData("chefer", "chef")]
    // diverse yrken/begrepp
    [InlineData("undersköterska", "underskötersk")]
    [InlineData("butik", "butik")]
    [InlineData("butiken", "butik")]
    [InlineData("butiker", "butik")]
    [InlineData("erfarenhet", "erfaren")]
    [InlineData("erfarenheter", "erfaren")]
    [InlineData("kunskap", "kunskap")]
    [InlineData("kunskaper", "kunskap")]
    // ansvar — the locked non-obvious pair: ansvar→ansv vs ansvarig→ansvar
    [InlineData("ansvar", "ansv")]
    [InlineData("ansvarig", "ansvar")]
    // ledning/ledare
    [InlineData("ledning", "ledning")]
    [InlineData("ledare", "led")]
    // svensk
    [InlineData("svenska", "svensk")]
    [InlineData("svensk", "svensk")]
    public void Stem_SwedishGoldenVector_MatchesPostgresToTsvector(
        string word, string expectedStem)
    {
        var stemmer = NewStemmer();

        var stem = stemmer.Stem(word, TextLanguage.Swedish);

        stem.ShouldBe(expectedStem);
    }

    // ===============================================================
    // åäö encoding round-trip guard — diacritics must survive verbatim
    // ===============================================================

    [Theory]
    [InlineData("förskola", "förskol")]
    [InlineData("hälsa", "häls")]
    [InlineData("ingenjör", "ingenjör")]
    public void Stem_WordWithDiacritics_PreservesAaoEncoding(
        string word, string expectedStem)
    {
        var stem = NewStemmer().Stem(word, TextLanguage.Swedish);

        // Guard against a UTF-8 → Latin-1 / mojibake regression: the returned
        // stem must equal the PG-derived golden byte-for-byte, and the åäö run
        // must be intact (no '?'/replacement chars, no double-encoding).
        stem.ShouldBe(expectedStem);
        stem.ShouldNotContain('�'); // replacement char
        stem.ShouldNotContain('?');
    }

    // ===============================================================
    // English stemming — wired in F4-9 (ADR 0074 F4-2 amendment). No longer
    // throws; full to_tsvector('english') correctness is the parity gate
    // (EnglishStemmerPostgresParityTests).
    // ===============================================================

    [Fact]
    public void Stem_EnglishLanguage_StemsViaSnowball_F49()
    {
        var stemmer = NewStemmer();

        // Snowball English: "running" → "run" (the parity gate proves the full corpus).
        stemmer.Stem("running", TextLanguage.English).ShouldBe("run");
    }

    // ===============================================================
    // Concurrency (CTO bindande villkor) — proves the [ThreadStatic]
    // stateful-stemmer safety. Snowball.SwedishStemmer mutates instance
    // state and is NOT safe for concurrent calls on one instance; the
    // singleton must give every thread its own instance. A shared singleton
    // stemmed across threads must return each word's golden stem with zero
    // cross-thread contamination.
    // ===============================================================

    [Fact]
    public void Stem_ConcurrentAccessAcrossThreads_AlwaysReturnsGoldenStem()
    {
        // ONE shared singleton, exactly as DI registers it — the test must
        // exercise the [ThreadStatic] per-thread instance, not a per-test new().
        var sharedStemmer = NewStemmer();

        (string Word, string Expected)[] vectors =
        [
            ("lärare", "lär"),
            ("utvecklare", "utveckl"),
            ("systemet", "systemet"),
            ("arbetet", "arbetet"),
            ("förskolan", "förskolan"),
            ("sjuksköterskan", "sjuksköterskan"),
            ("ingenjör", "ingenjör"),
            ("ansvar", "ansv"),
            ("ansvarig", "ansvar"),
            ("svenska", "svensk"),
        ];

        var failures = new ConcurrentBag<string>();

        // 5000 iterations × 10 vectors across all cores — high contention so a
        // shared-mutable-state regression (e.g. dropping [ThreadStatic] or
        // serialising) surfaces deterministically as a wrong/garbled stem.
        Parallel.For(0, 5000, _ =>
        {
            foreach (var (word, expected) in vectors)
            {
                var actual = sharedStemmer.Stem(word, TextLanguage.Swedish);
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    failures.Add($"{word} → '{actual}' (väntat '{expected}')");
                }
            }
        });

        failures.ShouldBeEmpty(
            "Stemmern måste vara trådsäker (per-tråd [ThreadStatic]-instans); " +
            $"avvikelser: {string.Join("; ", failures.Distinct())}");
    }

    [Fact]
    public void Stem_RepeatedCallsOnSameInstance_AreDeterministic()
    {
        // Sequential reuse of one instance is the documented safe path
        // (Snowball is reusable sequentially). Hammer the same word and a
        // mix to assert no buffer-state bleed between sequential calls.
        var stemmer = NewStemmer();

        for (var i = 0; i < 1000; i++)
        {
            stemmer.Stem("utvecklaren", TextLanguage.Swedish).ShouldBe("utveckl");
            stemmer.Stem("ansvar", TextLanguage.Swedish).ShouldBe("ansv");
            stemmer.Stem("ansvarig", TextLanguage.Swedish).ShouldBe("ansvar");
        }
    }
}
