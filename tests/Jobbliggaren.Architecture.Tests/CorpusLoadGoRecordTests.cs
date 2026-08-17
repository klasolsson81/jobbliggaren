using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Fitness function for the corpus-load GO record (<c>release-checklist.md</c> §2.6 point 3.5).
///
/// <para>
/// The condition that gates <c>JobTech__IngestEnabled=true</c> — and therefore 51 347 recruiter
/// contact records, Art. 14 data about non-users — is <b>Klas's explicit written GO</b>. It is a
/// DECISION, not a derivable state, and deliberately so: four state-shaped conditions each failed
/// open on 2026-08-16, every time when their sub-condition discharged.
/// </para>
///
/// <para>
/// <b>Why a test and not a convention.</b> Point 3.5 records the GO with an adjudicator, a date
/// and a place, because Art. 5(2) requires compliance to be <i>demonstrable</i> and a tick without
/// an author demonstrates nothing. A prose convention cannot stop a session flipping the box to
/// <c>[x]</c> and leaving the record on its placeholder — and the checklist measures that such
/// things happen here: <b>point 3's</b> box was ticked, reverted and ticked again inside one day
/// (2026-08-16). Point 3.5's own box has never been ticked; the evidence is a sibling's, which is
/// why it is attributed rather than borrowed.
/// </para>
///
/// <para>
/// <b>Two complementary properties, and the second is what keeps the first alive.</b>
/// Ticked ⇒ the record carries none of its placeholders. Unticked ⇒ it carries all three
/// verbatim (<c>_(ej givet)_</c>, <c>**Datum:** —</c>, <c>**Var:** —</c>). The second exists because the first is a <b>negated</b> assertion, and a negated
/// assertion passes trivially once its pattern stops matching: reword the placeholder and
/// <c>ShouldNotContain</c> would go green on an absent literal, silently and permanently. The
/// drift vector is measured, not hypothetical — the line below the record already spells the same
/// words in a different emphasis style. Only the unticked property crosses the threshold in the
/// resting state, which is the state this repo is actually in.
/// </para>
///
/// <para>
/// <b>Scope, and only it:</b> the tests say nothing about whether a GO <i>should</i> be given —
/// that is Klas's alone — nor about the other legs of the gate. A ticked box with a filled record
/// passes even if the GO were unwise. The guard is against an <i>unattributable</i> tick, not an
/// unwise one, and against the record's own decay.
/// </para>
///
/// <para>
/// <b>Coherence runs both ways, and that is delivered rather than incidental.</b> An unticked box
/// with a filled-in record also fails — the checklist says an unticked box here means "GO not
/// given" and nothing else, so a filled record behind one is the file contradicting itself. The
/// cost is that noting the adjudicator <em>before</em> ticking is not a legal intermediate state:
/// fill the record and tick in the same edit.
/// </para>
///
/// <para>
/// Naming: <c>&lt;ClassUnderTest&gt;_&lt;Scenario&gt;_&lt;Expected&gt;</c>.
/// </para>
/// </summary>
public class CorpusLoadGoRecordTests
{
    private const string Checklist = "docs/runbooks/release-checklist.md";

    /// <summary>Identifies point 3.5 by its heading text, never by line number — line numbers in
    /// this file went stale four times in two days.</summary>
    private const string PointHeading = "3.5 KORPUSLADDNINGEN";

    private const string RecordLabel = "**GO givet av:**";
    private const string DateLabel = "**Datum:**";
    private const string PlaceLabel = "**Var:**";

    /// <summary>The three placeholders that together mean "no GO given". Held alive by
    /// <see cref="CorpusGate_WhenPointIsUnticked_RecordStillReadsItsPlaceholders"/>.</summary>
    private const string NoAdjudicatorPlaceholder = "_(ej givet)_";
    private const string NoDatePlaceholder = "**Datum:** —";
    private const string NoPlacePlaceholder = "**Var:** —";

    private static readonly string[] Placeholders =
        [NoAdjudicatorPlaceholder, NoDatePlaceholder, NoPlacePlaceholder];

    [Fact]
    public void CorpusGate_WhenPointIsTicked_CarriesAnAdjudicatedGoRecord()
    {
        var (checkboxLine, recordLine) = ReadPointMarkers();

        if (!IsTicked(checkboxLine))
        {
            // Unticked is the resting state; the complementary test below owns it.
            return;
        }

        foreach (var placeholder in Placeholders)
        {
            recordLine.ShouldNotContain(
                placeholder,
                // Insensitive here is the fail-CLOSED direction: a NEGATED assertion should catch
                // more spellings, not fewer. The positive assertion below is Sensitive for the
                // mirror reason — it pins one exact literal.
                Case.Insensitive,
                $"{Checklist} §2.6 point 3.5 is TICKED while its GO record still carries the " +
                $"placeholder \"{placeholder}\". The tick is a RECORD of Klas's explicit written " +
                "GO, never the authorisation itself — so a ticked box without adjudicator, date " +
                "and place demonstrates nothing, and Art. 5(2) requires compliance to be " +
                "demonstrable. Either fill in who gave the GO, when and where, or untick the " +
                "box. This gate stands in front of 51 347 recruiter contact records.");
        }
    }

    /// <summary>
    /// The complementary property, and the one that actually executes today. It pins the
    /// placeholders as PRESENT while the box is unticked, so the discriminator the ticked test
    /// negates cannot drift out of existence unnoticed. Without this, rewording the placeholder
    /// would leave both tests green and kill the guard permanently — and the record's own
    /// surrounding prose already spells the same words a different way one line below.
    /// </summary>
    [Fact]
    public void CorpusGate_WhenPointIsUnticked_RecordStillReadsItsPlaceholders()
    {
        var (checkboxLine, recordLine) = ReadPointMarkers();

        if (IsTicked(checkboxLine))
        {
            // A GO has been given and recorded; the ticked test owns that state.
            return;
        }

        foreach (var placeholder in Placeholders)
        {
            recordLine.ShouldContain(
                placeholder,
                Case.Sensitive,
                $"{Checklist} §2.6 point 3.5 is UNTICKED, so its GO record must still read " +
                $"\"{placeholder}\" verbatim. This assertion exists to keep that literal alive: " +
                "the ticked-state test NEGATES it, and a negated assertion passes trivially once " +
                "its pattern stops matching. Reword the placeholder without updating this file " +
                "and the whole guard goes silently green on the day the box is ticked. If the " +
                "wording must change, change it here in the same commit — do not delete this.");
        }
    }

    /// <summary>
    /// Vacuity guard. It pins the FORM the two tests above depend on, not the strings that found
    /// the lines — asserting that a line found by a substring contains that substring is a
    /// tautology and can never fail.
    /// </summary>
    [Fact]
    public void CorpusGate_WhateverTheTickState_PointIsACheckboxWithAThreeSlotRecord()
    {
        var (checkboxLine, recordLine) = ReadPointMarkers();

        checkboxLine.TrimStart().StartsWith("- [", StringComparison.Ordinal).ShouldBeTrue(
            $"the line carrying \"{PointHeading}\" in {Checklist} is no longer a markdown " +
            "checkbox item. Both tests above branch on its ticked state, so they would return " +
            "early on every run and guard nothing at all.");

        var trimmed = checkboxLine.TrimStart();
        trimmed.Length.ShouldBeGreaterThan(4, "the checkbox item is truncated");
        (trimmed[3] is ' ' or 'x' or 'X').ShouldBeTrue(
            $"point 3.5's checkbox state character is '{trimmed[3]}', which IsTicked cannot read. " +
            "Both tests above branch on it, so an unreadable state character routes every run to " +
            "the wrong branch silently.");

        // The adjudicator slot is pinned by the BINDING (ExactlyOneLineContaining searches for it),
        // so asserting it here would test this class's own search predicate. These two are not the
        // binding marker, so they genuinely pin that all three slots share one line.
        recordLine.ShouldContain(DateLabel, Case.Sensitive, "an undated GO cannot be told from one that has decayed");
        recordLine.ShouldContain(PlaceLabel, Case.Sensitive, "the place is what makes the GO re-readable");
    }

    /// <summary>
    /// Case-INSENSITIVE by GFM: the task-list state character is "either a whitespace character
    /// or the letter x in either lowercase or uppercase", so <c>- [X]</c> renders as checked. An
    /// ordinal comparison here fails OPEN — <c>- [X]</c> with a placeholder record would send the
    /// ticked test down its early return, leave the unticked test passing (the placeholders are
    /// still there) and satisfy the vacuity guard, which is three greens on the one combination
    /// this class exists to catch.
    /// </summary>
    private static bool IsTicked(string checkboxLine) =>
        checkboxLine.TrimStart().StartsWith("- [x]", StringComparison.OrdinalIgnoreCase);

    private static (string CheckboxLine, string RecordLine) ReadPointMarkers()
    {
        var path = Path.Combine(RepositoryRoot(), Checklist);
        File.Exists(path).ShouldBeTrue($"{Checklist} is the home of the corpus-load GO condition");

        var lines = File.ReadAllLines(path);

        var checkboxLine = ExactlyOneLineContaining(lines, PointHeading);
        var recordLine = ExactlyOneLineContaining(lines, RecordLabel);

        return (checkboxLine, recordLine);
    }

    /// <summary>
    /// Binds a marker only when it is unique. First-match binding is how a guard quietly rebinds
    /// to a cross-reference or a table-of-contents entry and keeps passing against the wrong line
    /// — the same reason <c>BackupUnitFilePinTests</c> requires uniqueness for its directives.
    /// </summary>
    private static string ExactlyOneLineContaining(string[] lines, string marker)
    {
        var matches = lines
            .Where(line => line.Contains(marker, StringComparison.Ordinal))
            .ToList();

        matches.Count.ShouldBe(
            1,
            $"expected exactly one line containing \"{marker}\" in {Checklist}, found " +
            $"{matches.Count}. Zero means the marker was renamed or deleted — repoint this guard " +
            "in the same change rather than removing it, because it is the only mechanical check " +
            "that a ticked corpus gate carries an attributable GO. More than one means a prose " +
            "cross-reference now shadows the real marker, and binding the first match would let " +
            "this guard assert against a line that is not the point at all.");

        return matches[0];
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null
               && !(File.Exists(Path.Combine(dir.FullName, "Jobbliggaren.sln"))
                    && Directory.Exists(Path.Combine(dir.FullName, "src"))))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("could not locate the repository root from " + AppContext.BaseDirectory);
        return dir.FullName;
    }
}
