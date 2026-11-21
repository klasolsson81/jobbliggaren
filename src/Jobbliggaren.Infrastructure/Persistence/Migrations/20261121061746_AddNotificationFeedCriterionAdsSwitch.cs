using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR 0175 D8 (#1663 PR 3) — the bell's third switch, <c>NotificationFeedCriterionAdsOffAt</c>, lives in the
    /// <c>job_seekers.preferences</c> jsonb, so this migration moves only the model snapshot and carries no DDL (the form
    /// of <c>AddNotificationMailPauses</c>). A row written before it has no such key, which reads as on. The bell's two
    /// flags on <c>company_watch_criterion_ad_hits</c> came with that table in <c>AddCompanyWatchCriterionAdHits</c>.
    /// Klas's migration GO of 2026-11-20 names exactly this; it was recreated with <c>dotnet ef</c> on top of
    /// <c>AddRecentJobSearchQuickLinkSlot</c> (#2272), which held the migration token first.
    /// </summary>
    public partial class AddNotificationFeedCriterionAdsSwitch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
