using Jobbliggaren.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF-konfiguration för audit_log-tabellen. Schema: BUILD.md §7.1.
/// Strategi: ADR 0022. Indexering: BUILD.md §7.2 (occurred_at DESC).
/// Partitionering per dag deferras till Fas 4 retention-jobbet.
/// </summary>
public sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log");

        builder.HasKey(a => new { a.Id, a.OccurredAt });
        builder.Property(a => a.Id)
            .HasConversion(
                id => id.Value,
                value => new AuditLogEntryId(value))
            .ValueGeneratedNever();

        // OccurredAt är del av komposit-PK (krav från native partitioning per ADR 0024 D2).
        // ValueGeneratedNever() krävs för konsistens med Id-konfigurationen — utan det
        // emit:ar EF Core 10 PendingModelChangesWarning vid Migrate() trots att model
        // och snapshot är logiskt synced (känd EF-quirk för komposit-PK med blandad
        // value-generation-config).
        builder.Property(a => a.OccurredAt)
            .ValueGeneratedNever()
            .IsRequired();
        builder.Property(a => a.CorrelationId).IsRequired();

        builder.Property(a => a.UserId);              // nullable — system-jobb i Fas 2+ får null
        builder.Property(a => a.ImpersonatedBy);      // nullable — Fas 6

        builder.Property(a => a.EventType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.AggregateType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.AggregateId).IsRequired();

        // IP/UserAgent från IRequestContextProvider — kan vara null vid Worker-jobb
        builder.Property(a => a.IpAddress).HasMaxLength(45);   // IPv6 max
        builder.Property(a => a.UserAgent).HasMaxLength(256);  // matchar AuthAuditLogger-truncation

        // ADR 0035 — payload jsonb-kolumn aktiveras för Fas 2 system-events.
        // ADR 0022 reserverade kolumnen för Fas 4 (command-audit-payload med
        // PII-saner-krav); system-event-payload (counts + tidsstämplar) har
        // ingen PII och kan aktiveras tidigare. Nullable — command-audit-rader
        // skriver fortfarande null tills Fas 4-spec landar.
        builder.Property(a => a.Payload).HasColumnType("jsonb");

        // BUILD.md §7.2: audit_log (occurred_at DESC) — senaste först för admin-vy och retention
        builder.HasIndex(a => a.OccurredAt)
            .IsDescending()
            .HasDatabaseName("ix_audit_log_occurred_at");

        // Inga FK mot users/job_seekers — audit är write-only och får inte
        // hindras av FK-cascades vid soft-delete (ADR 0022)
    }
}
