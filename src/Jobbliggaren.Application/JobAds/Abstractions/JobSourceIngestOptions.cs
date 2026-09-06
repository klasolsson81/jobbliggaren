namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Master switch for external job-ad ingestion. Application owns the contract; Infrastructure
/// contributes the source value through the same typed-options aliasing as
/// <see cref="JobSourceRetentionOptions"/>, so the Application jobs need no Infrastructure type.
///
/// <para>
/// <b>Note:</b> this type binds in <c>AddJobSources</c> against
/// <c>JobTechOptions.SectionName</c> ("JobTech") — not a section of its own. It exposes no
/// <c>SectionName</c> constant, because one would be misleading.
/// </para>
///
/// <para>
/// <b>Why the switch exists.</b> Platsbanken ingestion writes recruiter contact records:
/// <c>PlatsbankenJobSource</c> maps <c>application_contacts</c> off the wire into Domain
/// <c>AdContact</c>, and <c>UpsertExternalJobAdCommandHandler</c> persists them. Those records are
/// the same personal data that ADR 0050's pre-beta-data gate B-1 covers. <b>B-1 CLOSED
/// 2026-08-16</b> — the master key is no longer plaintext on disk (tmpfs, rotated to
/// <c>local-v3</c>, measured in <c>vps-deploy-stack.md</c> rows 21-25). <b>B-1 was never the only
/// gate.</b> <b>The condition for loading is Klas's explicit
/// written GO</b> — a DECISION, not a derivable state, and deliberately so: four state-shaped
/// conditions each failed open on 2026-08-16 as their sub-condition discharged. Its home is
/// <c>release-checklist.md</c> §2.6 point 3.5; #1240 owns the load itself. The flip that
/// satisfied this condition is recorded below.
/// <b>No discharged gate, ticked box or closed issue is permission.</b> Klas
/// confirmed the sequencing 2026-08-05: the stack may be deployed and every cutover proof taken,
/// but recruiter contact records must not land until that GO is given. A deployed Worker registers
/// <c>sync-platsbanken-stream</c> on a ten-minute cron, so without this switch the first ingest
/// runs within ten minutes of the first <c>up -d</c>.
/// </para>
///
/// <para>
/// <b>Dated record — 2026-08-17: the GO was given and the flip performed</b>, and recruiter
/// contact records landed from that date. The record carrying adjudicator and place is
/// <c>release-checklist.md</c> §2.6 point 3.5, and is not restated here.
/// <b>That was one box's operator flip — not a shipped default, and not permission.</b> The
/// Worker's Production overlay and <c>deploy/docker-compose.yml</c> both still ship
/// <see langword="false"/>, and §2.6 point 3.5 forbids switching that box's ingestion back off on
/// this comment's strength just as squarely. <b>For state read #1240, never this comment.</b>
/// </para>
/// </summary>
public sealed class JobSourceIngestOptions
{
    /// <summary>
    /// When false, the Platsbanken stream and snapshot jobs stay registered but do no work — no
    /// source call, no upsert, no audit row — and each logs a warning naming itself. Default
    /// <see langword="true"/>: dev and test ingest as before, and the value is turned off in the
    /// Worker's Production overlay so the polarity fails safe on a deployed box.
    /// </summary>
    public bool IngestEnabled { get; set; } = true;
}
