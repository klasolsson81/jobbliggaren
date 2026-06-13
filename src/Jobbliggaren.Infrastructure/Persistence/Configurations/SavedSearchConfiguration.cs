using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedSearches;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

public sealed class SavedSearchConfiguration : IEntityTypeConfiguration<SavedSearch>
{
    public void Configure(EntityTypeBuilder<SavedSearch> builder)
    {
        builder.ToTable("saved_searches");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => new SavedSearchId(value))
            .ValueGeneratedNever();

        builder.Property(s => s.JobSeekerId)
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .IsRequired();
        // Scope-query :as på (JobSeekerId) i alla handlers — B-tree-index för
        // "mina sparade sökningar" + JobSeeker-scopad lookup.
        builder.HasIndex(s => s.JobSeekerId)
            .HasDatabaseName("ix_saved_searches_job_seeker_id");

        builder.Property(s => s.Name)
            .HasMaxLength(SavedSearch.NameMaxLength)
            .IsRequired();

        // ADR 0039 §16 — criteria jsonb. ADR 0042 Beslut B (CTO Yta A3
        // 2026-05-16): property-level ValueConverter mot jsonb-kolumn
        // (`OwnsOne(...).ToJson()` mappar inte IReadOnlyList<string> stabilt i
        // Npgsql, #3129). Konverter-kontraktet (nycklar, tolerans, fail-loud
        // på legacy-"Ssyk" efter C2-reverse-lookup-migrationen) dokumenteras i
        // SearchCriteriaConverters.cs (SPOT). Comparern bär VO:ts strukturella
        // record-equality (SavedSearch jsonb-dedupe).
        var criteria = builder.Property(s => s.Criteria)
            .HasConversion(SearchCriteriaConversion.Converter)
            .HasColumnType("jsonb")
            .HasColumnName("criteria")
            .IsRequired();
        criteria.Metadata.SetValueComparer(SearchCriteriaConversion.Comparer);

        builder.Property(s => s.NotificationEnabled)
            .IsRequired()
            .HasDefaultValue(false);

        // ADR 0039 Beslut 2 — kolumnen finns (schema-stabil), skrivlogik Fas 5.
        builder.Property(s => s.LastRunAt);

        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();
        builder.Property(s => s.DeletedAt);

        builder.HasQueryFilter(s => s.DeletedAt == null);

        builder.Ignore(s => s.DomainEvents);
    }
}
