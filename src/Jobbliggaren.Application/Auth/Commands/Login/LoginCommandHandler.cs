using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Dtos;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Auth.Commands.Login;

public sealed class LoginCommandHandler(
    IAppDbContext db,
    IUserAccountService userAccountService,
    ISessionStore sessionStore,
    IAuthAuditLogger auditLogger)
    : ICommandHandler<LoginCommand, Result<SessionDto>>
{
    public async ValueTask<Result<SessionDto>> Handle(
        LoginCommand command, CancellationToken cancellationToken)
    {
        var credentialsResult = await userAccountService.ValidateCredentialsAsync(
            command.Email!, command.Password!, cancellationToken);

        if (credentialsResult.IsFailure)
        {
            // #503 G3(b): discriminate a lockout from an ordinary wrong password for attack
            // telemetry (account_locked_out is a targeted-brute-force signal). The wire
            // response is identical for both (AuthEndpoints normalizes) — only the audit
            // event differs.
            // #714: EmailNotConfirmed is NOT a failed login attempt — the credentials were valid, the
            // account just is not confirmed yet — so it emits NO LoginFailed audit (that would pollute
            // the brute-force signal). Its wire response is a distinct 403 (AuthEndpoints). The re-auth
            // path never reaches this code (it normalizes EmailNotConfirmed to InvalidCredentials).
            if (credentialsResult.Error.Code == AuthErrorCodes.AccountLocked)
                auditLogger.AccountLockedOut(HashEmail(command.Email!));
            else if (credentialsResult.Error.Code != AuthErrorCodes.EmailNotConfirmed)
                auditLogger.LoginFailed(HashEmail(command.Email!));
            return Result.Failure<SessionDto>(credentialsResult.Error);
        }

        var userId = credentialsResult.Value.UserId;

        // ADR 0024 D5 — blockera login för soft-deletade konton inom 30-dagars
        // restore-fönstret. JobSeeker.DeletedAt är 1:1-mapped mot ApplicationUser
        // (UserId), så vi behöver inte modifiera Identity-tabellen för restore-
        // semantiken. IgnoreQueryFilters för att se soft-deletad rad.
        // AsNoTracking — read-only-check, ingen mutation.
        //
        // SÄKERHET: vi returnerar samma fel (Auth.InvalidCredentials, 401) som
        // okänt-konto / fel-lösen för att undvika "deleted account oracle"
        // (security-auditor STEG 10b Major-1, GDPR Art. 32). Att avslöja
        // konto-status efter giltig credential-validering ger credential-stuffing-
        // listor en ny målgruppsfilter (just-deleted accounts → high-value social
        // engineering). Användaren kontaktar support out-of-band om de vill
        // återställa kontot.
        //
        // #1349 — an account with NO JobSeeker row is refused too. This branch used to continue, on the
        // premise "Register skapar båda atomiskt" — false: registration is deliberately non-atomic
        // across two boundaries (ADR 0024 D6, AccountHardDeleter.cs:19-28).
        //
        // The guard sits here because login is the CAPABILITY seam: the only unauthenticated place a
        // profile-less row can be granted anything (the other ISessionStore.CreateAsync call site is
        // RegisterCommandHandler's legacy branch, unreachable outside Dev/Test per
        // AuthOptionsValidator). That makes every delivery route and both activation seams —
        // /verify-email and the #1303 reset write — inert, instead of enumerating them.
        var jobSeeker = await db.JobSeekers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(js => js.UserId == userId, cancellationToken);

        // Two grounds, one outcome: a soft-deleted profile (ADR 0024 D5) and no profile at all (#1349).
        // The uniform InvalidCredentials is the ratified answer for the first (security-auditor STEG 10b
        // Major-1) and is kept for the second rather than inventing a distinct code — the fix must not
        // open the account-status oracle two lines above it closes.
        if (jobSeeker is null || jobSeeker.DeletedAt is not null)
        {
            auditLogger.LoginFailed(HashEmail(command.Email!));
            return Result.Failure<SessionDto>(
                DomainError.Validation(
                    AuthErrorCodes.InvalidCredentials,
                    "E-post eller lösenord är felaktigt."));
        }

        // Activation (#481 2b-3b): "Håll mig inloggad" checked → a rotating Persistent
        // session (30d sliding / 180d absolute cap, id rotates every 24h — the refresh
        // driver that drives that rotation ships in this same PR, so the > 30d reach is
        // only ever exposed WITH rotation, per security C3/COND-1). Unchecked/absent → a
        // short session-scoped Session (dies on browser close, the Art. 25(2) safe default
        // for shared computers). No new login lands on Legacy any more; existing Legacy
        // sessions keep today's reach until they expire (ship-silently).
        var lifetime = command.RememberMe ? SessionLifetime.Persistent : SessionLifetime.Session;
        var session = await sessionStore.CreateAsync(userId, lifetime, cancellationToken);

        auditLogger.LoginSucceeded(userId, session.Id.ToString());

        return Result.Success(new SessionDto(session.Id.Reveal()));
    }

    private static string HashEmail(string email)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant());
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
