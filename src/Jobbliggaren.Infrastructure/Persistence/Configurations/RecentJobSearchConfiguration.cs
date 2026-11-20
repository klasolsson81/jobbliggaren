using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.RecentJobSearches;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// ADR 0060 — EF Core-konfiguration för RecentJobSearches (auto-capture-domän).
/// Postgres text[]-mapping (Npgsql 10 auto-mappar List&lt;string&gt;) via shadow-
/// backing-fields, paritet med <see cref="ResumeConfiguration"/>. UNIQUE-index
/// på (job_seeker_id, filter_hash) är hard-invariant — Capturer-impl tappas
/// alltid till ON CONFLICT-fall (try/catch DbUpdateException).
/// </summary>
public sealed class RecentJobSearchConfiguration : IEntityTypeConfiguration<RecentJobSearch>
{
    public void Configure(EntityTypeBuilder<RecentJobSearch> builder)
    {
        // ADR 0188 (#2265) — the quick-link slot is bounded by the store, not only by the handler's check: the CHECK
        // admits 1 to QuickLinkCap.MaxTotal, and the partial unique index below admits each slot once per job seeker,
        // so no race marks more searches than there are slots (security-auditor's #2265 plan-read Major 1).
        builder.ToTable("recent_job_searches", t => t.HasCheckConstraint(
            "ck_recent_job_searches_quick_link_slot",
            $"\"quick_link_slot\" BETWEEN 1 AND {QuickLinkCap.MaxTotal}"));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new RecentJobSearchId(value))
            .ValueGeneratedNever();

        builder.Property(r => r.JobSeekerId)
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .HasColumnName("job_seeker_id")
            .IsRequired();

        builder.Property(r => r.FilterHash)
            .HasMaxLength(64)
            .HasColumnName("filter_hash")
            .IsRequired();

        // UNIQUE(job_seeker_id, filter_hash) — uniqueness-invarianten för
        // Capture vs. Bump. INSERT vs. UPDATE-bump-distinktion bygger på den.
        builder.HasIndex(r => new { r.JobSeekerId, r.FilterHash })
            .IsUnique()
            .HasDatabaseName("ux_recent_job_searches_seeker_hash");

        // Sekundär-index för list-query (ORDER BY last_viewed_at DESC scoped på seeker).
        builder.HasIndex(r => new { r.JobSeekerId, r.LastViewedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_recent_job_searches_seeker_viewed_at");

        builder.Property(r => r.Q)
            .HasMaxLength(100)
            .HasColumnName("q");

        // text[]-kolumner via shadow backing-fields. RecentJobSearch exponerar
        // IReadOnlyList<string> OccupationGroup/Municipality/Region som
        // AsReadOnly-wrapper över privat List<string>. EF mappar mot fältet
        // via shadow-property + field-access.
        var stringListComparer = new ValueComparer<List<string>>(
            (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>(), StringComparer.Ordinal),
            v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s)),
            v => v.ToList());

        // ADR 0067 Fas C2 (CTO-dom (d)): occupation_group_list + municipality_list
        // ersätter ssyk_list (occupation-name-dimensionen utgick; befintliga
        // rader raderades i C2-migrationen).
        var occupationGroup = builder.Property<List<string>>("_occupationGroup")
            .HasField("_occupationGroup")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("occupation_group_list")
            .HasColumnType("text[]")
            .IsRequired();
        occupationGroup.Metadata.SetValueComparer(stringListComparer);

        // #2199 PR 2 (ADR 0067 Amendment 2026-10-25): occupation_name_list, the picked
        // occupation-names. Additive migration on a live cache table: NOT NULL default '{}', so
        // existing rows get an empty list, and FilterHash leaves an empty list out, so their hash
        // is unchanged. Same shadow-backing-field pattern as the lists around it.
        var occupationName = builder.Property<List<string>>("_occupationName")
            .HasField("_occupationName")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("occupation_name_list")
            .HasColumnType("text[]")
            .IsRequired();
        occupationName.Metadata.SetValueComparer(stringListComparer);

        var municipality = builder.Property<List<string>>("_municipality")
            .HasField("_municipality")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("municipality_list")
            .HasColumnType("text[]")
            .IsRequired();
        municipality.Metadata.SetValueComparer(stringListComparer);

        var region = builder.Property<List<string>>("_region")
            .HasField("_region")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("region_list")
            .HasColumnType("text[]")
            .IsRequired();
        region.Metadata.SetValueComparer(stringListComparer);

        // ADR 0067 Beslut 6 (Fas B2, 2026-06-12): employment_type_list +
        // worktime_extent_list (Klass 2). Additiv migration på en levande
        // cache-tabell — nya kolumner NOT NULL default '{}' (befintliga rader
        // får tom lista; FilterHash-format-bump → benign dubblett, cap-20
        // självläker). Samma shadow-backing-field-mönster som ovan.
        var employmentType = builder.Property<List<string>>("_employmentType")
            .HasField("_employmentType")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("employment_type_list")
            .HasColumnType("text[]")
            .IsRequired();
        employmentType.Metadata.SetValueComparer(stringListComparer);

        var worktimeExtent = builder.Property<List<string>>("_worktimeExtent")
            .HasField("_worktimeExtent")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("worktime_extent_list")
            .HasColumnType("text[]")
            .IsRequired();
        worktimeExtent.Metadata.SetValueComparer(stringListComparer);

        // #311 PR-2b C1 (ADR 0087 D6): employer_list (org.nr). Additiv migration på en levande
        // cache-tabell — ny kolumn NOT NULL default '{}' (befintliga rader får tom lista;
        // FilterHash-format-bump → benign dubblett, cap-20-eviction självläker). Samma shadow-
        // backing-field-mönster som Klass 2.
        var employer = builder.Property<List<string>>("_employer")
            .HasField("_employer")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("employer_list")
            .HasColumnType("text[]")
            .IsRequired();
        employer.Metadata.SetValueComparer(stringListComparer);

        // #551 PR-D (ADR 0087 D6-paritet): remote (distans, bool). SKALÄR kolumn — mappas nativt
        // med builder.Property (INGEN shadow-backing-field/ValueComparer/Ignore-dans som list-dims;
        // en bool spåras nativt). Additiv migration på levande cache-tabell: NOT NULL DEFAULT false
        // (befintliga rader får false; FilterHash-format-bump → benign dubblett, cap-20 självläker).
        builder.Property(r => r.Remote)
            .HasColumnName("remote")
            .HasDefaultValue(false)
            .IsRequired();

        // Public IReadOnlyList<string>-getters är beräknade wrappers — EF
        // ska inte försöka mappa dem (skulle duplicera shadow-kolumnerna).
        builder.Ignore(r => r.OccupationGroup);
        builder.Ignore(r => r.OccupationName);
        builder.Ignore(r => r.Municipality);
        builder.Ignore(r => r.Region);
        builder.Ignore(r => r.EmploymentType);
        builder.Ignore(r => r.WorktimeExtent);
        builder.Ignore(r => r.Employer);

        builder.Property(r => r.SortBy)
            .HasConversion<int>()
            .HasColumnName("sort_by")
            .IsRequired();

        builder.Property(r => r.LastViewedAt)
            .HasColumnName("last_viewed_at")
            .IsRequired();

        builder.Property(r => r.LastSeenCount)
            .HasColumnName("last_seen_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(r => r.QuickLinkSlot)
            .HasColumnName("quick_link_slot")
            .HasColumnType("smallint");

        builder.HasIndex(r => new { r.JobSeekerId, r.QuickLinkSlot })
            .IsUnique()
            .HasFilter("\"quick_link_slot\" IS NOT NULL")
            .HasDatabaseName("ux_recent_job_searches_seeker_quick_link_slot");

        builder.Ignore(r => r.DomainEvents);
    }
}
