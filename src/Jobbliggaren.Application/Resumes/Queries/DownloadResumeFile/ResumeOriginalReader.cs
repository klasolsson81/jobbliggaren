using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes.Files;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Queries.DownloadResumeFile;

/// <summary>
/// The half of the original-file read path that is the SAME for every key: resolve the calling
/// owner, and open one <see cref="ResumeFile"/>'s Form C envelope into the transport DTO.
///
/// <para>Two queries reach a stored original by two different keys — a canonical
/// <c>ResumeId</c> and a staging <c>ParsedResumeId</c> — because they are two API surfaces with
/// two id forms. "Which row" is genuinely different per key and lives in each handler; "open the
/// envelope, redact the filename, shape the DTO" is ONE piece of knowledge and lives here (DRY as
/// one-place-per-piece-of-knowledge, not as text de-duplication). Copying the decrypt block into
/// both handlers is what would be the duplication.</para>
///
/// <para>Pure static functions over injected ports — no state, so this is not the stateful static
/// helper §5 bans, and it needs no DI registration of its own.</para>
/// </summary>
internal static class ResumeOriginalReader
{
    /// <summary>
    /// The authenticated caller, or <c>null</c> when there is no user or no job-seeker row. Every
    /// caller treats <c>null</c> as "return null" — the same fail-closed shape the sibling resume
    /// handlers use.
    ///
    /// <para>Both values come back together deliberately. The caller needs the
    /// <see cref="JobSeekerId"/> to scope its query and the user id to write a failed-access event,
    /// and returning only the first would make the second a null-forgiving <c>!</c> whose guarantee
    /// lives in THIS file rather than in the caller's — a contract the compiler cannot see. A
    /// nullable tuple carries the guarantee in the type instead.</para>
    /// </summary>
    public static async Task<(JobSeekerId JobSeekerId, Guid UserId)?> ResolveOwnerAsync(
        IAppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return null;

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == userId)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return jobSeekerId == default ? null : (jobSeekerId, userId);
    }

    /// <summary>
    /// Decrypt-to-buffer (AES-GCM verifies the whole tag before returning plaintext — no
    /// incremental streaming is possible; ADR 0100 §D3). The plaintext leaves via the DTO only and
    /// is never logged (§5).
    ///
    /// <para>Owner invariant (single source of truth by construction): the JobSeekerId that
    /// filtered the row and the owner the opener decrypts under (ICurrentDataOwner, warmed by
    /// FieldEncryptionKeyPrefetchBehavior off IRequiresFieldEncryptionKey) are the SAME
    /// authenticated owner. Any divergence would decrypt with the wrong DEK → AES-GCM tag failure
    /// → CryptographicException → fail-closed 500, never a cross-owner plaintext leak.</para>
    ///
    /// <para>The filename is re-redacted belt-and-braces (already redacted at rest in
    /// <c>ResumeFile.CaptureOriginal</c>, M-F1) so the M-F2 Content-Disposition header can never
    /// carry a plaintext personnummer even if the at-rest value were ever bypassed.</para>
    /// </summary>
    public static ResumeFileDownloadDto Open(IBinaryFieldOpener opener, ResumeFile file)
    {
        var plaintext = opener.Open(file.SealedContent);
        var safeFileName = PersonnummerRedactor.Redact(file.FileName);

        return new ResumeFileDownloadDto(plaintext, file.ContentType, safeFileName);
    }
}
