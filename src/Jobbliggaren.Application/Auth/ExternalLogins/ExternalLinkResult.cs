namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>What a link attempt found. The expected outcomes are values; only a write that failed otherwise throws.</summary>
public enum ExternalLinkResult
{
    Linked,
    AlreadyLinkedToThisUser,
    LinkedToAnotherUser,
}
