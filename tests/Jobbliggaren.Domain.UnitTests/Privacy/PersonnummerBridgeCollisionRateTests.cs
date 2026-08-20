using Jobbliggaren.Domain.Privacy;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Privacy;

/// <summary>
/// The measured ground for ADR 0134's profile split: how often a bridged gap yields a VALID
/// personnummer, for two shapes that differ only in whether the leading run is a real date.
/// ADR 0134 owns why that matters; this file is the instrument that keeps its numbers
/// regenerable instead of decaying in prose.
///
/// <para>The assertions are PROPERTIES with headroom, not the raw counts: the exact rates are a
/// function of the corpus of valid dates and of Luhn, and pinning them to the digit would make
/// an unrelated change to either read as a failure here.</para>
/// </summary>
public sealed class PersonnummerBridgeCollisionRateTests
{
    private const int Seed = 20260820;
    private const int Trials = 200_000;

    /// <summary>
    /// Two unrelated numbers bridged into a personnummer candidate: the case the F4-8
    /// analysis named, and the case its "~1-in-hundreds" estimate describes correctly.
    /// </summary>
    private static double ArbitraryEightPlusFourCollisionRate()
    {
        var rng = new Random(Seed);
        var hits = 0;
        for (var i = 0; i < Trials; i++)
        {
            var candidate = string.Create(12, rng, static (span, r) =>
            {
                for (var k = 0; k < span.Length; k++) span[k] = (char)('0' + r.Next(10));
            });
            if (Personnummer.TryParse(candidate, out _)) hits++;
        }
        return (double)hits / Trials;
    }

    /// <summary>
    /// A REAL date column stacked above four arbitrary digits — the shape a line-break bridge
    /// would join in extracted document text, and the shape
    /// <c>ResumeContentPersonnummerGuard.CollectFreeText</c> manufactures wholesale by joining
    /// separate DTO fields with <c>AppendLine</c>.
    /// </summary>
    private static double DateColumnPlusFourCollisionRate()
    {
        var rng = new Random(Seed);
        var origin = new DateOnly(1900, 1, 1);
        var span = new DateOnly(2010, 12, 31).DayNumber - origin.DayNumber;
        var hits = 0;
        for (var i = 0; i < Trials; i++)
        {
            var date = origin.AddDays(rng.Next(span));
            var tail = rng.Next(10_000);
            var candidate = string.Concat(
                date.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
                tail.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
            if (Personnummer.TryParse(candidate, out _)) hits++;
        }
        return (double)hits / Trials;
    }

    [Fact]
    public void ArbitraryDigits_CollideRarely_TheF4Dash8EstimateHoldsForTheCaseItNamed()
    {
        var rate = ArbitraryEightPlusFourCollisionRate();

        // "~1-in-hundreds": bounded on BOTH sides, so this fails if the gate ever stops
        // rejecting (rate -> 1) as loudly as if it stopped accepting (rate -> 0). A one-sided
        // assertion here would pass vacuously against a TryParse that rejected everything,
        // which is the same blindness that let the 23 937-vs-23 968 collector gap survive.
        rate.ShouldBeGreaterThan(0.001, "a rate of zero would mean the probe never reaches TryParse's accept path");
        rate.ShouldBeLessThan(0.05, "two arbitrary numbers must remain a rare coincidence");
    }

    [Fact]
    public void DateColumnAboveFourDigits_CollidesAnOrderOfMagnitudeMoreOften()
    {
        var arbitrary = ArbitraryEightPlusFourCollisionRate();
        var dateColumn = DateColumnPlusFourCollisionRate();

        // The property, not the digits: a real date column is MUCH likelier to complete a
        // valid personnummer than arbitrary digits are, because it satisfies date sanity with
        // certainty and leaves only Luhn. That is why ExtractedDocumentText does not bridge a
        // line break, and it is the half of the estimate F4-8 never separated out.
        dateColumn.ShouldBeGreaterThan(
            arbitrary * 5,
            "if a date column stopped being materially likelier to collide than arbitrary " +
            "digits, the ground for excluding newline from the ExtractedDocumentText profile " +
            "would be gone and ADR 0134 would need re-adjudicating, not quiet widening");

        // Luhn alone admits ~1 in 10, so a date column lands in that neighbourhood. Bounded
        // above too: a rate near 1 would mean the Luhn gate had stopped discriminating.
        dateColumn.ShouldBeInRange(0.05, 0.20);
    }

    [Fact]
    public void TheMeasurementIsDeterministic_SameSeedSameAnswer()
    {
        // Without this, a later refactor could swap in an unseeded Random and the two tests
        // above would still pass most days — a flaky ground for an accepted residual is not
        // a ground at all.
        ArbitraryEightPlusFourCollisionRate().ShouldBe(ArbitraryEightPlusFourCollisionRate());
        DateColumnPlusFourCollisionRate().ShouldBe(DateColumnPlusFourCollisionRate());
    }
}
