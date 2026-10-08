namespace Jobbliggaren.Application.Auth.Commands.DeleteAccount;

public sealed record AccountDeletionScheduled(
    Guid UserId,
    Guid ProfileId,
    DateTimeOffset DeletedAt,
    DateTimeOffset EligibleAt,
    DateTimeOffset ScheduledRunAt,
    bool IsSuspended,
    long AccessRevision);
