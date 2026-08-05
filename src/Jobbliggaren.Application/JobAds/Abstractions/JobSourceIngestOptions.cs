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
/// the same personal data that ADR 0050's pre-beta-data gate B-1 covers, and B-1 is not closed —
/// the field-encryption master key is still plaintext on disk (issue #198 owns the repair). Klas
/// confirmed the sequencing 2026-08-05: the stack may be deployed and every cutover proof taken,
/// but recruiter contact records must not land until B-1 closes. A deployed Worker registers
/// <c>sync-platsbanken-stream</c> on a ten-minute cron, so without this switch the first ingest
/// runs within ten minutes of the first <c>up -d</c>.
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
