using JobbPilot.Domain.Applications;
using JobbPilot.Domain.Applications.Events;
using JobbPilot.Domain.UnitTests.JobAds;
using Shouldly;

namespace JobbPilot.Domain.UnitTests.Applications;

/// <summary>
/// ApplicationNote.Create är internal — testas via Application.AddNote.
/// </summary>
public class ApplicationNoteTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly FakeDateTimeProvider LaterClock =
        FakeDateTimeProvider.At(FakeDateTimeProvider.Default.UtcNow.AddHours(1));

    private static Application CreateActiveApplication()
    {
        var jobSeekerId = new JobbPilot.Domain.JobSeekers.JobSeekerId(Guid.NewGuid());
        return Application.Create(jobSeekerId, null, null, Clock).Value;
    }

    // ---------------------------------------------------------------
    // AddNote — lyckade fall
    // ---------------------------------------------------------------

    [Fact]
    public void AddNote_WithValidContent_ReturnsSuccess()
    {
        var application = CreateActiveApplication();

        var result = application.AddNote("En bra notering.", Clock);

        result.IsSuccess.ShouldBeTrue();
        application.Notes.Count.ShouldBe(1);
    }

    [Fact]
    public void AddNote_StoresContentCorrectly()
    {
        var application = CreateActiveApplication();

        application.AddNote("Viktig information", Clock);

        application.Notes[0].Content.ShouldBe("Viktig information");
    }

    [Fact]
    public void AddNote_TrimsContent()
    {
        var application = CreateActiveApplication();

        application.AddNote("  Trimmat innehåll  ", Clock);

        application.Notes[0].Content.ShouldBe("Trimmat innehåll");
    }

    [Fact]
    public void AddNote_SetsCreatedAt()
    {
        var application = CreateActiveApplication();

        application.AddNote("Test", LaterClock);

        application.Notes[0].CreatedAt.ShouldBe(LaterClock.UtcNow);
    }

    [Fact]
    public void AddNote_MultipleNotes_AllAreStored()
    {
        var application = CreateActiveApplication();

        application.AddNote("Notering ett", Clock);
        application.AddNote("Notering två", Clock);

        application.Notes.Count.ShouldBe(2);
    }

    [Fact]
    public void AddNote_WithContentAtMaxLength_ReturnsSuccess()
    {
        var application = CreateActiveApplication();
        var maxContent = new string('A', 5000);

        var result = application.AddNote(maxContent, Clock);

        result.IsSuccess.ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // AddNote — valideringsfel
    // ---------------------------------------------------------------

    [Fact]
    public void AddNote_WithNullContent_ReturnsFailure()
    {
        var application = CreateActiveApplication();

        var result = application.AddNote(null, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ApplicationNote.ContentRequired");
        application.Notes.ShouldBeEmpty();
    }

    [Fact]
    public void AddNote_WithEmptyContent_ReturnsFailure()
    {
        var application = CreateActiveApplication();

        var result = application.AddNote("", Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ApplicationNote.ContentRequired");
    }

    [Fact]
    public void AddNote_WithWhitespaceOnlyContent_ReturnsFailure()
    {
        var application = CreateActiveApplication();

        var result = application.AddNote("   ", Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ApplicationNote.ContentRequired");
    }

    [Fact]
    public void AddNote_WithContentExceedingMaxLength_ReturnsFailure()
    {
        var application = CreateActiveApplication();
        var tooLong = new string('A', 5001);

        var result = application.AddNote(tooLong, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ApplicationNote.ContentTooLong");
        application.Notes.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // AddNote — domain event
    // ---------------------------------------------------------------

    [Fact]
    public void AddNote_WhenValid_RaisesApplicationNotedDomainEvent()
    {
        var application = CreateActiveApplication();
        application.ClearDomainEvents();

        application.AddNote("En notering", Clock);

        var evt = application.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ApplicationNotedDomainEvent>();
        evt.ApplicationId.ShouldBe(application.Id);
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void AddNote_WhenValid_EventReferencesCorrectNoteId()
    {
        var application = CreateActiveApplication();
        application.ClearDomainEvents();

        application.AddNote("En notering", Clock);

        var evt = application.DomainEvents.Single().ShouldBeOfType<ApplicationNotedDomainEvent>();
        evt.NoteId.ShouldBe(application.Notes[0].Id);
    }

    // ---------------------------------------------------------------
    // SoftDelete (via Application.SoftDelete-kaskad)
    // ---------------------------------------------------------------

    [Fact]
    public void SoftDelete_ViaApplicationCascade_SetsDeletedAtOnNote()
    {
        var application = CreateActiveApplication();
        application.AddNote("En notering", Clock);

        application.SoftDelete(LaterClock);

        application.Notes[0].DeletedAt.ShouldBe(LaterClock.UtcNow);
    }

    [Fact]
    public void SoftDelete_WhenNoNotes_DoesNotThrow()
    {
        var application = CreateActiveApplication();

        var act = () => application.SoftDelete(Clock);

        act.ShouldNotThrow();
    }
}
