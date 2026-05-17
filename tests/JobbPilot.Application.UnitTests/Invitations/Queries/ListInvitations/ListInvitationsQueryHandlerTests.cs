using JobbPilot.Application.Invitations.Queries.ListInvitations;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Invitations;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Invitations.Queries.ListInvitations;

/// <summary>
/// Branch-/beteende-täckande tester för <see cref="ListInvitationsQueryHandler"/>
/// (test-coverage-sidospår B1 — handlern var 0% täckt men är wired/live via
/// AdminInvitationsEndpoints, ej död kod).
///
/// Täckta grenar:
///   1. Status = null            → inget filter, ALLA, IssuedAt DESC, DTO-mappning
///   2. Status = "Pending"       → filtrerar på exakt status
///   3. Status = "pending"       → TryFromName ignoreCase:true-grenen filtrerar
///   4. Status = "Bogus"         → TryFromName false → faller igenom, ALLA
///   5. Status = "  " (blanksteg) → IsNullOrWhiteSpace true → inget filter, ALLA
///   6. Tomt repository           → tom lista, ingen exception
///   7. Ordning                   → strikt fallande på IssuedAt
/// </summary>
public class ListInvitationsQueryHandlerTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    /// <summary>
    /// Skapar en Pending-invitation med given e-post och IssuedAt styrt av en
    /// fix-klocka, samt vald origin. Invitation.Issue sätter IssuedAt = clock.UtcNow.
    /// </summary>
    private static Invitation Pending(
        string email, DateTimeOffset issuedAt, InvitationOrigin? origin = null)
    {
        var clock = new FakeDateTimeProvider(issuedAt);
        var result = Invitation.Issue(
            email,
            origin ?? InvitationOrigin.DirectInvite,
            "hash-" + Guid.NewGuid().ToString("N"),
            TimeSpan.FromDays(7),
            AdminId,
            clock);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private static Invitation Redeemed(string email, DateTimeOffset issuedAt)
    {
        var inv = Pending(email, issuedAt);
        // Lös in innan ExpiresAt (issuedAt + 7d) — Redeem kräver now < ExpiresAt.
        var redeemClock = new FakeDateTimeProvider(issuedAt.AddDays(1));
        inv.Redeem(Guid.NewGuid(), redeemClock).IsSuccess.ShouldBeTrue();
        return inv;
    }

    private static Invitation Revoked(string email, DateTimeOffset issuedAt)
    {
        var inv = Pending(email, issuedAt);
        var revokeClock = new FakeDateTimeProvider(issuedAt.AddDays(1));
        inv.Revoke(AdminId, revokeClock).IsSuccess.ShouldBeTrue();
        return inv;
    }

    private static Invitation Expired(string email, DateTimeOffset issuedAt)
    {
        var inv = Pending(email, issuedAt);
        // MarkExpired kräver clock.UtcNow >= ExpiresAt (issuedAt + 7d).
        var expiredClock = new FakeDateTimeProvider(issuedAt.AddDays(8));
        inv.MarkExpired(expiredClock).IsSuccess.ShouldBeTrue();
        return inv;
    }

    private static readonly DateTimeOffset T0 =
        new(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListInvitationsQueryHandler_WithNullStatus_ReturnsAllOrderedByIssuedAtDescAndMapsDto()
    {
        var db = TestAppDbContextFactory.Create();
        var oldest = Pending("oldest@example.com", T0, InvitationOrigin.DirectInvite);
        var newest = Redeemed("newest@example.com", T0.AddHours(2));
        db.Invitations.AddRange(oldest, newest);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListInvitationsQueryHandler(db);

        var result = await handler.Handle(
            new ListInvitationsQuery(null), CancellationToken.None);

        result.Count.ShouldBe(2);
        // Ordning: nyaste först (IssuedAt DESC).
        result[0].Email.ShouldBe("newest@example.com");
        result[1].Email.ShouldBe("oldest@example.com");

        // DTO-mappning (InvitationListItemDto.From) — verifiera samtliga fält
        // för den inlösta posten, inkl. Origin.Name + Status.Name.
        var newestDto = result[0];
        newestDto.InvitationId.ShouldBe(newest.Id.Value);
        newestDto.Email.ShouldBe("newest@example.com");
        newestDto.Origin.ShouldBe(InvitationOrigin.DirectInvite.Name);
        newestDto.Status.ShouldBe(InvitationStatus.Redeemed.Name);
        newestDto.IssuedAt.ShouldBe(newest.IssuedAt);
        newestDto.ExpiresAt.ShouldBe(newest.ExpiresAt);
        newestDto.RedeemedAt.ShouldBe(newest.RedeemedAt);
        newestDto.RevokedAt.ShouldBeNull();

        // Pending-posten: RedeemedAt/RevokedAt ska vara null.
        var oldestDto = result[1];
        oldestDto.Status.ShouldBe(InvitationStatus.Pending.Name);
        oldestDto.RedeemedAt.ShouldBeNull();
        oldestDto.RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task ListInvitationsQueryHandler_WithValidStatusName_FiltersToThatStatusOnly()
    {
        var db = TestAppDbContextFactory.Create();
        db.Invitations.AddRange(
            Pending("pending@example.com", T0),
            Redeemed("redeemed@example.com", T0.AddHours(1)),
            Revoked("revoked@example.com", T0.AddHours(2)),
            Expired("expired@example.com", T0.AddHours(3)));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListInvitationsQueryHandler(db);

        var result = await handler.Handle(
            new ListInvitationsQuery("Pending"), CancellationToken.None);

        result.Count.ShouldBe(1);
        result[0].Email.ShouldBe("pending@example.com");
        result[0].Status.ShouldBe(InvitationStatus.Pending.Name);
    }

    [Fact]
    public async Task ListInvitationsQueryHandler_WithLowercaseStatusName_FiltersCaseInsensitively()
    {
        var db = TestAppDbContextFactory.Create();
        db.Invitations.AddRange(
            Pending("pending@example.com", T0),
            Revoked("revoked@example.com", T0.AddHours(1)));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListInvitationsQueryHandler(db);

        // "revoked" gemener → TryFromName(ignoreCase:true)-grenen.
        var result = await handler.Handle(
            new ListInvitationsQuery("revoked"), CancellationToken.None);

        result.Count.ShouldBe(1);
        result[0].Email.ShouldBe("revoked@example.com");
        result[0].Status.ShouldBe(InvitationStatus.Revoked.Name);
    }

    [Fact]
    public async Task ListInvitationsQueryHandler_WithUnknownStatusName_AppliesNoFilterAndReturnsAll()
    {
        var db = TestAppDbContextFactory.Create();
        db.Invitations.AddRange(
            Pending("pending@example.com", T0),
            Redeemed("redeemed@example.com", T0.AddHours(1)));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListInvitationsQueryHandler(db);

        // "Bogus" → TryFromName false → if-villkoret faller → inget filter.
        var result = await handler.Handle(
            new ListInvitationsQuery("Bogus"), CancellationToken.None);

        result.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ListInvitationsQueryHandler_WithWhitespaceStatus_AppliesNoFilterAndReturnsAll()
    {
        var db = TestAppDbContextFactory.Create();
        db.Invitations.AddRange(
            Pending("a@example.com", T0),
            Revoked("b@example.com", T0.AddHours(1)));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListInvitationsQueryHandler(db);

        // "  " → IsNullOrWhiteSpace true → kortsluter → inget filter.
        var result = await handler.Handle(
            new ListInvitationsQuery("  "), CancellationToken.None);

        result.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ListInvitationsQueryHandler_WithEmptyRepository_ReturnsEmptyListWithoutThrowing()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = new ListInvitationsQueryHandler(db);

        var result = await handler.Handle(
            new ListInvitationsQuery(null), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListInvitationsQueryHandler_WithMultipleInvitations_OrdersStrictlyDescendingByIssuedAt()
    {
        var db = TestAppDbContextFactory.Create();
        // Tillsatta i icke-sorterad ordning för att bevisa att handlern sorterar.
        var mid = Pending("mid@example.com", T0.AddHours(5));
        var newest = Pending("newest@example.com", T0.AddHours(9));
        var oldest = Pending("oldest@example.com", T0.AddHours(1));
        db.Invitations.AddRange(mid, newest, oldest);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListInvitationsQueryHandler(db);

        var result = await handler.Handle(
            new ListInvitationsQuery(null), CancellationToken.None);

        result.Count.ShouldBe(3);
        result[0].Email.ShouldBe("newest@example.com");
        result[1].Email.ShouldBe("mid@example.com");
        result[2].Email.ShouldBe("oldest@example.com");
        // Strikt fallande på IssuedAt.
        for (var k = 0; k < result.Count - 1; k++)
        {
            result[k].IssuedAt.ShouldBeGreaterThan(result[k + 1].IssuedAt);
        }
    }
}
