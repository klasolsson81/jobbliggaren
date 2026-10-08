namespace Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;

public sealed record AccountDeletionTiming(
    DateTimeOffset DeletedAt, DateTimeOffset EligibleAt, DateTimeOffset ScheduledRunAt)
{
    public static AccountDeletionTiming From(DateTimeOffset deletedAt)
    {
        var eligibleAt = AccountRestoreWindow.EligibleAt(deletedAt);
        return new(deletedAt, eligibleAt, AccountRestoreWindow.FirstScheduledRunAfter(eligibleAt));
    }
}
