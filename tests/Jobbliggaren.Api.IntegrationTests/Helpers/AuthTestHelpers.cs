using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Api.IntegrationTests.Helpers;

public static class AuthTestHelpers
{
    /// <summary>
    /// Creates the account shape ADR 0142 D10 defines — a passwordless user whose address is
    /// confirmed — with a JobSeeker and a session of <paramref name="lifetime"/>, and returns the raw
    /// session id for an Authorization: Bearer header.
    /// </summary>
    public static async Task<string> RegisterAndGetSessionIdAsync(
        WebApplicationFactory<Program> factory,
        string? email = null,
        SessionLifetime lifetime = SessionLifetime.Persistent,
        CancellationToken ct = default)
    {
        email ??= $"test-{Guid.NewGuid()}@example.se";

        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var created = await services.GetRequiredService<AccountRegistrar>().OpenAsync(email, ct);
        if (created.IsFailure)
            throw new InvalidOperationException($"Bootstrap user creation failed: {created.Error.Code}");

        var user = await services.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email)
            ?? throw new InvalidOperationException("The registrar did not create the account.");
        return await CreateSessionAsync(services, user.Id, lifetime, ct);
    }

    internal static async Task<string> RegisterJobSeekerAndCreateSessionAsync(
        IServiceProvider services,
        Guid userId,
        SessionLifetime lifetime,
        CancellationToken ct)
    {
        await using (var access = await services.GetRequiredService<IAccountAccessCoordinator>()
            .BeginAsync([userId], false, ct))
        {
            var clock = services.GetRequiredService<IDateTimeProvider>();
            var seeker = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(clock), clock);
            if (seeker.IsFailure)
                throw new InvalidOperationException($"Bootstrap JobSeeker.Register failed: {seeker.Error.Code}");

            var db = services.GetRequiredService<IAppDbContext>();
            db.JobSeekers.Add(seeker.Value);
            await db.SaveChangesAsync(ct);
            await access.CommitAsync(ct);
        }

        return await CreateSessionAsync(services, userId, lifetime, ct);
    }

    private static async Task<string> CreateSessionAsync(
        IServiceProvider services, Guid userId, SessionLifetime lifetime, CancellationToken ct)
    {
        var reader = services.GetRequiredService<IAccountAccessReader>();
        var epoch = await reader.ReadEpochAsync(ct);
        var account = await reader.ReadAsync(userId, ct)
            ?? throw new InvalidOperationException("The account is unavailable.");
        var proof = new AccountAccessProof(epoch, userId, account.AccessRevision);
        var session = await services.GetRequiredService<ISessionStore>().CreateAsync(userId, proof, lifetime, ct)
            ?? throw new InvalidOperationException("The account cannot receive a session.");
        return session.Id.Reveal();
    }
}
