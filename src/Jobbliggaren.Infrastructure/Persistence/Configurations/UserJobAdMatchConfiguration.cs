using System.Text.Json;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Matching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// ADR 0080 Vag 4 — EF Core configuration for <see cref="UserJobAdMatch"/>.
/// <para>
/// <b>Design decisions:</b>
/// <list type="bullet">
/// <item>Strongly-typed <see cref="UserJobAdMatchId"/> converted to uuid (parity
///   with <c>SavedJobAdId</c>, <c>ParsedResumeId</c>, etc.).</item>
/// <item><see cref="JobAdId"/> stored as plain uuid — NO FK to <c>job_ads</c>
///   (ADR 0058/0059: soft-delete isolation, reference by identity, join is
///   handler-managed; parity with <c>ApplicationConfiguration</c>'s optional
///   <c>job_ad_id</c> and <c>SavedJobAdConfiguration</c>'s <c>job_ad_id</c>).</item>
/// <item><see cref="NotifiableMatchGrade"/> and <see cref="NotificationStatus"/>
///   stored as their enum NAMES via <c>HasConversion&lt;string&gt;()</c> (reorder-safe,
///   human-readable; parity with <c>TaxonomyConceptConfiguration.Kind</c> and the
///   <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c> already on both enums).</item>
/// <item><c>matched_skill_concept_ids</c> as jsonb (plaintext, DEK-free per ADR 0079
///   Beslut 1 — concept-ids are non-PII taxonomy references, same as
///   <c>occupation_proposals</c> in <c>ParsedResumeConfiguration</c>). Backed by
///   the private <c>_matchedSkillConceptIds</c> field
///   (<c>UsePropertyAccessMode(PropertyAccessMode.Field)</c>).</item>
/// <item>UNIQUE (user_id, job_ad_id) — the dedup spine (idempotent Worker scan;
///   parity with <c>ux_saved_job_ads_seeker_jobad</c> and
///   <c>ix_job_ads_external_source_external_id</c>).</item>
/// <item>Two btree indexes for the hot read paths: digest pagination
///   (user_id ASC, created_at DESC) and dispatch query
///   (user_id, notification_status).</item>
/// </list>
/// </para>
/// <para>
/// <b>GDPR:</b> <c>matched_skill_concept_ids</c> are non-PII (taxonomy concept-id
/// strings, ADR 0079 Beslut 1). Soft-delete via <c>deleted_at</c> (nullable
/// timestamptz) + global query filter. No DEK column, no encryption surface.
/// </para>
/// </summary>
public sealed class UserJobAdMatchConfiguration : IEntityTypeConfiguration<UserJobAdMatch>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public void Configure(EntityTypeBuilder<UserJobAdMatch> builder)
    {
        builder.ToTable("user_job_ad_matches");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasConversion(id => id.Value, value => new UserJobAdMatchId(value))
            .ValueGeneratedNever();

        builder.Property(m => m.UserId)
            .IsRequired();

        // ADR 0058/0059 — by-identity reference, NO FK to job_ads (parity with
        // SavedJobAdConfiguration.job_ad_id and ApplicationConfiguration.job_ad_id).
        builder.Property(m => m.JobAdId)
            .HasConversion(id => id.Value, value => new JobAdId(value))
            .HasColumnName("job_ad_id")
            .IsRequired();

        // Enum columns — stored by NAME (stable against ordinal reorder; parity with
        // TaxonomyConceptConfiguration.Kind which uses HasConversion<string>()).
        builder.Property(m => m.Grade)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.NotificationStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Private backing field — MatchedSkillConceptIds exposes it as IReadOnlyList<string>.
        // UsePropertyAccessMode(Field) tells EF to read/write _matchedSkillConceptIds directly
        // (parity with ParsedResumeConfiguration's _occupationProposals / _skillProposals).
        var (skillsConverter, skillsComparer) = JsonConverter<List<string>>();
        builder.Property<List<string>>("_matchedSkillConceptIds")
            .HasField("_matchedSkillConceptIds")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(skillsConverter, skillsComparer)
            .HasColumnName("matched_skill_concept_ids")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        // IReadOnlyList<string> property is not mapped (the backing field is the storage);
        // EF must ignore it to avoid duplicate-column errors.
        builder.Ignore(m => m.MatchedSkillConceptIds);

        builder.Property(m => m.CreatedAt).IsRequired();
        builder.Property(m => m.SentAt);
        builder.Property(m => m.DeletedAt);

        // UNIQUE (user_id, job_ad_id) — the dedup spine (an existing row in any non-Pending
        // status is skipped; re-running the Worker scan never re-notifies). The Worker
        // uses ADR 0032 §5 ON CONFLICT pattern for race-safe INSERT (parity with
        // ux_saved_job_ads_seeker_jobad).
        builder.HasIndex(m => new { m.UserId, m.JobAdId })
            .IsUnique()
            .HasDatabaseName("ux_user_job_ad_matches_user_jobad");

        // Digest pagination (ORDER BY created_at DESC scoped on user_id).
        builder.HasIndex(m => new { m.UserId, m.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_user_job_ad_matches_user_created_at");

        // Dispatch query (Worker: WHERE user_id = ? AND notification_status = 'Pending').
        builder.HasIndex(m => new { m.UserId, m.NotificationStatus })
            .HasDatabaseName("ix_user_job_ad_matches_user_status");

        // UserId index for cascade-delete by user (AccountHardDeleter Art.17 sweep).
        builder.HasIndex(m => m.UserId)
            .HasDatabaseName("ix_user_job_ad_matches_user_id");

        // Soft-delete: deleted rows hidden from normal queries (retained for audit).
        builder.HasQueryFilter(m => m.DeletedAt == null);

        builder.Ignore(m => m.DomainEvents);
    }

    // jsonb round-trip via System.Text.Json (parity with ParsedResumeConfiguration).
    private static (ValueConverter<T, string> Converter, ValueComparer<T> Comparer) JsonConverter<T>()
    {
        var converter = new ValueConverter<T, string>(
            value => JsonSerializer.Serialize(value, JsonOptions),
            json => JsonSerializer.Deserialize<T>(json, JsonOptions)!);

        var comparer = new ValueComparer<T>(
            (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
            v => v == null ? 0 : JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!);

        return (converter, comparer);
    }
}
