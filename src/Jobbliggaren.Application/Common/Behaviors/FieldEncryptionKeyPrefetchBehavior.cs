using System.Security.Cryptography;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Common.Behaviors;

/// <summary>
/// TD-13 (ADR 0049 Mekanik-not 3/4, CTO Approach B) — förladdar den
/// auktoriserade ägarens DEK till den scope-bundna cachen INNAN handlern
/// materialiserar krypterade entiteter. Pipeline-ordning: efter
/// Authorization/AdminAuthorization (ingen KMS-op för ej auktoriserad
/// principal, §5.4), före UnitOfWork (cachen varm när handlerns query
/// materialiserar). Endast meddelanden som bär
/// <see cref="IRequiresFieldEncryptionKey"/> (opt-in — inga onödiga KMS-op).
///
/// Gör <c>FieldDecryptionMaterializationInterceptor</c> till en ren synkron
/// cache-hit (EF Core 10 InitializedInstance är synkron; §3.5 förbjuder
/// sync-over-async). Fail-closed: KMS-fel propageras här, före handlern —
/// requesten failar utan att exponera ciphertext.
/// </summary>
public sealed class FieldEncryptionKeyPrefetchBehavior<TMessage, TResponse>(
    ICurrentUser currentUser,
    IAppDbContext db,
    IUserDataKeyStore dataKeyStore,
    ICurrentDataOwner currentDataOwner)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        if (message is IRequiresFieldEncryptionKey && currentUser.UserId.HasValue)
        {
            var jobSeekerId = await db.JobSeekers
                .AsNoTracking()
                .Where(js => js.UserId == currentUser.UserId.Value)
                .Select(js => js.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (jobSeekerId != default)
            {
                currentDataOwner.SetOwner(jobSeekerId);
                // Värmer ScopedUserDataKeyCache (samma cache som
                // encrypt-on-write). Fail-closed: KMS-fel kastar här.
                var dek = await dataKeyStore
                    .GetOrCreateDataKeyAsync(jobSeekerId, cancellationToken)
                    .ConfigureAwait(false);
                // Prefetchen värmer bara cachen (som äger sin egen kopia och
                // nollar den vid scope-dispose). Den returnerade klonen används
                // aldrig → nolla den så plaintext-nyckelmaterial inte läcker
                // o-zeroat (Low1, epic #480; paritet FieldEncryptionBackfiller).
                CryptographicOperations.ZeroMemory(dek);
            }
        }

        return await next(message, cancellationToken).ConfigureAwait(false);
    }
}
