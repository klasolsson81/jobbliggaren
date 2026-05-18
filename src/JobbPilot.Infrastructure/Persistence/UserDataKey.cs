using JobbPilot.Domain.JobSeekers;

namespace JobbPilot.Infrastructure.Persistence;

/// <summary>
/// TD-13 (ADR 0049 Beslut 1) — wrapped per-användare-DEK. <b>Infrastructure-
/// entitet, EJ Domain, EJ aggregate root.</b> Mappas keyless-på-aggregat-vis
/// via <c>AppDbContext.Set&lt;UserDataKey&gt;()</c> och exponeras ALDRIG via
/// <c>IAppDbContext</c> eller in i <c>JobbPilot.Application</c> (CTO-triage
/// FRÅGA 2 2026-05-18; ISP/Clean Arch, Martin 2017 kap. 10/22; ADR 0009).
///
/// PK <c>(job_seeker_id, dek_version)</c> stödjer DEK-rotation (ADR 0049
/// Beslut 4 — sentinel bär version). <see cref="WrappedDek"/> är CMK-wrappad
/// (KMS) — aldrig klartext. Ingen EF-navigation till JobSeeker (FK på DB-nivå
/// i migrationen); crypto-erasure raderar raderna explicit i hard-delete-
/// transaktionen (C6), inte via cascade-magi.
/// </summary>
public sealed class UserDataKey
{
    // EF-materialiserings-ctor.
    private UserDataKey()
    {
        WrappedDek = [];
        CmkKeyId = string.Empty;
    }

    public UserDataKey(
        JobSeekerId jobSeekerId,
        int dekVersion,
        byte[] wrappedDek,
        string cmkKeyId,
        DateTimeOffset createdAt)
    {
        JobSeekerId = jobSeekerId;
        DekVersion = dekVersion;
        WrappedDek = wrappedDek;
        CmkKeyId = cmkKeyId;
        CreatedAt = createdAt;
    }

    public JobSeekerId JobSeekerId { get; private set; }

    public int DekVersion { get; private set; }

    public byte[] WrappedDek { get; private set; }

    public string CmkKeyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
