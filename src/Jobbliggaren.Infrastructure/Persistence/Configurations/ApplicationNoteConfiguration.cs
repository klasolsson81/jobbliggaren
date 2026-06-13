using Jobbliggaren.Domain.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

public sealed class ApplicationNoteConfiguration : IEntityTypeConfiguration<ApplicationNote>
{
    public void Configure(EntityTypeBuilder<ApplicationNote> builder)
    {
        builder.ToTable("application_notes");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id)
            .HasConversion(id => id.Value, value => new ApplicationNoteId(value))
            .ValueGeneratedNever();

        // TD-13 (ADR 0049 C3): krypteras via interceptor-paret. HasMaxLength
        // borttagen (ciphertext > klartext-cap; TEXT obegränsad). IsRequired
        // behålls — krypterat värde är aldrig null/tomt.
        builder.Property(n => n.Content).IsRequired();

        builder.Property(n => n.CreatedAt).IsRequired();
        builder.Property(n => n.DeletedAt);

        builder.HasQueryFilter(n => n.DeletedAt == null);
    }
}
