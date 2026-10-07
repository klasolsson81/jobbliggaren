using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

[Collection("Api")]
public sealed class AccountRegistrationAtomicityTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OpenAsync_ShouldCommitIdentityProfileAndAudit_WhenRegistrationSucceeds()
    {
        var email = $"atomic-registration-{Guid.NewGuid():N}@example.se";
        await using (var write = factory.Services.CreateAsyncScope())
            (await write.ServiceProvider.GetRequiredService<AccountRegistrar>().OpenAsync(email, Ct)).IsSuccess.ShouldBeTrue();

        await using var read = factory.Services.CreateAsyncScope();
        var user = await read.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Users.AsNoTracking()
            .Where(row => row.Email == email).Select(row => new { row.Id, row.EmailConfirmed, row.PasswordHash })
            .SingleAsync(Ct);
        user.EmailConfirmed.ShouldBeTrue();
        user.PasswordHash.ShouldBeNull();
        var app = read.ServiceProvider.GetRequiredService<AppDbContext>();
        (await app.JobSeekers.AnyAsync(row => row.UserId == user.Id, Ct)).ShouldBeTrue();
        (await app.AuditLogEntries.CountAsync(row => row.AggregateId == user.Id
            && row.EventType == AccountRegistrar.AccountCreatedAuditEventType, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task OpenAsync_ShouldLeaveNoIdentityOrProfile_WhenAuditSaveFails()
    {
        var email = $"atomic-rollback-{Guid.NewGuid():N}@example.se";
        await using (var write = factory.Services.CreateAsyncScope())
        using (factory.AuditRowSaveFailure.FailingForNewRegistration())
            await Should.ThrowAsync<DbUpdateException>(() => write.ServiceProvider
                .GetRequiredService<AccountRegistrar>().OpenAsync(email, Ct));

        await using var read = factory.Services.CreateAsyncScope();
        var identity = read.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
        (await identity.Users.AnyAsync(row => row.Email == email, Ct)).ShouldBeFalse();
        var failedUserId = factory.AuditRowSaveFailure.LastFailedUserId.ShouldNotBeNull();
        var app = read.ServiceProvider.GetRequiredService<AppDbContext>();
        (await app.JobSeekers.IgnoreQueryFilters().AnyAsync(row => row.UserId == failedUserId, Ct)).ShouldBeFalse();
        (await app.AuditLogEntries.AnyAsync(row => row.AggregateId == failedUserId, Ct)).ShouldBeFalse();
    }
}
