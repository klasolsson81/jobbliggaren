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
    // Digits are never part of a name, which disposes of phones, periods and street addresses in one
    // rule (and is why no separate phone/date arm exists).
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

    [Fact]
    public void TryPersonName_RefusesALineOverTheLengthCap()
    {
        // The cap the old heuristic already carried (60), kept. Two tokens, both capitalised, so ONLY
        // the cap can refuse this — which is what makes the assertion able to fail if the cap goes.
        var line = new string('A', 40) + " " + new string('B', 40);

        line.Length.ShouldBeGreaterThan(ContactPatterns.MaxNameLength);
        ContactPatterns.TryPersonName(line, Particles, out _).ShouldBeFalse();
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
