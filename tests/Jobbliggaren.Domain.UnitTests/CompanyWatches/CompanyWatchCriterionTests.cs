using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.CompanyWatches;

/// <summary>
/// #560 kriterie-vågen PR-1 (Fork A1, ADR 0105 RF-1) — invariants for the
/// <see cref="CompanyWatchCriterion"/> aggregate root: the <see cref="CompanyWatchCriterion.Create"/>
/// guards, label normalization (blank = unlabelled, not an error), and the write-path preconditions
/// on <see cref="CompanyWatchCriterion.UpdateCriteria"/> / <see cref="CompanyWatchCriterion.Rename"/>.
///
/// <para>
/// <b>There is no lifecycle axis to test.</b> Delete is HARD (C-D8/CTO Fork G1) — the row is removed,
/// never stamped — so the aggregate has exactly two states, present and gone, and the second one is
/// not representable in memory. The soft-delete apparatus PR-3 carried (a <c>SoftDelete()</c> with
/// zero production callers, a <c>DeletedAt</c> stamp, an EF query filter, a <c>deleted_at</c> column)
/// was demolished wholesale in the follow-up migration; the tests that pinned its idempotency and
/// its payload-retention contrast with <see cref="CompanyWatch"/> went with it, because they
/// described a state that can no longer exist. Art. 5(1)(c) is satisfied by the account-level Art. 17
/// hard-delete cascade (pinned behaviourally in <c>HardDeleteAccountsJobIntegrationTests</c>).
/// </para>
/// </summary>
public class CompanyWatchCriterionTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly Guid ValidUserId = Guid.NewGuid();

    private static CompanyWatchCriteriaSpec ItInStockholm() =>
        CompanyWatchCriteriaSpec.Create(["62010"], ["0180"]).Value;

    private static CompanyWatchCriteriaSpec ConsultingInGoteborg() =>
        CompanyWatchCriteriaSpec.Create(["62020"], ["1480"]).Value;

    private static CompanyWatchCriterion CreateValid(string? label = null) =>
        CompanyWatchCriterion.Create(ValidUserId, ItInStockholm(), label, Clock).Value;

    // ---------------------------------------------------------------
    // Create — happy path + guards
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithValidData_CreatesActiveCriterion()
    {
        var criteria = ItInStockholm();

        var result = CompanyWatchCriterion.Create(ValidUserId, criteria, "IT i Stockholm", Clock);

        result.IsSuccess.ShouldBeTrue();
        var criterion = result.Value;
        criterion.UserId.ShouldBe(ValidUserId);
        criterion.Criteria.ShouldBe(criteria);
        criterion.Label.ShouldBe("IT i Stockholm");
        criterion.CreatedAt.ShouldBe(Clock.UtcNow);
        criterion.UpdatedAt.ShouldBe(Clock.UtcNow);
        criterion.Id.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Create_WithEmptyUserId_Fails()
    {
        var result = CompanyWatchCriterion.Create(Guid.Empty, ItInStockholm(), null, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriterion.UserIdRequired");
    }

    [Fact]
    public void Create_WithNullCriteria_Fails()
    {
        var result = CompanyWatchCriterion.Create(ValidUserId, null!, null, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriterion.CriteriaRequired");
    }

    [Fact]
    public void Create_RaisesNoDomainEvents()
    {
        // Deliberate (mirrors CompanyWatch / UserJobAdMatch): the Art. 17 cascade is handler-driven
        // by UserId and the browse is a read — there is no reactive consumer of a criterion event.
        CreateValid().DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Create_TwiceForTheSameUser_ProducesDistinctIdentities()
    {
        var first = CreateValid();
        var second = CreateValid();

        second.Id.ShouldNotBe(first.Id);
    }

    // ---------------------------------------------------------------
    // Label — optional, trimmed, blank means UNLABELLED (never an error)
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithNullLabel_LeavesTheCriterionUnlabelled()
    {
        CreateValid(label: null).Label.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Create_WithWhitespaceOnlyLabel_StoresNullInsteadOfFailing(string blank)
    {
        // "no label" and "a label of spaces" are the same user intent — the picker must not throw a
        // validation error at someone who simply did not name their criterion.
        CreateValid(label: blank).Label.ShouldBeNull();
    }

    [Fact]
    public void Create_WithPaddedLabel_TrimsIt()
    {
        CreateValid(label: "  IT i Stockholm  ").Label.ShouldBe("IT i Stockholm");
    }

    [Fact]
    public void Create_WithLabelExactlyAtMaxLength_Succeeds()
    {
        var atMax = new string('a', CompanyWatchCriterion.LabelMaxLength);

        var result = CompanyWatchCriterion.Create(ValidUserId, ItInStockholm(), atMax, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Label!.Length.ShouldBe(CompanyWatchCriterion.LabelMaxLength);
    }

    [Fact]
    public void Create_WithLabelOneOverMaxLength_Fails()
    {
        var overMax = new string('a', CompanyWatchCriterion.LabelMaxLength + 1);

        var result = CompanyWatchCriterion.Create(ValidUserId, ItInStockholm(), overMax, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriterion.LabelTooLong");
    }

    [Fact]
    public void Create_WithMaxLengthLabelWrappedInWhitespace_Succeeds()
    {
        // The length is measured AFTER the trim — otherwise a stray trailing space in the form would
        // reject a label the user can see is exactly at the limit. It also guarantees the DB column
        // (varchar(LabelMaxLength)) can never overflow: what is measured is what is stored.
        var padded = $"  {new string('a', CompanyWatchCriterion.LabelMaxLength)}  ";

        var result = CompanyWatchCriterion.Create(ValidUserId, ItInStockholm(), padded, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Label!.Length.ShouldBe(CompanyWatchCriterion.LabelMaxLength);
    }

    // ---------------------------------------------------------------
    // Criteria getter — a SNAPSHOT, never a window into the aggregate's mutable state
    // ---------------------------------------------------------------

    [Fact]
    public void Criteria_HeldAcrossAnUpdate_DoesNotChangeUnderTheHolder()
    {
        // The getter computes a VO over two MUTABLE backing lists that UpdateCriteria Clears and
        // AddRanges IN PLACE. If the getter aliased those lists, this previously-read spec would
        // silently mutate under a caller that is still holding it — a value object that changes
        // value is not a value object.
        var criterion = CreateValid();
        var before = criterion.Criteria;

        criterion.UpdateCriteria(ConsultingInGoteborg(), Clock).IsSuccess.ShouldBeTrue();

        before.SniCodes.ShouldBe(["62010"], "en redan hämtad spec är en ögonblicksbild, inte en vy");
        before.MunicipalityCodes.ShouldBe(["0180"]);
        criterion.Criteria.SniCodes.ShouldBe(["62020"]);
    }

    [Fact]
    public void Criteria_ReadTwice_ReturnsEqualButIndependentSnapshots()
    {
        var criterion = CreateValid();

        var first = criterion.Criteria;
        var second = criterion.Criteria;

        first.ShouldBe(second);                       // structural equality
        ReferenceEquals(first, second).ShouldBeFalse(); // fresh copy per read — no shared mutable state
    }

    // ---------------------------------------------------------------
    // UpdateCriteria — active-only, stamps UpdatedAt, never touches CreatedAt
    // ---------------------------------------------------------------

    [Fact]
    public void UpdateCriteria_OnActiveCriterion_ReplacesThePredicateAndStampsUpdatedAt()
    {
        var criterion = CreateValid();
        var later = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(5));
        var replacement = ConsultingInGoteborg();

        var result = criterion.UpdateCriteria(replacement, later);

        result.IsSuccess.ShouldBeTrue();
        criterion.Criteria.ShouldBe(replacement);
        criterion.UpdatedAt.ShouldBe(later.UtcNow);
        criterion.CreatedAt.ShouldBe(Clock.UtcNow, "CreatedAt är oföränderlig");
    }

    [Fact]
    public void UpdateCriteria_ReplacesTheOldCodesEntirely_NeverAppendsToThem()
    {
        // Clear-then-AddRange, not AddRange: an update that appended would quietly WIDEN the user's
        // watch to industries they just removed — a bevakning that keeps notifying about a sector
        // the user deselected is the silent-surplus twin of the silent-miss bug.
        var criterion = CreateValid();

        criterion.UpdateCriteria(ConsultingInGoteborg(), Clock);

        criterion.Criteria.SniCodes.ShouldBe(["62020"]);
        criterion.Criteria.SniCodes.ShouldNotContain("62010");
        criterion.Criteria.MunicipalityCodes.ShouldBe(["1480"]);
        criterion.Criteria.MunicipalityCodes.ShouldNotContain("0180");
    }

    [Fact]
    public void UpdateCriteria_WithNull_Fails_AndLeavesTheCriterionUntouched()
    {
        var criterion = CreateValid();
        var original = criterion.Criteria;

        var result = criterion.UpdateCriteria(null!, FakeDateTimeProvider.At(Clock.UtcNow.AddDays(5)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriterion.CriteriaRequired");
        criterion.Criteria.ShouldBe(original);
        criterion.UpdatedAt.ShouldBe(Clock.UtcNow, "en avvisad ändring får inte stämpla UpdatedAt");
    }

    // ---------------------------------------------------------------
    // Rename — blank clears the label
    // ---------------------------------------------------------------

    [Fact]
    public void Rename_OnActiveCriterion_SetsTheLabelAndStampsUpdatedAt()
    {
        var criterion = CreateValid("Gammalt namn");
        var later = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(2));

        var result = criterion.Rename("  Nytt namn  ", later);

        result.IsSuccess.ShouldBeTrue();
        criterion.Label.ShouldBe("Nytt namn");
        criterion.UpdatedAt.ShouldBe(later.UtcNow);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_WithBlank_ClearsTheLabel(string? blank)
    {
        var criterion = CreateValid("Namngiven");

        var result = criterion.Rename(blank, Clock);

        result.IsSuccess.ShouldBeTrue();
        criterion.Label.ShouldBeNull("tomt namn = namnlös bevakning, inte ett fel");
    }

    [Fact]
    public void Rename_WithTooLongLabel_Fails_AndKeepsTheOldLabel()
    {
        var criterion = CreateValid("Kort namn");

        var result = criterion.Rename(
            new string('a', CompanyWatchCriterion.LabelMaxLength + 1),
            FakeDateTimeProvider.At(Clock.UtcNow.AddDays(2)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriterion.LabelTooLong");
        criterion.Label.ShouldBe("Kort namn");
        criterion.UpdatedAt.ShouldBe(Clock.UtcNow, "en avvisad ändring får inte stämpla UpdatedAt");
    }

    // ---------------------------------------------------------------
    // The cross-instance cap lives here as a CONSTANT, enforced in Application (PR-3)
    // ---------------------------------------------------------------

    [Fact]
    public void MaxPerUser_IsTwenty()
    {
        // Value pin (the ScbCompanyRegisterStore timeout-constant idiom): the number bounds the
        // browse — and, once RF-9 unfreezes, a notification scan — over ~1.17M register rows per
        // criterion. Domain OWNS the rule (CLAUDE.md §5: no magic numbers); a single aggregate
        // cannot see across instances, so the create-handler ENFORCES it (PR-3, the
        // RecentJobSearch.MaxPerSeeker precedent). Changing the number must be a conscious edit.
        CompanyWatchCriterion.MaxPerUser.ShouldBe(20);
    }
}
