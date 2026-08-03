using System.Text.RegularExpressions;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #1060 road 3 — <see cref="CvMonthNames"/>, the shared month-word home, and the two properties it
/// must hold that nothing else can check ((S3) obligations 3 and the prefix half of 2).
///
/// <para><b>Why the correspondence test is the one that matters.</b> The pattern and the ordinal map
/// are two halves of one fact, and they fail in opposite, silent ways. An alternative present in the
/// pattern but missing from the map makes <c>TryParsePoint</c> refuse a form the segmenter can
/// extract — the K1 class, one layer down. An entry in the map with no alternative in the pattern is
/// a token production can never deliver, so it is dead weight that reads as coverage.
/// <c>mars</c>/<c>mar</c>/<c>march</c> = 3 and <c>maj</c>/<c>may</c> = 5 are one transposition apart
/// from silently wrong A4 gap maths and B7 chronology, and no other test in the tree would redden.
/// </para>
/// </summary>
public class CvMonthNamesTests
{
    // The alternation, recovered from the shared const rather than re-typed. Re-typing the list here
    // would create the third copy the whole design forbids, and it would make this test agree with
    // itself instead of with production.
    private static string[] PatternAlternatives()
    {
        var alternatives = CvMonthNames.Pattern.TrimStart('(', '?', ':').TrimEnd(')').Split('|');

        // THE FLOOR LIVES HERE, not in one caller. This recovers the alternation by string-
        // manipulating the const, so a change to Pattern's SHAPE — a nested group, a quantifier, a
        // capture — degrades the parse rather than breaking it. Every caller must inherit the
        // guard: the prefix-order test iterates pairs, and a collapsed list gives zero iterations
        // and silent green. A test whose non-vacuity is inherited from a sibling is one refactor
        // away from measuring nothing.
        alternatives.Length.ShouldBeGreaterThan(30,
            "the alternation parse collapsed — Pattern's shape changed under this helper.");

        return alternatives;
    }

    [Fact]
    public void EveryPatternAlternative_HasAnOrdinal_AndEveryOrdinalHasAnAlternative()
    {
        var alternatives = PatternAlternatives().ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var token in alternatives)
        {
            CvMonthNames.TryGetOrdinal(token, out var month).ShouldBeTrue(
                $"[{token}] is matchable by the shared pattern, so PeriodParser can be handed it — " +
                "an alternative with no ordinal refuses a period the segmenter extracted.");
            month.ShouldBeInRange(1, 12);
        }

        foreach (var known in CvMonthNames.KnownTokens)
        {
            alternatives.ShouldContain(known,
                $"[{known}] maps to a month but no alternative can produce it — dead weight that " +
                "reads as coverage.");
        }
    }

    [Theory]
    // Both languages, all three lengths, and the abbreviating period. Spelled out rather than
    // generated: a table generated from the map would assert the map against itself.
    [InlineData("januari", 1)]
    [InlineData("january", 1)]
    [InlineData("jan", 1)]
    [InlineData("februari", 2)]
    [InlineData("february", 2)]
    [InlineData("febr", 2)]
    [InlineData("feb", 2)]
    [InlineData("mars", 3)]
    [InlineData("march", 3)]
    [InlineData("mar", 3)]
    [InlineData("april", 4)]
    [InlineData("apr", 4)]
    [InlineData("maj", 5)]
    [InlineData("may", 5)]
    [InlineData("juni", 6)]
    [InlineData("june", 6)]
    [InlineData("jun", 6)]
    [InlineData("juli", 7)]
    [InlineData("july", 7)]
    [InlineData("jul", 7)]
    [InlineData("augusti", 8)]
    [InlineData("august", 8)]
    [InlineData("aug", 8)]
    [InlineData("september", 9)]
    [InlineData("sept", 9)]
    [InlineData("sep", 9)]
    [InlineData("oktober", 10)]
    [InlineData("october", 10)]
    [InlineData("okt", 10)]
    [InlineData("oct", 10)]
    [InlineData("november", 11)]
    [InlineData("nov", 11)]
    [InlineData("december", 12)]
    [InlineData("dec", 12)]
    // Casing only. NOT "Dec." — the abbreviating period is consumed by AfterName, which sits
    // OUTSIDE the capture group in both consumers, so the token this method receives never
    // carries one. A row for "Dec." would assert a mechanism production cannot deliver.
    [InlineData("MARS", 3)]
    [InlineData("Dec", 12)]
    public void TryGetOrdinal_ResolvesTheRightMonth(string token, int expected)
    {
        CvMonthNames.TryGetOrdinal(token, out var month).ShouldBeTrue();
        month.ShouldBe(expected);
    }

    [Fact]
    public void TheOrdering_IsPrefixFree_WhichIsTheContractTheCodeActuallyHolds()
    {
        // THE INVARIANT, CHECKED MECHANICALLY — and it is deliberately NOT "longest first".
        // Length-order is sufficient but not necessary; this list is not sorted by length and has
        // zero prefix inversions. Stating the unverifiable rule is what an earlier revision did,
        // and a contract a reader cannot check is the problem commit 1 exists to fix, not a
        // restatement of its fix. No inversion COUNT is published: it is an artefact of how the
        // list happens to be written, and this loop checks the property instead.
        var alternatives = PatternAlternatives();

        for (var i = 0; i < alternatives.Length; i++)
        {
            for (var j = i + 1; j < alternatives.Length; j++)
            {
                alternatives[j].StartsWith(alternatives[i], StringComparison.OrdinalIgnoreCase)
                    .ShouldBeFalse(
                        $"[{alternatives[i]}] precedes [{alternatives[j]}] and is a prefix of it, so " +
                        "the shorter branch is tried first. Ordered alternation takes the first " +
                        "branch that lets the OVERALL match succeed.");
            }
        }
    }

    [Theory]
    // The present-keyword lexicon, shared for the same reason the month list is (architect R3).
    // DateRange matches these as an end point so ExtractPeriod STORES them; PeriodParser must read
    // every one back. Both rows go through the real segmenter and the real parser, so a drift
    // between the regex alternation and the array cannot pass.
    [InlineData("nuvarande")]
    [InlineData("pågående")]
    [InlineData("pagaende")]
    [InlineData("present")]
    [InlineData("current")]
    [InlineData("now")]
    [InlineData("idag")]
    [InlineData("nu")]
    public void EveryPresentKeyword_IsBothMatchedAndParsed(string keyword)
    {
        var range = $"2020 – {keyword}";

        DatePatterns.DateRange().Match(range).Value.ShouldBe(range,
            $"[{keyword}] must be reachable as an end point, or the range is truncated.");
        PeriodParser.TryParse(range, out _, out var end, out _).ShouldBeTrue(
            $"[{keyword}] must be readable back, or the stored period is dropped downstream.");
        end.ShouldBe(DateOnly.MaxValue, "an ongoing role uses the far-future sentinel.");
    }

    [Fact]
    public void ThePresentKeywords_ArePrefixOrdered_LikeTheMonthList()
    {
        // "nu" is a prefix of "nuvarande", so this list has the same ordering obligation the month
        // list has — and only the regex consumer cares, which is exactly the kind of asymmetry that
        // makes a shared const worth more than two agreeing copies.
        var keywords = CvMonthNames.PresentKeywords.Split('|');

        for (var i = 0; i < keywords.Length; i++)
        {
            for (var j = i + 1; j < keywords.Length; j++)
            {
                keywords[j].StartsWith(keywords[i], StringComparison.OrdinalIgnoreCase)
                    .ShouldBeFalse($"[{keywords[i]}] precedes [{keywords[j]}] and is a prefix of it.");
            }
        }
    }

    [Fact]
    public void TheMonthGap_IsHorizontalWhitespaceOnly_InTheSharedFragment()
    {
        // (S3) — the V2 fix on the shared fragment, so BOTH consumers inherit line-locality from one
        // token. This composes the two constants exactly as production does, but it is NOT the
        // load-bearing pin: a test-built regex cannot fail if someone inlines \s+ back into either
        // consumer. That is what DateModelWideningStoredPeriodTests' newline case is for, and it
        // goes through the real segmenter. Both exist because they fail for different reasons — this
        // one localises a break to the fragment, that one proves production actually reads it.
        var monthPoint = new Regex("^" + CvMonthNames.Pattern + CvMonthNames.AfterName + @"\d{4}$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        monthPoint.IsMatch("maj 2020").ShouldBeTrue("a space is the ordinary case.");
        monthPoint.IsMatch("maj\t2020").ShouldBeTrue("a tab is still horizontal.");
        monthPoint.IsMatch("dec. 2024").ShouldBeTrue("the abbreviating period is tolerated.");
        monthPoint.IsMatch("maj\n2020").ShouldBeFalse(
            "a month point is a LINE-LOCAL grammar — crossing a newline lifts a word out of a " +
            "neighbouring bullet and stores it as part of the period.");
        monthPoint.IsMatch("maj\r\n2020").ShouldBeFalse("the same, for CRLF sources.");
    }
}
