using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

// F4-12 (CTO-frozen) — JobSeeker.UpdateMatchPreferences speglar
// UpdatePreferences exakt: sätter VO:t + bumpar UpdatedAt via injicerad klocka.
// CTO-bunden: INGET domain event (ingen reaktiv konsument). Owner-state ägs av
// aggregatet (handler ansvarar för owner-scope-uppslag).
//
// RÖD tills JobSeeker.UpdateMatchPreferences + MatchPreferences-property
// implementeras (varken metoden eller VO:t finns ännu).
public class JobSeekerMatchPreferencesTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly Guid ValidUserId = Guid.NewGuid();

    private static MatchPreferences SamplePrefs() =>
        MatchPreferences.Create(
            preferredOccupationGroups: ["grp_12345"],
            preferredRegions: ["stockholm_AB"],
            preferredEmploymentTypes: ["et_fast"]).Value;

    [Fact]
    public void UpdateMatchPreferences_SetsThePreferences()
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas Olsson", Clock).Value;
        var prefs = SamplePrefs();

        seeker.UpdateMatchPreferences(prefs, Clock);

        seeker.MatchPreferences.ShouldBe(prefs);
    }

    [Fact]
    public void UpdateMatchPreferences_BumpsUpdatedAt_FromInjectedClock()
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas Olsson", Clock).Value;
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(2));

        seeker.UpdateMatchPreferences(SamplePrefs(), laterClock);

        seeker.UpdatedAt.ShouldBe(laterClock.UtcNow);
    }

    [Fact]
    public void UpdateMatchPreferences_DoesNotRaiseDomainEvent()
    {
        // CTO-bunden: ingen reaktiv konsument → inget event (paritet med
        // UpdatePreferences som heller inte höjer event).
        var seeker = JobSeeker.Register(ValidUserId, "Klas Olsson", Clock).Value;
        seeker.ClearDomainEvents();

        seeker.UpdateMatchPreferences(SamplePrefs(), Clock);

        seeker.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void UpdateMatchPreferences_WithEmptyPreferences_IsValid_AndStored()
    {
        // Tom MatchPreferences är giltig (ingen "minst ett"-invariant) → ska gå
        // att lagra (t.ex. att nollställa angivna preferenser).
        var seeker = JobSeeker.Register(ValidUserId, "Klas Olsson", Clock).Value;
        var empty = MatchPreferences.Empty;

        seeker.UpdateMatchPreferences(empty, Clock);

        seeker.MatchPreferences.ShouldBe(empty);
    }

    [Fact]
    public void UpdateMatchPreferences_Overwrite_ReplacesPreviousPreferences()
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas Olsson", Clock).Value;
        seeker.UpdateMatchPreferences(SamplePrefs(), Clock);

        var newPrefs = MatchPreferences.Create(
            preferredOccupationGroups: ["grp_99999"],
            preferredRegions: null,
            preferredEmploymentTypes: null).Value;
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(3));

        seeker.UpdateMatchPreferences(newPrefs, laterClock);

        seeker.MatchPreferences.ShouldBe(newPrefs);
        seeker.UpdatedAt.ShouldBe(laterClock.UtcNow);
    }
}
