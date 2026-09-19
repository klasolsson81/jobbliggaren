namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>What a proven login challenge leads to. A closed set: only the variants nested here exist.</summary>
public abstract record LoginOutcome
{
    private LoginOutcome()
    {
    }

    public sealed record SignedIn(string SessionId) : LoginOutcome
    {
        public const string WireName = "signedIn";
    }

    public sealed record PendingDeletion(DateOnly PermanentDeletionEarliest) : LoginOutcome
    {
        public const string WireName = "pendingDeletion";
    }

    public sealed record RegistrationClosed : LoginOutcome
    {
        public const string WireName = "registrationClosed";
    }
}
