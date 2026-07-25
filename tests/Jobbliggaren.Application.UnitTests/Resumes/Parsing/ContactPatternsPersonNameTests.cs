using System.Collections.Frozen;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #898 — <c>ContactPatterns.TryPersonName</c>, the recogniser that replaced the heuristic
/// "the first substantial line under 60 characters that is not an e-mail, a phone or a date".
///
/// <para>The heuristic ALWAYS answered, which is why it answered "Systemutvecklare" on a job-title-
/// above-the-name CV and half a summary sentence on another. These tests pin BOTH halves of the
/// replacement: what it recognises, and — at least as important — what it REFUSES rather than
/// guesses. A refusal is not a gap in coverage here; it is the product decision (ADR 0040
/// propose-and-approve: the user is asked, never shown an invention — ADR 0071).</para>
///
/// <para>The particle vocabulary is passed in, exactly as production passes the lexicon's
/// <c>nameParticles</c>: the recogniser owns FORM and never vocabulary.</para>
/// </summary>
public class ContactPatternsPersonNameTests
{
    /// <summary>The SHIPPED vocabulary — never a local copy of it, or this suite would guard data
    /// with a different set than production reads (the integrity-suite lesson, 8b.4a).</summary>
    private static readonly FrozenSet<string> Particles =
        CvParsingLexiconFixture.Load().NameParticles;

    [Theory]
    // Ordinary Swedish names, one to three given names.
    [InlineData("Anna Andersson", "Anna Andersson")]
    [InlineData("Anna Maria Andersson", "Anna Maria Andersson")]
    [InlineData("Karl Johan Gustav Bernadotte", "Karl Johan Gustav Bernadotte")]
    // Å/Ä/Ö are uppercase letters, not an exotic case.
    [InlineData("Örjan Öberg", "Örjan Öberg")]
    // A CV header written in caps is very common and must pass: the token TAIL is unconstrained.
    [InlineData("ANNA ANDERSSON", "ANNA ANDERSSON")]
    // The bullet a PDF extractor emits for a sidebar line never reaches the field: the glue trim is
    // INSIDE the recogniser, so no call site has to remember it (#844's lesson, applied to the name).
    [InlineData("• Anna Andersson", "Anna Andersson")]
    [InlineData("- Anna Andersson", "Anna Andersson")]
    [InlineData("· Anna Andersson ·", "Anna Andersson")]
    // Lowercase particles between capitalised tokens — the whole reason nameParticles is data.
    [InlineData("Anna von Sydow", "Anna von Sydow")]
    [InlineData("Anna van der Berg", "Anna van der Berg")]
    [InlineData("Omar bin Salim", "Omar bin Salim")]
    public void TryPersonName_RecognisesAName(string line, string expected)
    {
        ContactPatterns.TryPersonName(line, Particles, out var name).ShouldBeTrue();
        name.ShouldBe(expected);
    }

    [Theory]
    // THE defect in the issue title, layout 1: a one-token job title above the name. Indistinguishable
    // in shape from a mononym, so the token band refuses both — see the mononym test in
    // HeadingDrivenResumeSegmenterTests for why that trade is the honest one.
    [InlineData("Systemutvecklare")]
    [InlineData("Zlatan")]
    // Layout 2: the first line of the user's SUMMARY, which the heuristic reported as her name
    // (39 chars, no mail/phone/date — it cleared every check the heuristic had).
    [InlineData("Erfaren undersköterska, tio år i yrket.")]
    [InlineData("Trygg i stressade lägen.")]
    // Prose that happens to be short: lowercase non-particle tokens are what gives it away.
    [InlineData("Söker jobb")]
    [InlineData("driven och noggrann")]
    // A rail line before subtraction — the residue is what makes the name reachable, not the recogniser.
    [InlineData("Anna Andersson | anna@example.com")]
    [InlineData("Anna Andersson Anna@example.com")]
    // Phone, period and street lines. NOTE these are refused by the token band / capitalised-token
    // rule as much as by the digit rule, so they are NOT a pin on the digit rule — see
    // TryPersonName_RefusesADigit_EvenWhenEveryTokenIsCapitalised for that.
    [InlineData("070-123 45 67")]
    [InlineData("2021 - 2024 Volvo AB")]
    [InlineData("Storgatan 12")]
    // A labelled line is not a bare name (accepted false negative, stated in the recogniser's doc).
    [InlineData("Namn: Anna Andersson")]
    // Five tokens is prose far more often than it is a name.
    [InlineData("Anna Maria Kristina Elisabet Andersson")]
    // A line of nothing but particles is not a name.
    [InlineData("von der")]
    // Nothing at all.
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("•")]
    public void TryPersonName_RefusesRatherThanGuesses(string line)
    {
        ContactPatterns.TryPersonName(line, Particles, out var name).ShouldBeFalse();
        name.ShouldBe(string.Empty);
    }

    [Theory]
    // ONLY the digit rule can refuse these: 2–3 tokens, EVERY token capitalised, no colon, no e-mail,
    // under the cap. Delete `char.IsAsciiDigit(c)` and they go red — which is what makes them a pin on
    // the DIGIT rule rather than on the token band it otherwise hides behind. (The first draft of this
    // suite pinned "070-123 45 67", "2021 - 2024 Volvo AB" and "Storgatan 12", all of which the token
    // band and the capitalised-token rule refuse on their own: mutation-verified green with the digit
    // branch deleted. The rule's own doc predicted that failure mode and the tests walked into it.)
    //
    // This pin is load-bearing beyond itself: deleting LooksLikePhone and LooksLikeDatePeriod rests
    // entirely on "the digit rule is a superset of both shapes", and a superset argument is only as
    // good as the test that can see the rule is alive. A superscript or footnote marker glued onto a
    // surname is exactly what a PDF extractor produces.
    [InlineData("Anna Andersson2")]
    [InlineData("Anna A1 Andersson")]
    public void TryPersonName_RefusesADigit_EvenWhenEveryTokenIsCapitalised(string line) =>
        ContactPatterns.TryPersonName(line, Particles, out _).ShouldBeFalse();

    [Theory]
    // The line must be ONE item. Without the fragment rule the two call sites asked different
    // questions: the preamble arm passes residue fragments (so "Anna Andersson, Undersköterska"
    // arrives split and resolves to the name), the Kontakt-block arm passes raw lines — and the same
    // text became the "name" WITH the job title attached. Same class for a city pair.
    [InlineData("Anna Andersson, Undersköterska")]
    [InlineData("Göteborg, Sverige")]
    [InlineData("Anna Andersson | Systemutvecklare")]
    public void TryPersonName_RefusesALineThatGluesTheNameToASecondItem(string line) =>
        ContactPatterns.TryPersonName(line, Particles, out _).ShouldBeFalse();

    [Fact]
    public void TryPersonName_AcceptsATwoTokenCapitalisedJobTitle_AcceptedFalsePositive()
    {
        // The trade the token band buys, pinned where the rule lives rather than left for a reader to
        // discover. A Title-Case two-token job title has the SAME shape as a name — "Legitimerad
        // Sjuksköterska", "Grafisk Formgivare", "Senior Utvecklare" are ordinary Swedish CV header
        // lines — and no deterministic rule separates them. Refusing the shape would cost every
        // two-token name, which is most of them.
        //
        // It is pinned because it is the error the USER sees: a false negative yields an honest gap,
        // a false positive yields a wrong name in a field labelled namn that B3 then verdicts on.
        // If a future change claims to close this, this test must be the one that goes red.
        ContactPatterns.TryPersonName("Legitimerad Sjuksköterska", Particles, out var name)
            .ShouldBeTrue();
        name.ShouldBe("Legitimerad Sjuksköterska");
    }

    [Fact]
    public void TryPersonName_AcceptsAParticleBesideAnyCapitalisedWord_AcceptedFalsePositive()
    {
        // The particle vocabulary's own trade, stated. Several particles are ordinary Swedish words
        // ("de", "den", "du", "la", "le"), so a two-token line whose second token is one of them
        // passes. The cost is bounded by the same Title-Case class above and buys "Anna von Sydow";
        // the pin exists so a future particle addition ("och", "för") cannot widen the name field
        // silently.
        ContactPatterns.TryPersonName("Ansvarig de", Particles, out _).ShouldBeTrue();
    }

    [Fact]
    public void TryPersonName_RefusesAtExactlyOneCharacterOverTheCap()
    {
        // The cap is 60, and the boundary is what pins it. The first draft used an 81-char line, so
        // MaxNameLength could have been changed to anything in 60..80 without a red test — a pin on
        // "some cap exists", not on THE cap.
        var exactly60 = new string('A', 29) + " " + new string('B', 30);
        var exactly61 = new string('A', 30) + " " + new string('B', 30);

        exactly60.Length.ShouldBe(ContactPatterns.MaxNameLength);
        exactly61.Length.ShouldBe(ContactPatterns.MaxNameLength + 1);

        ContactPatterns.TryPersonName(exactly60, Particles, out _).ShouldBeTrue();
        ContactPatterns.TryPersonName(exactly61, Particles, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryPersonName_DoesNotRewriteTheUsersText()
    {
        // Tokenisation drives the DECISION, never the VALUE. The double space is the user's own
        // spacing: collapsing it would be the engine editing what she wrote, which the display-form
        // invariants (#893, ADR 0108 §5) rejected for exactly the same reason one layer over.
        ContactPatterns.TryPersonName("Anna  Andersson", Particles, out var name).ShouldBeTrue();
        name.ShouldBe("Anna  Andersson");
    }

    [Fact]
    public void TryPersonName_ReadsTheParticlesItIsGiven_NotAHardcodedSet()
    {
        // The vocabulary is a PARAMETER. An empty set must refuse the very name the shipped set
        // accepts — otherwise a hardcoded C# list could be hiding behind the parameter (§5), and every
        // particle assertion above would be passing for the wrong reason.
        ContactPatterns.TryPersonName("Anna von Sydow", Particles, out _).ShouldBeTrue();
        ContactPatterns.TryPersonName("Anna von Sydow", FrozenSet<string>.Empty, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryPersonName_MatchesParticlesCaseInsensitively()
    {
        // "ANNA VON SYDOW" reaches the capitalised arm, so the case-insensitive particle lookup needs
        // its own case: a lowercase-authored particle must still match a token that is NOT capitalised
        // but is written with mixed case by the extractor.
        ContactPatterns.TryPersonName("Anna vOn Sydow", Particles, out var name).ShouldBeTrue();
        name.ShouldBe("Anna vOn Sydow");
    }
}
