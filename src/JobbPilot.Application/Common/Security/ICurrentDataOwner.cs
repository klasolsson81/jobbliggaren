using JobbPilot.Domain.JobSeekers;

namespace JobbPilot.Application.Common.Security;

/// <summary>
/// TD-13 (ADR 0049 Mekanik-not 3/4) — scope-bunden bärare av den
/// auktoriserade request-ägarens <see cref="JobSeekerId"/>.
/// <c>FieldEncryptionKeyPrefetchBehavior</c> sätter den (efter Authorization,
/// ADR 0031 <c>currentUser → JobSeekerId</c>) innan handlern kör.
/// <c>FieldDecryptionMaterializationInterceptor</c> läser den för
/// barn-entiteter (ApplicationNote/FollowUp saknar egen JobSeekerId — de
/// nås via aggregatroten, vars data är owner-scoped per query). Scoped, ej
/// ambient/static — nyckel-/ägar-kontext dör med scopet (CLAUDE.md §5.1).
/// </summary>
public interface ICurrentDataOwner
{
    JobSeekerId? JobSeekerId { get; }

    void SetOwner(JobSeekerId jobSeekerId);
}
