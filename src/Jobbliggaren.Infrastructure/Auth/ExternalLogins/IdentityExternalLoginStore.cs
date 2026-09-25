using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.Auth.ExternalLogins;

/// <summary>
/// External logins in Identity's own <c>AspNetUserLogins</c> (ADR 0142 D8, ADR 0017 point 7 as amended): the
/// provider's key and the provider's identifier for the person, keyed together, cascading with the account. No
/// display name is stored: the column would hold nothing the login needs.
/// </summary>
internal sealed class IdentityExternalLoginStore(
    UserManager<ApplicationUser> userManager,
    IDbExceptionInspector dbExceptionInspector) : IExternalLoginLookup, IExternalLoginWriter
{
    public async Task<Guid?> FindUserIdAsync(
        ExternalProviderKey provider, ExternalSubject subject, CancellationToken ct) =>
        (await userManager.FindByLoginAsync(provider.Value, subject.Reveal()))?.Id;

    public async Task<ExternalLinkResult> LinkAsync(
        Guid userId, ExternalProviderKey provider, ExternalSubject subject, CancellationToken ct)
    {
        // Read first: Identity answers LoginAlreadyAssociated for the same user too, and the caller must tell a
        // login it already holds from one another account holds.
        if (await ClassifyAsync(userId, provider, subject) is { } existing)
            return existing;

        // The caller resolved this id from the account table a moment ago, so absence is a race with a hard
        // delete, not a state to answer.
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"User {userId} vanished between resolve and external link.");

        IdentityResult result;
        try
        {
            result = await userManager.AddLoginAsync(
                user, new UserLoginInfo(provider.Value, subject.Reveal(), providerDisplayName: null));
        }
        catch (DbUpdateException ex) when (dbExceptionInspector.IsUniqueConstraintViolation(ex))
        {
            // Two links raced past the read; the primary key refused this one. Whoever holds it now decides.
            return await ClassifyAsync(userId, provider, subject) ?? ExternalLinkResult.LinkedToAnotherUser;
        }

        if (result.Succeeded)
            return ExternalLinkResult.Linked;

        // Codes only: an Identity Description can interpolate what it refused.
        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.LoginAlreadyAssociated)))
            return await ClassifyAsync(userId, provider, subject) ?? ExternalLinkResult.LinkedToAnotherUser;

        throw new InvalidOperationException(
            $"External login for user {userId} was not persisted: " + string.Join("; ", result.Errors.Select(e => e.Code)));
    }

    private async Task<ExternalLinkResult?> ClassifyAsync(
        Guid userId, ExternalProviderKey provider, ExternalSubject subject) =>
        await userManager.FindByLoginAsync(provider.Value, subject.Reveal()) switch
        {
            null => null,
            { } holder when holder.Id == userId => ExternalLinkResult.AlreadyLinkedToThisUser,
            _ => ExternalLinkResult.LinkedToAnotherUser,
        };
}
