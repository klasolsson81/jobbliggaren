using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedSearches;
using Jobbliggaren.Domain.SavedSearches.Events;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.SavedSearches;

// AR — Create/Rename/UpdateCriteria/SetNotification/SoftDelete. Skyddar
// invarianter i fabrik + metoder (CLAUDE.md §2.2). Refererar JobSeeker
// endast via strongly-typed ID.
public class SavedSearchTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId ValidJobSeekerId = new(Guid.NewGuid());
    private const string ValidName = "Backend i Stockholm";

    private static SearchCriteria ValidCriteria() =>
        SearchCriteria.Create(
            occupationGroup: ["grp_12345"], municipality: ["sthlm_kn"],
            region: ["stockholm"], employmentType: null, worktimeExtent: null,
            employer: null,
            remote: false,
            q: "backend",
            sortBy: JobAdSortBy.PublishedAtDesc).Value;

    private static SavedSearch CreateValid() =>
        SavedSearch.Create(ValidJobSeekerId, ValidName, ValidCriteria(), false, Clock).Value;

    // ---------------------------------------------------------------
    // Create — happy path
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithValidData_ReturnsSuccess()
    {
        var result = SavedSearch.Create(ValidJobSeekerId, ValidName, ValidCriteria(), true, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.JobSeekerId.ShouldBe(ValidJobSeekerId);
        result.Value.Name.ShouldBe(ValidName);
        result.Value.NotificationEnabled.ShouldBeTrue();
        result.Value.CreatedAt.ShouldBe(Clock.UtcNow);
        result.Value.UpdatedAt.ShouldBe(Clock.UtcNow);
        result.Value.DeletedAt.ShouldBeNull();
        result.Value.LastRunAt.ShouldBeNull();
    }

    [Fact]
    public void Create_TrimsName()
    {
        var result = SavedSearch.Create(ValidJobSeekerId, "  Mitt sök  ", ValidCriteria(), false, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Mitt sök");
    }

    [Fact]
    public void Create_RaisesSavedSearchCreatedDomainEvent()
    {
        var result = SavedSearch.Create(ValidJobSeekerId, ValidName, ValidCriteria(), false, Clock);

        var evt = result.Value.DomainEvents.OfType<SavedSearchCreatedDomainEvent>()
            .ShouldHaveSingleItem();
        evt.SavedSearchId.ShouldBe(result.Value.Id);
        evt.JobSeekerId.ShouldBe(ValidJobSeekerId);
        evt.Name.ShouldBe(ValidName);
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    // ---------------------------------------------------------------
    // Create — invariant-brott
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithDefaultJobSeekerId_ReturnsFailure()
    {
        var result = SavedSearch.Create(default, ValidName, ValidCriteria(), false, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.JobSeekerIdRequired");
    }

    [Fact]
    public void Create_WithNullCriteria_ReturnsFailure()
    {
        var result = SavedSearch.Create(ValidJobSeekerId, ValidName, null!, false, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.CriteriaRequired");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyName_ReturnsFailure(string? name)
    {
        var result = SavedSearch.Create(ValidJobSeekerId, name, ValidCriteria(), false, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.NameRequired");
    }

    [Fact]
    public void Create_WithNameTooLong_ReturnsFailure()
    {
        var name = new string('x', SavedSearch.NameMaxLength + 1);
        var result = SavedSearch.Create(ValidJobSeekerId, name, ValidCriteria(), false, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.NameTooLong");
    }

    [Fact]
    public void Create_WithNameAtMaxLength_ReturnsSuccess()
    {
        var name = new string('x', SavedSearch.NameMaxLength);
        var result = SavedSearch.Create(ValidJobSeekerId, name, ValidCriteria(), false, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.Length.ShouldBe(SavedSearch.NameMaxLength);
    }

    // ---------------------------------------------------------------
    // Rename
    // ---------------------------------------------------------------

    [Fact]
    public void Rename_WithValidName_UpdatesNameAndRaisesEvent()
    {
        var savedSearch = CreateValid();
        savedSearch.ClearDomainEvents();
        var later = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));

        var result = savedSearch.Rename("  Nytt namn  ", later);

        result.IsSuccess.ShouldBeTrue();
        savedSearch.Name.ShouldBe("Nytt namn");
        savedSearch.UpdatedAt.ShouldBe(later.UtcNow);
        var evt = savedSearch.DomainEvents.OfType<SavedSearchRenamedDomainEvent>()
            .ShouldHaveSingleItem();
        evt.SavedSearchId.ShouldBe(savedSearch.Id);
        evt.Name.ShouldBe("Nytt namn");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rename_WithEmptyName_ReturnsFailure(string? name)
    {
        var savedSearch = CreateValid();

        var result = savedSearch.Rename(name, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.NameRequired");
        savedSearch.Name.ShouldBe(ValidName); // oförändrat
    }

    [Fact]
    public void Rename_WithNameTooLong_ReturnsFailure()
    {
        var savedSearch = CreateValid();

        var result = savedSearch.Rename(new string('x', SavedSearch.NameMaxLength + 1), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.NameTooLong");
    }

    // ---------------------------------------------------------------
    // UpdateCriteria
    // ---------------------------------------------------------------

    [Fact]
    public void UpdateCriteria_WithValidCriteria_ReplacesCriteriaAndTouchesUpdatedAt()
    {
        var savedSearch = CreateValid();
        var later = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(2));
        var newCriteria = SearchCriteria.Create(
            occupationGroup: ["grp_99999"], municipality: null, region: null,
            employmentType: null, worktimeExtent: null, employer: null, remote: false,
            q: null, sortBy: JobAdSortBy.PublishedAtAsc).Value;

        var result = savedSearch.UpdateCriteria(newCriteria, later);

        result.IsSuccess.ShouldBeTrue();
        savedSearch.Criteria.ShouldBe(newCriteria);
        savedSearch.UpdatedAt.ShouldBe(later.UtcNow);
    }

    [Fact]
    public void UpdateCriteria_WithNull_ReturnsFailure()
    {
        var savedSearch = CreateValid();

        var result = savedSearch.UpdateCriteria(null!, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.CriteriaRequired");
    }

    // ---------------------------------------------------------------
    // SetNotification
    // ---------------------------------------------------------------

    [Fact]
    public void SetNotification_WhenValueChanges_UpdatesFlagAndTouchesUpdatedAt()
    {
        var savedSearch = CreateValid();
        savedSearch.NotificationEnabled.ShouldBeFalse();
        var later = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(3));

        savedSearch.SetNotification(true, later);

        savedSearch.NotificationEnabled.ShouldBeTrue();
        savedSearch.UpdatedAt.ShouldBe(later.UtcNow);
    }

    [Fact]
    public void SetNotification_WhenValueUnchanged_IsNoOp()
    {
        var savedSearch = CreateValid();
        var originalUpdatedAt = savedSearch.UpdatedAt;
        var later = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(4));

        savedSearch.SetNotification(false, later); // redan false

        savedSearch.NotificationEnabled.ShouldBeFalse();
        savedSearch.UpdatedAt.ShouldBe(originalUpdatedAt); // ej rörd
    }

    // ---------------------------------------------------------------
    // SoftDelete — idempotent + event + GDPR
    // ---------------------------------------------------------------

    [Fact]
    public void SoftDelete_SetsDeletedAtAndRaisesEvent()
    {
        var savedSearch = CreateValid();
        savedSearch.ClearDomainEvents();
        var deleteTime = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(1));

        savedSearch.SoftDelete(deleteTime);

        savedSearch.DeletedAt.ShouldBe(deleteTime.UtcNow);
        var evt = savedSearch.DomainEvents.OfType<SavedSearchDeletedDomainEvent>()
            .ShouldHaveSingleItem();
        evt.SavedSearchId.ShouldBe(savedSearch.Id);
        evt.JobSeekerId.ShouldBe(ValidJobSeekerId);
        evt.OccurredAt.ShouldBe(deleteTime.UtcNow);
    }

    [Fact]
    public void SoftDelete_WhenAlreadyDeleted_IsIdempotentNoOp()
    {
        var savedSearch = CreateValid();
        var firstDelete = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(1));
        savedSearch.SoftDelete(firstDelete);
        savedSearch.ClearDomainEvents();

        var secondDelete = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(2));
        savedSearch.SoftDelete(secondDelete);

        // DeletedAt rörs inte vid upprepad delete; inget nytt event.
        savedSearch.DeletedAt.ShouldBe(firstDelete.UtcNow);
        savedSearch.DomainEvents.OfType<SavedSearchDeletedDomainEvent>().ShouldBeEmpty();
    }
}
