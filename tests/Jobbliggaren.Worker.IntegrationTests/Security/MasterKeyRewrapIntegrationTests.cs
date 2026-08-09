using System.Security.Cryptography;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Security;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Security;

/// <summary>
/// #198 gate M-3 — the offline master-key re-wrap, against real Postgres (Testcontainers).
///
/// <para>
/// <b>Test premise (CLAUDE.md §5 <c>Tests:</c>).</b> Every "row wrapped under the retiring key"
/// these cases assert against is produced by <b>calling the production writer</b> —
/// <c>IUserDataKeyStore.GetOrCreateDataKeyAsync</c>, which goes through
/// <see cref="LocalDataKeyProvider"/> exactly as the running app does. No wrapped-DEK bytes are
/// hand-built anywhere here, so the state under test is one <c>src/</c> genuinely produces.
/// </para>
///
/// <para>
/// <b>The field-data case is the one that matters.</b> A re-wrap that generated a FRESH DEK
/// instead of re-wrapping the existing one passes every "the row unwraps" assertion and destroys
/// all field data for that owner. Only comparing DEK bytes across the rotation catches it, which
/// is why <c>Rewrap_PreservesTheDekBytes_SoFieldCiphertextStillDecrypts</c> exists and why the
/// production code round-trips before it writes.
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class MasterKeyRewrapIntegrationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    // The fixture's own master key is the RETIRING one, because the rows these tests rotate are
    // created by the running host through the ordinary write path.
    private static string RetiringKeyBase64 => WorkerTestFixture.TestMasterKeyBase64;
    private const string RetiringKeyId = "local-v1";
    private const string IncomingKeyId = "local-v2";

    private static string IncomingKeyBase64 =>
        Convert.ToBase64String([.. Enumerable.Range(200, 32).Select(i => (byte)i)]);

    private static LocalDataKeyProvider Provider(string masterKeyBase64, string keyId) =>
        new(
            Options.Create(new FieldEncryptionOptions
            {
                Provider = "Local",
                LocalMasterKeyBase64 = masterKeyBase64,
                LocalMasterKeyId = keyId,
            }),
            NullLogger<LocalDataKeyProvider>.Instance);

    private static MasterKeyRewrapper Rewrapper(
        string retiring = null!, string incoming = null!) =>
        new(
            Provider(retiring ?? RetiringKeyBase64, RetiringKeyId),
            Provider(incoming ?? IncomingKeyBase64, IncomingKeyId),
            RetiringKeyId,
            IncomingKeyId);

    /// <summary>
    /// Creates an owner AND its DEK row through the production write path, returning the
    /// plaintext DEK bytes so a test can prove they survive the rotation.
    ///
    /// <para>
    /// Clears <c>user_data_keys</c> first, and that is required rather than tidy: the re-wrap
    /// operates on the WHOLE table by design, so rows left by a sibling case — including this
    /// class's own foreign-identity case — would be inside the next case's scope. The Worker
    /// collection is serial and shares one database, so isolation has to be explicit here.
    /// </para>
    /// </summary>
    private async Task<(JobSeekerId Owner, byte[] Dek)> SeedOwnerWithDekAsync(CancellationToken ct)
    {
        using var reset = _fixture.Services.CreateScope();
        await reset.ServiceProvider.GetRequiredService<AppDbContext>()
            .Set<UserDataKey>().ExecuteDeleteAsync(ct);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var seeker = JobSeeker.Register(Guid.NewGuid(), "Rewrap Test", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        var store = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
        var dek = await store.GetOrCreateDataKeyAsync(seeker.Id, ct);
        return (seeker.Id, (byte[])dek.Clone());
    }

    private static AppDbContext NewContext(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<AppDbContext>();

    [Fact]
    public async Task Rewrap_PreservesTheDekBytes_SoFieldCiphertextStillDecrypts()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, dekBefore) = await SeedOwnerWithDekAsync(ct);

        using var scope = _fixture.Services.CreateScope();
        var db = NewContext(scope);

        await Rewrapper().RewrapAllAsync(db, ct);

        // THE ASSERTION THAT CATCHES A FRESH-DEK BUG. Field ciphertext is encrypted under the
        // DEK, not under the master key, so the only thing that keeps existing data readable is
        // that these bytes are unchanged. A re-wrap that minted a new DEK would satisfy every
        // "it unwraps cleanly" check and silently destroy the owner's data.
        var row = await db.Set<UserDataKey>().AsNoTracking()
            .SingleAsync(k => k.JobSeekerId == owner, ct);
        var dekAfter = await Provider(IncomingKeyBase64, IncomingKeyId)
            .UnwrapDataKeyAsync(owner, row.WrappedDek, ct);

        dekAfter.ShouldBe(dekBefore);
        row.CmkKeyId.ShouldBe(IncomingKeyId);

        // And the retiring key must no longer open it — otherwise the rotation moved nothing.
        await Should.ThrowAsync<CryptographicException>(() =>
            Provider(RetiringKeyBase64, RetiringKeyId)
                .UnwrapDataKeyAsync(owner, row.WrappedDek, ct));
    }

    [Fact]
    public async Task Rewrap_PreservesDekVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await SeedOwnerWithDekAsync(ct);

        using var scope = _fixture.Services.CreateScope();
        var db = NewContext(scope);

        await Rewrapper().RewrapAllAsync(db, ct);

        // A master-key re-wrap is orthogonal to #501's DEK-version axis. Bumping the version here
        // would trip UserDataKeyStore's single-version guard and make the owner's data
        // unreadable through the ordinary read path.
        var row = await db.Set<UserDataKey>().AsNoTracking()
            .SingleAsync(k => k.JobSeekerId == owner, ct);
        row.DekVersion.ShouldBe(1);
    }

    [Fact]
    public async Task Rewrap_SecondRun_IsANoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOwnerWithDekAsync(ct);

        using var scope = _fixture.Services.CreateScope();
        var db = NewContext(scope);

        var first = await Rewrapper().RewrapAllAsync(db, ct);
        first.Rewrapped.ShouldBeGreaterThan(0);

        // M-3's idempotency proof: a completed rotation leaves nothing selectable, so the second
        // run reports zero re-wrapped and succeeds rather than failing or double-wrapping.
        var second = await Rewrapper().RewrapAllAsync(db, ct);
        second.Rewrapped.ShouldBe(0);
        second.AlreadyCurrent.ShouldBe(first.Rewrapped + first.AlreadyCurrent);
    }

    [Fact]
    public async Task Rewrap_WithTheWrongRetiringKey_FailsAndWritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await SeedOwnerWithDekAsync(ct);

        using var scope = _fixture.Services.CreateScope();
        var db = NewContext(scope);

        var wrongRetiring = Convert.ToBase64String([.. Enumerable.Range(50, 32).Select(i => (byte)i)]);

        // Fail-closed: an unwrap failure must abort the whole run inside the transaction rather
        // than skip the row and report partial success.
        await Should.ThrowAsync<CryptographicException>(() =>
            Rewrapper(retiring: wrongRetiring).RewrapAllAsync(db, ct));

        var row = await db.Set<UserDataKey>().AsNoTracking()
            .SingleAsync(k => k.JobSeekerId == owner, ct);
        row.CmkKeyId.ShouldBe(RetiringKeyId);
    }

    [Fact]
    public async Task Rewrap_SameIdentityOnBothSides_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOwnerWithDekAsync(ct);

        using var scope = _fixture.Services.CreateScope();
        var db = NewContext(scope);

        var sameIdentity = new MasterKeyRewrapper(
            Provider(RetiringKeyBase64, RetiringKeyId),
            Provider(IncomingKeyBase64, RetiringKeyId),
            RetiringKeyId,
            RetiringKeyId);

        // Without a distinguishable marker the operation cannot tell rotated rows from
        // un-rotated ones, which makes the second run destructive instead of idempotent.
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            sameIdentity.RewrapAllAsync(db, ct));
        ex.Message.ShouldContain(RetiringKeyId);
    }

    [Fact]
    public async Task Rewrap_RowWithAForeignKeyIdentity_IsRefusedBeforeAnythingIsWritten()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await SeedOwnerWithDekAsync(ct);

        using var scope = _fixture.Services.CreateScope();
        var db = NewContext(scope);

        // PREMISE (§5 Tests:): the actor that produces a row stamped with a third identity is
        // LocalDataKeyProvider itself, configured with that identity — which is precisely what a
        // rotation to some other key would have done. Stamped here through ExecuteUpdate rather
        // than by hand-building bytes; the wrapped-DEK itself remains one the provider wrote.
        await db.Set<UserDataKey>()
            .Where(k => k.JobSeekerId == owner)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.CmkKeyId, "local-v9"), ct);

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            Rewrapper().RewrapAllAsync(db, ct));
        ex.Message.ShouldContain("local-v9");

        // Nothing written: the guard runs before the transaction opens.
        var row = await db.Set<UserDataKey>().AsNoTracking()
            .SingleAsync(k => k.JobSeekerId == owner, ct);
        row.CmkKeyId.ShouldBe("local-v9");
    }

    [Fact]
    public async Task Rewrap_OnAnEmptyTable_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _fixture.Services.CreateScope();
        var db = NewContext(scope);

        await db.Set<UserDataKey>().ExecuteDeleteAsync(ct);

        // The state the box is in right now (measured 2026-08-09: user_data_keys holds 0 rows),
        // and therefore the state the first real rotation will run against.
        var result = await Rewrapper().RewrapAllAsync(db, ct);

        result.Rewrapped.ShouldBe(0);
        result.Verified.ShouldBe(0);
    }
}
