using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Invitations;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.SavedJobAds;
using Jobbliggaren.Domain.SavedSearches;
using Jobbliggaren.Domain.Waitlist;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Application-side abstraction över EF Core DbContext. Exponerar DbSet&lt;T&gt;
/// per aggregate root. Medveten kompromiss per ADR 0009 — repository-pattern
/// ovanpå EF Core är ett anti-pattern. DbSet&lt;T&gt; är ett accepterat bridge-interface.
/// </summary>
public interface IAppDbContext
{
    DbSet<JobAd> JobAds { get; }
    DbSet<JobSeeker> JobSeekers { get; }
    DbSet<DomainApplication> Applications { get; }
    DbSet<Resume> Resumes { get; }
    DbSet<AuditLogEntry> AuditLogEntries { get; }
    DbSet<Invitation> Invitations { get; }
    DbSet<WaitlistEntry> WaitlistEntries { get; }
    DbSet<SavedSearch> SavedSearches { get; }
    DbSet<RecentJobSearch> RecentJobSearches { get; }
    DbSet<SavedJobAd> SavedJobAds { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Detacha en tracked entity från change-tracker. Använd när
    /// <see cref="SaveChangesAsync"/> kastat <c>DbUpdateException</c> men
    /// handler vill fortsätta scope:t med annan entity (t.ex. upsert-retry
    /// efter UNIQUE-violation per ADR 0032 §5). Bryter INTE Clean Arch —
    /// EF-tracking är en infrastructure-concern men port-yta håller
    /// implementationen leverantörs-agnostisk.
    /// </summary>
    void Detach(object entity);
}
