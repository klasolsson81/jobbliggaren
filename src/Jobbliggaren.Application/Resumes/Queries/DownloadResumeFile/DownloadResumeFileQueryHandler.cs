using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes.Files;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Queries.DownloadResumeFile;

/// <summary>
/// Loads the OWNING job seeker's stored original CV file and decrypts it for download. Mirrors
/// <c>GetParsedResumeQueryHandler</c> EXACTLY for the fail-closed IDOR shape: resolve the owner
/// from <see cref="ICurrentUser"/>, FirstOrDefault on <c>db.ResumeFiles</c> filtered by Id +
/// JobSeekerId, return null on not-found OR cross-user (logging ONLY the cross-user attempt, never
/// an unknown-id typo — no enumeration oracle), else open the Form C envelope via
/// <see cref="IBinaryFieldOpener"/> and return the plaintext bytes.
///
/// <para>The decrypt happens HERE (Application, via the port) — never in the Api composition root
/// and never on the aggregate: the handler reads the opaque <c>SealedContent</c> ciphertext off the
/// aggregate and opens it outside the model (aggregate-honesty, ADR 0100 CTO Q2). The query is
/// <see cref="IRequiresFieldEncryptionKey"/>, so the owner DEK is warm by the time
/// <see cref="IBinaryFieldOpener.Open"/> peeks it. The filename is re-redacted belt-and-braces
/// (already redacted at rest in <c>ResumeFile.CaptureOriginal</c>) so the M-F2 header can never
/// carry a plaintext personnummer. The bytes are never logged (§5).</para>
/// </summary>
public sealed class DownloadResumeFileQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger,
    IBinaryFieldOpener opener)
    : IQueryHandler<DownloadResumeFileQuery, ResumeFileDownloadDto?>
{
    public async ValueTask<ResumeFileDownloadDto?> Handle(
        DownloadResumeFileQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return null;

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return null;

        var fileId = new ResumeFileId(query.FileId);
        var file = await db.ResumeFiles
            .AsNoTracking()
            .Where(f => f.Id == fileId && f.JobSeekerId == jobSeekerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (file is null)
        {
            // Identical NotFound for cross-user and unknown — no enumeration oracle. Log the
            // cross-user attempt ONLY when the row exists for someone else (ownership check would
            // have matched without the user filter); a plain unknown-id typo is not logged.
            var exists = await db.ResumeFiles
                .AsNoTracking()
                .AnyAsync(f => f.Id == fileId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "ResumeFile", fileId.Value, currentUser.UserId.Value, "DownloadResumeFile");
            }

            return null;
        }

        // Decrypt-to-buffer (AES-GCM verifies the whole tag before returning plaintext — no
        // incremental streaming is possible; ADR 0100 §D3). The plaintext leaves via the DTO only.
        // Owner invariant (single source of truth by construction): the JobSeekerId that filtered the
        // row above (ICurrentUser-derived) and the owner the opener decrypts under (ICurrentDataOwner,
        // warmed by FieldEncryptionKeyPrefetchBehavior off IRequiresFieldEncryptionKey) are the SAME
        // authenticated owner. Any divergence would decrypt with the wrong DEK → AES-GCM tag failure →
        // CryptographicException → fail-closed 500, never a cross-owner plaintext leak.
        var plaintext = opener.Open(file.SealedContent);

        // Belt-and-braces: the column is already redacted at rest (M-F1); re-run the same
        // gap-aware, Luhn+date-gated redactor so the Content-Disposition header can never carry a
        // plaintext personnummer even if the at-rest value were ever bypassed (defense-in-depth).
        var safeFileName = PersonnummerRedactor.Redact(file.FileName);

        return new ResumeFileDownloadDto(plaintext, file.ContentType, safeFileName);
    }
}
