namespace Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;

/// <summary>The earliest date an account in its restore window can be removed.</summary>
public static class AccountRestoreWindow
{
    public static DateTimeOffset EligibleAt(DateTimeOffset deletedAt) =>
        deletedAt.AddDays(HardDeleteAccountsJob.RestoreWindowDays);

    public static DateTimeOffset FirstScheduledRunAfter(DateTimeOffset eligibleAt)
    {
        var run = new DateTimeOffset(eligibleAt.UtcDateTime.Date.AddHours(4), TimeSpan.Zero);
        return run > eligibleAt ? run : run.AddDays(1);
    }

    // The hard-delete job's cutoff is the restore window before "now", so a row soft-deleted at T is removed
    // at the first run after T + window. The UTC date of T + window is on or before that run, which is what
    // "tidigast" promises.
    public static DateOnly PermanentDeletionEarliest(DateTimeOffset deletedAt) =>
        DateOnly.FromDateTime(deletedAt.AddDays(HardDeleteAccountsJob.RestoreWindowDays).UtcDateTime);
}
