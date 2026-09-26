# #1735 (epic #1732 part 1a) — pre-code form round (dotnet-architect, 2026-09-19)

> Transcribed verbatim by the driving session from the agent's returned report. Brief as above plus security-auditor's report; told mid-run that Klas chose Q18 (A). Worktree `C:/tmp/jbl-1735` at `3f3d4784`. No escalation to Klas.

## Architecture analysis: #1735 pre-code form round (Q6–Q17), dotnet-architect

### Summary
Some items must be settled before code: 4 Kritiskt, 15 Viktigt and 5 Nice-to-have. Only Klas's answer (A) of 2026-09-19 is bound here, with security's conditions (i)–(iii); there is no (B) variant. Two of the Kritiskt items decide whether (A) delivers what it promises at all: R1 (a code burn must never kill the link) and R4 (one budget key cannot hold two windows). Measured against `C:/tmp/jbl-1735` at `3f3d4784` on 2026-09-19. The StackExchange.Redis API surface was checked in the 3.1.13 package XML (`Directory.Packages.props:95`). No files were edited, nothing was built and nothing was run.

### Recommendation

**R1 [Kritiskt] Q7/Q8 with Klas's (A): a code burn must never kill the link. "Burned" is a state of the code arm only.**
**What:** under (A), the targeted owner's only relief is a link in a mail that someone else caused. The requester holds that record's `challengeId` from the 202 body (D2:139). The victim cannot use the code, because the code is bound to the minter's cookie (D2:163-165). If "3 attempts then burn" (D1:105, table :553) deletes or poisons the whole record, the attacker can follow each mint with 3 verifies. Every link-only mail is then dead on arrival, and the (A) promise to Klas ("offret har alltid en giltig länk") is false.
**Why:** D1:105-108 separates the two arms in one direction only: a wrong link never spends the code's attempts. (A) needs the other direction too. Security's "a link-only record never burns the live code challenge" covers only the mint side.
**Bind:**
- The attempt counter `a` gates `ConsumeCodeAsync` only. `ConsumeLinkAsync` never reads it.
- A record leaves Redis in exactly three ways: a successful consume on either arm (`DEL` returns 1), a newer code-bearing Put (R8), or its TTL.
- A burned record therefore stays until its TTL. That answers Q7: a fourth verify gets `Burned`, not `Missing`.
- #1735's acceptance gains two rows: "3 wrong codes, then the link still consumes", and a mutation check that catches the link arm reading `a`.
**Route:** security confirms, because this changes what D1's "burn" removes.

**R2 [Kritiskt] Q8: consume-by-code is one Lua script with an existence guard**
**What:** `HINCRBY` on a missing key creates the key with no TTL. `challengeId` is chosen by the client, so a two-step "HINCRBY, then check" form lets anyone create keys that never expire, at `AuthWrite`'s 20 requests/min per IP.
**Bind:** one declared key, with the `jobbliggaren:` prefix inside `KEYS[1]`:
```lua
if redis.call('EXISTS', KEYS[1]) == 0 then return false end
local n = redis.call('HINCRBY', KEYS[1], 'a', 1)
return { n, redis.call('HGET', KEYS[1], 'p') }
```
Run it with `ScriptEvaluateAsync(string, RedisKey[], RedisValue[])`, which exists in 3.1.13; the library caches the script's SHA. The C# side then decides:
- **nil** → a dummy compare → `Missing`.
- **`n > MaxAttempts`** → a dummy compare → `Burned`.
- **Otherwise** → `Unprotect(p)`, then `CryptographicOperations.FixedTimeEquals` against the stored code. If the payload has no code, compare against a static 6-byte dummy instead, so a code-less record answers Wrong, Wrong, Burned (security condition (i)).
- **Hit** → `KeyDeleteAsync`. `true` means `Verified`; `false` means `Missing` (a concurrent consume or a newer mint won).
- **Miss** → `n == MaxAttempts ? Burned : Wrong(MaxAttempts − n)`.

`HINCRBY` keeps the key's TTL. The script is atomic, so a parallel burst gets distinct values of `n`. The #1735 atomicity row runs against Testcontainers Redis (R3).

**R3 [Kritiskt] Q10: no `InMemoryLoginChallengeStore`**
**What:** #1735 names this class and puts "store contract via the in-memory impl" into the Application unit tests.
**Why:**
- The contract is Redis semantics: increment before compare, `DEL`=1 as single use, the TTL, and R2's guard.
- A contract proven on an implementation that production never registers is AGENTS.md §5 `Tests:` ("a production fact asserted off a premise production cannot produce"). That is a §12 STOPP class.
- `InMemorySessionStore` already shows how this goes wrong: `DependencyInjection.cs:1798-1800` registers only the Redis session store and its decorator.
**Bind:**
- The contract suite runs against `RedisLoginChallengeStore` on its own Testcontainers Redis, with no WebApplicationFactory. The precedent is `RedisSessionStoreFailureTests.cs:14-47`.
- Handler tests use NSubstitute on `ILoginChallengeStore`, and return only verdicts the adapter can actually emit: `Verified`, `Wrong(2|1)`, `Burned`, `Missing`.

**R4 [Kritiskt] IRateBudget: one scope per window, typed, with the reorder and the (A) accounting**
**What:** D1:102 keys a budget as `budget/{scope}/v1/{hex}`, and security's register text names `budget/login-challenge/v1/{hex}` "for both windows". One key cannot hold two counters. Built literally:
- After the reorder the 10-minute gate runs first, so its `EXPIRE NX` fixes a 600-second TTL on the shared key.
- The 24-hour count then resets every 10 minutes and never binds: up to about 432 code mints a day instead of 10.
- The 0.003 %/day arithmetic (:561-563) is then off by roughly 43×.
**Bind:**
```csharp
public sealed record RateBudgetScope(string Name, int Limit, TimeSpan Window);   // ctor guards Limit >= 1, Window > 0
public interface IRateBudget { Task<bool> TryConsumeAsync(RateBudgetScope scope, string subject, CancellationToken ct); }
// LoginChallengePolicy (Q3's home):
//   MailBudget = new("login-challenge-mails", 3, 10 min)
//   CodeBudget = new("login-challenge-codes", 10, 24 h)
```
- **Key:** `jobbliggaren:budget/{scope.Name}/v1/{SubjectFingerprint.Hex(subject)}`.
- **Atomic form:**
  1. `CreateTransaction()`, queuing `StringIncrementAsync(key)` and `KeyExpireAsync(key, scope.Window, ExpireWhen.HasNoExpiry)`.
  2. `await ExecuteAsync()`, and only then await the queued INCR task.
  3. Return `count <= scope.Limit`.

  MULTI makes INCR and EXPIRE one unit, which satisfies security's "never without a TTL". `NX` fixes the window at the first counted use, so refused calls never extend it. `NX` needs Redis 7.0 or later: the box runs `redis:8.6-alpine` (`deploy/docker-compose.yml:647`) and the tests run `redis:8-alpine` (`ApiFactory.cs:26`).
- **Why a scope type:** it carries its own limit and window, so no caller can pair one scope with two windows.
- **Request path under (A):**
  1. `CanDeliver`.
  2. Cooldown (R12).
  3. `MailBudget`: refused means silent, no enqueue.
  4. `CodeBudget`: sets `CodeBudget.Admitted` or `Exhausted` on the dispatch. It is never a no-op.
  5. `Enqueue`.

  Each gate runs only if the previous one admitted the request (security Q18.1).

**R5 [Viktigt] Q6: the port. The adapter mints; plaintext never goes in.**
**Why:**
- Application cannot protect anything: `Jobbliggaren.Application.csproj:18-24` has no DataProtection reference.
- The ADR's own table already puts code minting in the adapter (:552).
- D1:77's `PutAsync(LoginChallenge, TimeSpan, ct)` returns nothing, so it cannot hand back what it minted.
- Its `ttl` parameter lets a caller type the TTL a second time, which security's Q3 forbids.

**Bind:**
```csharp
// ChallengeId, LoginCode and LoginLinkToken all take SessionId's shape (ISessionStore.cs:8-28): private value,
// Reveal(), ToString() => first 6 chars + "…", static FromRaw(string). ChallengeId adds Generate() (16 CSPRNG bytes, Base64Url).
public enum ChallengeCredentials { None, CodeOnly, LinkOnly, CodeAndLink }
public sealed record NewLoginChallenge(ChallengeId Id, string Email, ChallengeCredentials Credentials);
public sealed record IssuedCredentials(LoginCode? Code, LoginLinkToken? Link);
public sealed record LoginChallengeProof(string ProvenEmail);
public interface ILoginChallengeStore
{
    Task<IssuedCredentials> PutAsync(NewLoginChallenge challenge, CancellationToken ct);            // TTL = LoginChallengePolicy.ChallengeTtl
    Task<ChallengeVerdict> ConsumeCodeAsync(ChallengeId id, LoginCode presented, CancellationToken ct);
    Task<LoginChallengeProof?> ConsumeLinkAsync(LoginLinkToken token, CancellationToken ct);         // null = the one link failure
}
```
- D1:73's `record struct ChallengeId(string Value)` would print the whole id in any interpolated log line. The SessionId shape cannot.
- **Code:** `RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture)`. That is the runtime's unbiased range, which satisfies D10:463-464 without a hand-written rejection loop.
- **Link token:** `Base64Url(idBytes[16] ‖ secret[16])`, sent as one `token` parameter (A8). The adapter finds the record from the id half, so there is no per-link index key to write, expire or record in the register.
- **`TryClaimAsync`** moves to 1c. Nothing in 1a calls it, and a primitive with no caller ships its semantics untested.

**R6 [Viktigt] Q6/Q8: a record is one protected payload plus one counter. No stored subject flags.**
**What:**
- D1:94-95 stores `isNewAddress`, `registrationClosed` and `pendingDeletion` unprotected, next to a bare `linkTokenHash`.
- D1:96 puts a Redis reader in scope, and the address index (R8) is keyed by a fingerprint anyone holding the address can recompute (security Q20.1).
- Following index → record, either an unprotected `isNewAddress` or the mere presence of a link hash tells that reader whether the address has an account. Links exist only for existing accounts (D1:107-110).
**Bind:**
- The record is a hash at `jobbliggaren:auth/challenge/v1/{b64url(SHA256(id))}`, hashed the way `RedisSessionStore.cs:476-480` hashes session ids.
- It has exactly two fields:
  - `p` = `IDataProtector("Jobbliggaren.Auth.LoginChallenge.v1").Protect(json{ e: email, c: code?, l: b64(SHA256(secret))? })`
  - `a` = the attempt counter.
- No `expiresAt`: the TTL is the one place expiry lives (D1:105).
- **After proof, resolve at verify time.** The branch after proof (Q1's outcomes) looks up the proven address at that moment (R13), and this works under either Q1 answer. It is also the only correct way to read a kill-switch. A `RegistrationsOpen` value (ADR 0083) snapshotted 15 minutes earlier would let a user past a switch thrown in the meantime.
- "New address ⇒ no link" still holds by construction. `LoginChallengePlan` (R10) never asks for a link for a subject without an account, so the payload has no `l`.

**R7 [Viktigt] Q7: the verdict carries attempts remaining, and `Proof` is non-null by construction**
```csharp
public enum ChallengeOutcome { Verified, Wrong, Burned, Missing }   // D1:74 unchanged; an enum keeps CS8509 exhaustiveness
public sealed record ChallengeVerdict
{
    public ChallengeOutcome Outcome { get; }
    public LoginChallengeProof? Proof { get; }           // non-null iff Verified
    public int AttemptsRemaining { get; }                // 1..MaxAttempts-1 iff Wrong, else 0
    [MemberNotNullWhen(true, nameof(Proof))] public bool IsVerified => Outcome == ChallengeOutcome.Verified;
    private ChallengeVerdict(ChallengeOutcome outcome, LoginChallengeProof? proof, int attemptsRemaining) { /* … */ }
    public static ChallengeVerdict Verified(LoginChallengeProof proof);
    public static ChallengeVerdict Wrong(int attemptsRemaining);          // throws outside 1..MaxAttempts-1
    public static readonly ChallengeVerdict Burned, Missing;
}
```
- The properties are get-only, so a `with` expression cannot forge a `Verified` without a proof.
- Why no `WrongLastAttempt` enum member: the handler maps `AttemptsRemaining == 1` to the warning (Page form :609-610). That mapping stays true if `MaxAttempts` ever changes (lapse trigger 5).
- A burned record is kept until its TTL (R1).

**R8 [Viktigt] Q8: Put, and one live challenge per address. The index tracks the live CODE.**
**Bind:**
- **Index key:** `jobbliggaren:auth/challenge-by-address/v1/{SubjectFingerprint.Hex(email)}`. Its value is the record key's hash segment, and its expiry (PX) is the challenge TTL.
- **Only a Put that mints a code touches the index.** Link-only records and code-less new, closed or pending records never do. That matches security's "a link-only record never burns the live code challenge".
- **Sequence:**
```
tx = db.CreateTransaction()
  HSET    recordKey p <protected> a 0
  PEXPIRE recordKey <ttl>
  [only if a code is minted]  prev = SET indexKey <hashSeg> PX <ttl> GET      // StringSetAndGetAsync, inside the tx
await tx.ExecuteAsync()
[prev != null && prev != hashSeg]  DEL jobbliggaren:auth/challenge/v1/{prev}
```
**Why this form and not Lua:**
- Every key is declared up front.
- `SET … GET` is an atomic swap, so under any interleaving each Put deletes exactly the predecessor it displaced, and only the last one to swap survives.
- A crash between `EXEC` and `DEL` leaves the predecessor live until its TTL. That is bounded, and it does not affect the guess limit, because `CodeBudget` was already spent on the request path.
- The consumer is single-reader (the `PasswordResetDispatchChannel.cs:65` pattern), so within one process Puts are serial anyway.

**For security to grade (not graded here):** link-only records are not indexed, so under (A) up to about 6 can be live per inbox. That is 3 per fixed 10-minute window, with a 15-minute TTL spanning two windows. Each one is a 128-bit bearer credential sitting in the owner's inbox.

**R9 [Viktigt] Q6/Q8: consume by link**
```
decode token → exactly 32 bytes, else return null      (no FormatException escapes)
p = HGET recordKey(idHalf)                              (read-only: nothing is recreated)
p == null → FixedTimeEquals(dummy32, dummy32) → null
payload = Unprotect(p); l = payload.l ?? staticDummy32
FixedTimeEquals(SHA256(secret), l) && payload.l != null → KeyDeleteAsync: true ⇒ proof, false ⇒ null
```
- This path never reads or writes `a` (R1).
- Every existing record costs one `HGET`, one `Unprotect` and one compare, whatever its kind. So an attacker holding their own `challengeId` cannot use timing on a link probe to learn "this record carries a link" (that is, "this address has an account").
- If a code consume and a link consume race, exactly one of them sees `DEL` return 1.

**R10 [Viktigt] Q9: Infrastructure drains the queue, Application decides. The drain is a base class, not a copy.**
**The drain:**
- `internal abstract partial class BoundedDispatchService<T> : BackgroundService` takes over all of `PasswordResetDispatchService.cs:32-113`:
  - drain on `CancellationToken.None`;
  - `StopAsync` completes the writer first;
  - one scope per item, with resolution inside the try;
  - `catch … when (ex is not OperationCanceledException)`.
- Subclasses supply `DispatchOneAsync(T item, IServiceProvider scoped, CancellationToken ct)` and their own `[LoggerMessage]` failure line.
- **Why a base class:** a copy would not be covered by `PasswordResetDispatchServiceShutdownTests`, whose bug "failed ONLY under Email:Provider=Scaleway" (`:26-29`). After D10, that bug means nobody can log in.

**The issuer:** per item, the subclass resolves `LoginChallengeIssuer`, a plain sealed Application class. It is registered in `AddIdentityAndSessions` the way `ReauthenticationService` is (`DependencyInjection.cs:1823`), so it can be tested with fakes (AGENTS.md §2.4).
```csharp
public sealed record LoginChallengeDispatch(ChallengeId ChallengeId, string Email, CodeBudget CodeBudget, string? IpAddress, string? UserAgent);
public enum CodeBudget { Admitted, Exhausted }
public Task IssueAsync(LoginChallengeDispatch dispatch, CancellationToken ct);
// subject (R13) → kind = LoginChallengePlan.Decide(subject, d.CodeBudget, auth.RegistrationsOpen)
// → PutAsync (record BEFORE the mail, security Q2) → SendLoginChallengeAsync → audit for known accounts
```
**The plan:** `LoginChallengePlan.Decide` is a pure function that covers every input and returns `LoginChallengeKind`. It is Klas's (A) matrix as one exhaustively tested table:

| Subject | Code budget | Kind |
|---|---|---|
| Active account | Admitted | `CodeAndLink` |
| Active account | Exhausted | `LinkOnly` |
| No account, registration open | Admitted | `NewAccountCode` |
| No account, registration open | Exhausted | `NewAccountCodeLimitReached` |
| No account, registration closed | any | `RegistrationClosed` |
| Pending deletion, profile missing | any | per Q1 |

The kind drives the credentials, the mail variant and the audit line.

**Audit and logging:**
- `LoginChallengeIssued(Guid userId, LoginChallengeKind kind, string? ipAddress, string? userAgent)`, with the context carried in as for `PasswordResetRequested` (`IAuthAuditLogger.cs:52`). `LinkOnly` is the only operational signal of the Q18 drain attack.
- `LoginSucceeded(Guid userId, string sessionIdPrefix, LoginMethod method)` replaces the two-argument member; it is not an overload (the 1b `Register` precedent). That touches 2 `src` sites (`LoginCommandHandler.cs:100`, `RegisterCommandHandler.cs:166`) and 9 test sites (`git grep -n "\.LoginSucceeded(" -- src tests`). `LoginMethod { Password, Code, Link }`.
- Event ids 1001–1008 are taken (measured). Proposed: 1009 queue full, 1010 dispatch failed (security Q19's line), 1011 issued, 1012 payload unreadable (R12), 1013 inbox proven (F5).

**R11 [Viktigt] Q11: response shapes**
- **`POST /auth/challenge`** → 202 `{challengeId}` on every path except the 503 (D2:139). Mint the id right after `CanDeliver`, so every exit returns the same shape.
- **Success after proof, on both verify and link** → 200 with an `outcome` field that is always present: `{outcome:"signedIn", sessionId}`. 1c adds `consentRequired` with `grantToken`, and Q1's outcomes add their own values. Binding `outcome` in 1a keeps the 1a→1c contract stable.
- **No `persistent` field.** It is always true on these routes (D4:211-219). `AuthEndpoints.cs:141-145` carries it only because change-password's lifetime varies.
- **Failures** go through `ToErrorResult` to the central mapper (`DomainErrorResults.cs:29-40`), with no endpoint-specific arm:

  | Code | Status |
  |---|---|
  | `Auth.LoginCodeWrong` | 400 |
  | `Auth.LoginCodeWrongLastAttempt` | 400 |
  | `Auth.LoginCodeBurned` | 410 |
  | `Auth.LoginCodeExpired` | 410 (Missing and Expired share this one code, as A4 requires) |
  | `Auth.LoginLinkUnusable` | 410 (the one link-failure answer) |

  `DomainError` has no extension slot (`DomainError.cs:3`), so "last attempt" is a separate code rather than an `attemptsRemaining` field.
- **Validators:** `code` must match `^[0-9]{6}$`. That keeps the `FixedTimeEquals` lengths equal, and a malformed code spends no attempt. `challengeId` and `token`: not empty, with a maximum length.

**R12 [Viktigt] Q12: fault translation, and the cooldown moves onto IRateBudget**
**Redis faults:**
- The new adapters share one translation, `RedisFaults.GuardAsync`. It uses the same filter as `SessionStoreResilienceDecorator.cs:84-91` (`RedisException or RedisTimeoutException`).
- It throws a new unsealed base, `StoreUnavailableException`. `SessionStoreUnavailableException`, sealed today (`:3`), derives from it, and the `Program.cs:304` arm catches the base. Sessions behave exactly as before.
- Event 2050's name stays, because it is the alarm key.

**Lost keyring:**
- A typed `catch (CryptographicException)` goes around `Unprotect` only. Add `JsonException` for the payload, following the #511 precedent at `RedisSessionStore.cs:28-48`.
- Either one means `Missing` on verify or `null` on the link path, logged with EventId 1012 (the exception type only).
- Without this catch, `Program.cs:289-303` answers a bare 500 instead of D1:120-121's "expired".
- On `Protect` during a Put, the failure propagates to the consumer's failure line: no record and no mail.

**Cooldown:** use `IRateBudget` with `RateBudgetScope("login-challenge-cooldown", 1, window)`, not `ICooldownGate`.
- After the reorder, the cooldown is the first Redis call on `/auth/challenge`, and `RedisCooldownGate` faults are untranslated (they surface as 500).
- This form lets Q5 stay "only the three new routes", and the counter is atomic where `RedisCooldownGate.cs:26-29` is not. The semantics are the same: a fixed, silent window.
- If the CTO widens Q5 instead, keep `ICooldownGate` and translate inside `RedisCooldownGate`. The register key follows whichever is chosen.
- The window goes in `AuthEmailCooldownOptions.LoginChallengeWindowSeconds`, `[Range(1, 3600)]`, default 60 (precedent at `:46-47`).

**R13 [Viktigt] Q13: one composite, assembled in Application; the request path pinned by its constructor**
**What:** `UserAccountService` can see only Identity (`UserAccountService.cs:13-17`). `DeletedAt` lives on `JobSeeker` in `AppDbContext`, and the existing pattern reads it from Application (`LoginCommandHandler.cs:72-75`).
**Bind:**
```csharp
Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken ct);   // IUserAccountService; doc: off-request-path or post-proof callers only
public abstract record LoginSubject { private LoginSubject() { }
  public sealed record NoAccount : LoginSubject;
  public sealed record Active(Guid UserId) : LoginSubject;
  public sealed record PendingDeletion(Guid UserId, DateTimeOffset DeletedAt) : LoginSubject;
  public sealed record ProfileMissing(Guid UserId) : LoginSubject; }      // #1349; its placement is Q1's
public sealed class LoginSubjectResolver(IUserAccountService accounts, IAppDbContext db)
{ public Task<LoginSubject> ResolveAsync(string email, CancellationToken ct); }   // IgnoreQueryFilters().AsNoTracking(), projected
```
- This is the one place that classifies #1349 and soft-deleted accounts. The issuer uses it to pick the mail variant, and R14 uses it after proof.
- A bare lookup is only safe because of where it is called, so D2's property needs a test that can fail. Add a reflection test that `RequestLoginChallengeCommandHandler`'s constructor takes none of `IUserAccountService`, `IAppDbContext`, `LoginSubjectResolver`, `ILoginChallengeStore` or `IServiceProvider`.
- Why constructor parameters rather than a NetArchTest type rule: a service-locator call inside an async body lives in the compiler-generated state-machine type, which a type rule does not inspect.

**R14 [Viktigt] Q14 and Q21: one grant, and the first-proof write behind its own single-consumer port**
```csharp
public sealed class PasswordlessSessionGrant(IInboxProofRecorder proof, ISessionStore sessions, IAuthAuditLogger audit)
{ public Task<SessionDto> GrantAsync(LoginSubject.Active subject, LoginMethod method, CancellationToken ct); }
public interface IInboxProofRecorder { Task<InboxProof> RecordAsync(Guid userId, CancellationToken ct); }
public enum InboxProof { AlreadyConfirmed, FirstProofRecorded }
```
- **Order inside `GrantAsync`:** `RecordAsync`; if it returns `FirstProofRecorded`, call `InvalidateAllForUserAsync`; then `CreateAsync(Persistent)`; then `LoginSucceeded`. Invalidating before creating follows `AuthEndpoints.cs:129-133`.
- **The recorder:**
  - If `EmailConfirmed` is already true, it writes nothing.
  - Otherwise it sets `EmailConfirmed = true` and calls `RemovePasswordAsync(user)`. Identity nulls the hash and rotates the stamp inside the same `UpdateUserAsync` that saves the flag, which is security's single Identity write.
- **Why `LoginSubject.Active` as the parameter:** the #1349 and soft-delete guard becomes a type rather than a repeated query. Verify, link and 1c's `complete` share it, so there is one `CreateAsync` site instead of two.
- **Why a separate port:** security ruled out "a bare force-confirm on IUserAccountService". Pin it with a type-level test: only `PasswordlessSessionGrant` depends on `IInboxProofRecorder`, and only the verify and link handlers depend on the grant. Security confirms this is the form she meant.

**R15 [Viktigt] Q15 (security decided the capture holds the code only): wiring that survives `ApiFactory`**
**What:**
- `ApiFactory.cs:174-175` removes every `IEmailSender` registration after the Development host is composed (`:85`).
- A decorator added by `AddDevOnlyTestingSupport` (`DependencyInjection.cs:72`, which runs after `:66`) is therefore gone in the test host.
- Without it, every test of `/dev/login-code` passes on the 404 branch, which is the failure mode of security's condition 3.
**Bind:**
- **One internal extension, `AddDevLoginCodeCapture()`.**
  - It calls `TryAddSingleton<DevLoginCodeCapture>()` and wraps the last `IEmailSender` registration in a factory. The repo has no Scrutor and zero `Decorate<` calls (measured).
  - Its only production caller is `AddDevOnlyTestingSupport`, under `IsDevelopment()`.
  - Its test caller is `ApiFactory`, right after the `RecordingEmailSender` swap. Infrastructure already grants the integration tests InternalsVisibleTo (`csproj:220`).
- **What the decorator captures:**
  - Only `CodeAndLink` and `NewAccountCode` mails, and only when `ConsoleEmailSender.IsReservedRecipient` (`:181`) is true.
  - Keyed by `SubjectFingerprint.Hex`, holding the code only.
  - TTL no longer than `ChallengeTtl`, measured with `IDateTimeProvider`; size-capped; readable once.
  - It always forwards the mail.
- **The read side** is an `Application/Dev/Abstractions` port read by an `Application/Dev` query. Deleting `Application/Dev/` then breaks the capture's build, the same mechanism that already removes `DevEmailConfirmer`. Q4 itself is the CTO's call.
- **Composition pin, both polarities:** in Development the capture wraps `ConsoleEmailSender`; in Production the sender is a bare `NullEmailSender`.
- **Link tests:** `RecordingEmailSender` gains `IReadOnlyList<RecordedLoginChallenge(string ToEmail, LoginChallengeEmail Content)>`. Its comments at `:9` and `:21-22` are corrected in the same change.

**R16 [Viktigt] Q16: one port member, a closed content hierarchy, and one template per variant**
```csharp
Task SendLoginChallengeAsync(string toEmail, LoginChallengeEmail content, CancellationToken cancellationToken);  // IEmailSender, member 10
public abstract record LoginChallengeEmail { private LoginChallengeEmail() { }
  public sealed record CodeAndLink(LoginCode Code, LoginLinkToken Link) : LoginChallengeEmail;
  public sealed record LinkOnly(LoginLinkToken Link) : LoginChallengeEmail;                        // Klas (A)
  public sealed record NewAccountCode(LoginCode Code) : LoginChallengeEmail;
  public sealed record NewAccountCodeLimitReached : LoginChallengeEmail;                           // F3
  public sealed record RegistrationClosed : LoginChallengeEmail;
  public sealed record PendingDeletion(DateOnly PermanentDeletionDate) : LoginChallengeEmail; }   // membership per Q1
```
- **Why one port member:** it breaks the five implementations once. The reflection guard (`ConsoleEmailSenderReservedRecipientTests.cs:186-199`) gains one case, kind `login-challenge`.
- **Templates:** one `internal static EmailContent` method per variant, plus a `LoginChallenge` dispatcher that ends in `_ => throw new UnreachableException()`.
  - `EmailHtmlNoRemoteResourceTests.cs:248-263` reflects over every static method returning `EmailContent`, public or not, so each variant method is forced into `RenderAll`.
  - A single method with an internal switch would pass that guard with one fixture.
  - One method per variant also lines up with the register's recipient classes (security Q20.2).
- **Closure is by convention only.** A record that is not sealed must keep a protected copy constructor (CS8878). Add a reflection test that every nested variant has a template case.
- **One link builder, `LoginLink(baseUrl, token)`**, producing `/logga-in/lank?token=…`, shared by both link-bearing variants.
- **If Q1 keeps a date in the pending-deletion mail,** `HardDeleteAccountsJob.RestoreWindowDays` (`:33`, currently a private constant) must be lifted to one readable home.

**R17 [Nice-to-have] Q17: test hosts. No new host.**
- **Redis down:**
  - The translation is proven at adapter level, on an own container that the test stops. The precedent is `RedisSessionStoreFailureTests.cs:14-47`, with no WebApplicationFactory.
  - The Api-level mapping is proven through one singleton registered last in `ApiFactory`. It wraps `IRateBudget` and `ILoginChallengeStore` with an `Unavailable()` scope, the same shape as `RecordingEmailSender.cs:59-63`.
  - Per-test derived factories, as in `SessionStoreUnavailableTests.cs:102-135`, would spend the EF service-provider headroom `RecordingEmailSender.cs:50-56` documents.
- **Load-dropped:** `void Enqueue` leaves the endpoint nothing to branch on. The row is carried by the type, plus a unit test of `BoundedDispatchChannel<T>` with capacity 1 and no reader that checks the drop is logged under its own event id.
- **The `LinkOnly` branch in integration tests:** the 3-per-10-minutes gate makes 10 real code mints for one address unreachable inside a test. Seed the 24-hour counter through the adapter's own internal key function, and name the producing actor in the test (AGENTS.md §5 `Tests:`).
- Use a unique address per test: the whole Api collection shares one Redis (`ApiFactory.cs:26`).

### Findings (A1–A11 and security's bindings, where they cannot hold as written)

**F1 [Kritiskt]** Security's register text "`budget/login-challenge/v1/{hex}` for both windows", read together with D1:102's key form, cannot hold. The binding is R4. The register therefore gets these keys:
- two or three budget keys (mails 10 min, codes 24 h, plus the cooldown if R12's form is chosen);
- the record key with a hashed id;
- the address index.

**F2 [Viktigt] A1's parenthetical falls with security's reorder. This is a cross-report conflict for the CTO.**
A1 says budget-first "is also what makes #1735's 'Redis unavailable → one uniform 503' possible". Security's Q18.1 moves the cooldown first. `RedisCooldownGate.cs:20-70` has no Redis catch, so a Redis outage answers 500 on `/auth/challenge` before any budget call is made. "Q5 = only the three new routes" and "cooldown through `ICooldownGate`" cannot both hold. R12 resolves it one way; widening Q5 resolves it the other.

**F3 [Viktigt] Under (A), the row "new address, code budget spent" has no mail**
- Security's (A) text gives it a record (condition (i)) but no mail variant.
- D2:150 says the consumer "sends ONE mail", and lapse trigger 7 (:573-574) rests on that premise. Without a mail, trigger 7 fires and the resting copy (:604-609) is false for this row.
- R16 binds it as `NewAccountCodeLimitReached`: no code, and a class-(3) recipient, so Art. 14 and security's Q20.2 retention sentence apply.
- Copy belongs to design and security. Whether the variant exists at all is the CTO's call.

**F4 [Viktigt] A7's test pair must also assert the hosted consumer, not only the port**
- D2:159-161 names the pair "for the new port". A build that registers `ILoginChallengeDispatcher` without `AddHostedService<LoginChallengeDispatchService>` passes it and silently drops every login mail. After D10 that is a total login stop.
- **Positive test:** `AddIdentityAndSessions` registers both the port and an `IHostedService` whose `ImplementationType` is the consumer.
- **Negative test:** `AddCoreIdentityForWorker` registers neither. Measured: it registers `IUserAccountService` but no multiplexer and no DataProtection.

**F5 [Nice-to-have] Security's Q21 "write an audit row" needs a named sink, and there is one residual**
- **The sink:** `AuditBehavior` writes `audit_log` on every success of an `IAuditableCommand` (`IAuditableCommand.cs:5-10, 76-80`). It cannot write a row for the first-proof branch alone. So bind an `IAuthAuditLogger` line, `InboxProvenByLogin(Guid userId)` with event id 1013, unless security requires `audit_log`, which would need a command of its own.
- **The residual:**
  - If Redis fails between the Identity write and `InvalidateAllForUserAsync`, pre-existing sessions survive, and the retry sees `AlreadyConfirmed` so it never invalidates.
  - An unconfirmed account can only hold a session on the flag-OFF instant-login branch, which is unreachable in production (`LoginCommandHandler.cs:62-66`).

**F6 [Nice-to-have] A8 under (A): two link-bearing templates**
Page form :639-641 says "the new template" enters `RenderedLinks()`. Under (A) both `CodeAndLink` and `LinkOnly` render a link, so:
- both enter `RenderedLinks()`;
- `TokenLink` (`CaddyfileTokenScrubbingPinTests.cs:151-153`) gains `logga-in/lank`;
- `Count` (`:286-289`) goes from 3 to 5.

**F7 [Nice-to-have] Sentences 1a makes false, beyond security's list**
- `ICooldownGate.cs:23` "Atomically begins" is false already (`RedisCooldownGate.cs:26-29`). Fix it together with A6's `:16`.
- `AuthOptionsValidator.cs:52-54`, "boots a real Production host with the real NullEmailSender", becomes false once the stub is in place.
- `LoginCommandHandler.cs:60-66` counts "the other two" `CreateAsync` call sites; after R14 there are three.
- `RecordingEmailSender.cs:9` and `:21-22` (R15).
- ADR table :555-556 under (A): there is one live **code** challenge per address, and the budget reads "3/10 min caps mails, 10/24 h caps codes".
- D1's "GETDEL": the record is a hash with a counter inside it, so single use is `DEL` returning 1.

**F8 [Nice-to-have] Security's Q19: the five Production test hosts should reuse `RecordingEmailSender`**
- All five hosts are in the same assembly as the internal `RecordingEmailSender`, whose `CanDeliver` is true by default.
- A sixth `IEmailSender` implementation would be one more class to update for every future port member.
- There are four registration sites, because `HttpsRedirectionGateFactoryBase` covers both HTTPS hosts.

### Escalation to Klas
None. His (A) answer of 2026-09-19 is bound as given. R1 is what makes the promise he was given true, and it goes to security. F2 and F3 go to the CTO. Nothing here needs a new decision from him.

### References
- ADR 0142, at `C:/tmp/jbl-1735/docs/decisions/0142-passwordless-auth-one-page-code-or-link-oauth-ready.md`:
  - D1: :72-80, :94-110, :123-130
  - D2: :134-161
  - D3: :169-177
  - D10: :454-471
  - Attempt budget: :550-574
  - Page form: :604-642
- Security's form report: `C:/DOTNET-UTB/JobbPilot/docs/reviews/2026-09-18-1735-form-security-raw.md`
- AGENTS.md §2.1–§2.4, §3, §5 (`Backend:`, `Tests:`, `Comments:`), §7; CLAUDE.md §9.6, §11
- Main code files cited:
  - `C:/tmp/jbl-1735/src/Jobbliggaren.Infrastructure/Auth/RedisCooldownGate.cs`
  - `C:/tmp/jbl-1735/src/Jobbliggaren.Infrastructure/Auth/PasswordResetDispatchService.cs`
  - `C:/tmp/jbl-1735/src/Jobbliggaren.Infrastructure/DependencyInjection.cs`
  - `C:/tmp/jbl-1735/src/Jobbliggaren.Api/Program.cs`
  - `C:/tmp/jbl-1735/src/Jobbliggaren.Application/Common/Abstractions/ISessionStore.cs`
  - `C:/tmp/jbl-1735/tests/Jobbliggaren.Api.IntegrationTests/Infrastructure/ApiFactory.cs`