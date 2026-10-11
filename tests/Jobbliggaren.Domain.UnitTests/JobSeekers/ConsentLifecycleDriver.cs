using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.UnitTests.JobAds;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

public static class ConsentLifecycleDriver
{
    public enum Lifecycle
    {
        NeverGranted,
        Granted,
        Withdrawn,
        GrantedAgain,
    }

    public static void Apply(Lifecycle lifecycle, Action<bool, IDateTimeProvider> setConsent, int fromHour)
    {
        if (lifecycle == Lifecycle.NeverGranted)
            return;

        setConsent(true, Later(fromHour));
        if (lifecycle == Lifecycle.Granted)
            return;

        setConsent(false, Later(fromHour + 1));
        if (lifecycle == Lifecycle.Withdrawn)
            return;

        setConsent(true, Later(fromHour + 2));
    }

    private static FakeDateTimeProvider Later(int hours) =>
        FakeDateTimeProvider.At(FakeDateTimeProvider.Default.UtcNow.AddHours(hours));
}
