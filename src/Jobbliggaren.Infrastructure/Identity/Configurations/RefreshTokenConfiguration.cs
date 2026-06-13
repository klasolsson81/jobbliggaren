using Jobbliggaren.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Identity.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.UserId).IsRequired();
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(t => t.ExpiresAt).IsRequired();
        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.CreatedByIp).HasMaxLength(45);

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ix_refresh_tokens_token_hash");
        builder.HasIndex(t => t.UserId).HasDatabaseName("ix_refresh_tokens_user_id");
    }
}
