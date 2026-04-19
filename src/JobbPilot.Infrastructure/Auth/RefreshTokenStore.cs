using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using JobbPilot.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Infrastructure.Auth;

public sealed class RefreshTokenStore(
    AppIdentityDbContext db,
    IDateTimeProvider clock)
    : IRefreshTokenStore
{
    public async Task StoreAsync(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        string? createdByIp,
        CancellationToken ct)
    {
        var token = RefreshToken.Create(userId, tokenHash, expiresAt, clock.UtcNow, createdByIp);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);
    }

    public async Task<StoredRefreshToken?> FindActiveByHashAsync(string tokenHash, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var token = await db.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (token is null || !token.IsActive(now))
            return null;

        return new StoredRefreshToken(token.Id, token.UserId, token.TokenHash, token.ExpiresAt, true);
    }

    public async Task RevokeAsync(Guid tokenId, Guid? replacedByTokenId, CancellationToken ct)
    {
        var token = await db.RefreshTokens.FindAsync([tokenId], ct);
        if (token is null) return;

        token.Revoke(clock.UtcNow, replacedByTokenId);
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);

        foreach (var token in tokens)
            token.Revoke(now);

        await db.SaveChangesAsync(ct);
    }
}
