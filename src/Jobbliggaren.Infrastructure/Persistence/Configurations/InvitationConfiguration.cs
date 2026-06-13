using Jobbliggaren.Domain.Invitations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasConversion(id => id.Value, value => new InvitationId(value))
            .ValueGeneratedNever();

        builder.Property(i => i.Email)
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(i => i.Origin)
            .HasConversion(
                o => o.Name,
                v => InvitationOrigin.FromName(v))
            .HasMaxLength(30)
            .IsRequired();

        // TokenHash är HMAC-SHA256-hex (64 chars) men kan vara annan opaque hash
        // i framtiden — håll kolumnen rymlig nog. Unique index garanterar att
        // ingen hash-kollision kan ha två pending invitations.
        builder.Property(i => i.TokenHash)
            .HasMaxLength(128)
            .IsRequired();
        builder.HasIndex(i => i.TokenHash).IsUnique();

        builder.Property(i => i.ExpiresAt).IsRequired();

        builder.Property(i => i.Status)
            .HasConversion(
                s => s.Name,
                v => InvitationStatus.FromName(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(i => i.IssuedByAdminId).IsRequired();
        builder.Property(i => i.IssuedAt).IsRequired();
        builder.Property(i => i.RedeemedAt);
        builder.Property(i => i.RedeemedByUserId);
        builder.Property(i => i.RevokedAt);
        builder.Property(i => i.RevokedByAdminId);

        // Admin-list-query: vilka pending invitations finns? Index på status + email
        // täcker både "lista pending" och "kolla om email redan har pending invite".
        builder.HasIndex(i => new { i.Status, i.Email });

        // Optimistic concurrency för single-use redemption: två parallella redeem-
        // anrop kan inte båda lyckas tack vare xmin-token (ApplicationConfiguration-
        // mönster). Skyddar invarianten "redemption är single-use" mot race
        // conditions även om TokenHash-unique index skulle bypass:as.
        builder.Property<uint>("xmin")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(i => i.DomainEvents);
    }
}
