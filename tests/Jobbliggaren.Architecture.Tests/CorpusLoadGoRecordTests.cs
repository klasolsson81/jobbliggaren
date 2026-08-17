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
/// an author demonstrates nothing. But a prose convention cannot stop a session from flipping the
/// box to <c>[x]</c> and leaving the record on its placeholder — and this file measures that such
/// things happen here: that same box was ticked, reverted and ticked again inside one day
/// (2026-08-16). Klas asked for the convention to be mechanically enforced rather than trusted.
/// </para>
///
/// <para>
/// <b>The property, and only it:</b> if point 3.5's box is ticked, its GO record must not still
/// carry the placeholder. The test says nothing about whether a GO <i>should</i> be given — that
/// is Klas's alone — nor about the other legs. A ticked box with a filled record passes even if
/// the GO were unwise; the guard is against an <i>unattributable</i> tick, not an unwise one.
/// </para>
///
/// <para>
/// <b>Failure direction.</b> Every way this test can break is loud: a moved marker, a renamed
/// point or a deleted record line all fail rather than pass silently. That is deliberate — a
/// guard on a 51 347-record legal gate must not be able to go vacuously green, which is the
/// defect class <see cref="JobSourceIngestGateConfigurationTests"/> exists for on the config side.
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

    /// <summary>The record line's label, and the placeholder that means "no GO given".</summary>
    private const string RecordLabel = "**GO givet av:**";
    private const string NotGivenPlaceholder = "_(ej givet)_";

    [Fact]
    public void CorpusGate_WhenPointIsTicked_CarriesAnAdjudicatedGoRecord()
    {
        var (checkboxLine, recordLine) = ReadPointMarkers();

        if (!checkboxLine.TrimStart().StartsWith("- [x]", StringComparison.OrdinalIgnoreCase))
        {
            // Unticked is the resting state and asserts nothing here: point 3.5 says an unticked
            // box means "GO not given" and nothing else. The record's own placeholder is then the
            // correct content, so there is nothing to check.
            return;
        }

        recordLine.ShouldNotContain(
            NotGivenPlaceholder,
            Case.Sensitive,
            $"{Checklist} §2.6 point 3.5 is TICKED while its GO record still reads " +
            $"\"{NotGivenPlaceholder}\". The tick is a RECORD of Klas's explicit written GO, never " +
            "the authorisation itself — so a ticked box with no adjudicator, date and place " +
            "demonstrates nothing, and Art. 5(2) requires compliance to be demonstrable. Either " +
            "fill in who gave the GO, when and where, or untick the box. This gate stands in " +
            "front of 51 347 recruiter contact records.");
    }

    /// <summary>
    /// Vacuity guard. The test above returns early on an unticked box, so every marker it depends
    /// on must be proven present independently — otherwise a renamed point or a deleted record
    /// line would make it pass by finding nothing to assert against, which is exactly how a guard
    /// on a legal gate goes silently green.
    /// </summary>
    [Fact]
    public void CorpusGate_MarkersAreActuallyPresent()
    {
        var (checkboxLine, recordLine) = ReadPointMarkers();

        checkboxLine.ShouldContain(PointHeading);
        recordLine.ShouldContain(RecordLabel);
        recordLine.ShouldContain("**Datum:**");
        recordLine.ShouldContain("**Var:**");
    }

    private static (string CheckboxLine, string RecordLine) ReadPointMarkers()
    {
        var path = Path.Combine(RepositoryRoot(), Checklist);
        File.Exists(path).ShouldBeTrue($"{Checklist} is the home of the corpus-load GO condition");

        var lines = File.ReadAllLines(path);

        var headingIndex = Array.FindIndex(lines, l => l.Contains(PointHeading, StringComparison.Ordinal));
        headingIndex.ShouldBeGreaterThanOrEqualTo(
            0,
            $"could not find \"{PointHeading}\" in {Checklist}. If the point was renamed, this " +
            "guard must be repointed in the same change — do not delete it: it is the only " +
            "mechanical check that a ticked corpus gate carries an attributable GO.");

        var recordIndex = Array.FindIndex(
            lines,
            headingIndex,
            l => l.Contains(RecordLabel, StringComparison.Ordinal));
        recordIndex.ShouldBeGreaterThanOrEqualTo(
            0,
            $"point 3.5 in {Checklist} no longer carries its \"{RecordLabel}\" record line. That " +
            "line is what makes a tick demonstrable under Art. 5(2); removing it silently " +
            "converts the gate back into an unattributable checkbox.");

        return (lines[headingIndex], lines[recordIndex]);
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
