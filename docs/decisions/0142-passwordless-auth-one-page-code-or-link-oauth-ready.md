# ADR 0142 — Passwordless auth: one page, code or link, OAuth-ready

**Status:** Accepted for D1–D10 and the parts sequence · **D10's three questions answered by Klas on
2026-09-18** (mail outage = total login stop accepted, no break-glass, 5b deletes before launch — verbatim
under "Open — Klas decides") · **Part 1a delivered in two PRs, #1755 and the #1735 feature PR — the form as
delivered and the corrections it made are the amendment under D10** · **Date:** 2026-09-17 ·
**Deciders:** Klas (the directive 2026-09-16; the four answers 2026-09-17: consent as a checkbox in
its own step after the code, `/logga-in` as the single URL, provider buttons visible but inactive,
the CV name optional with no confirm step), `senior-cto-advisor` (D8 Variant B, D1 Redis, D5 (ii),
the parts order — `docs/reviews/2026-09-17-auth-epic-cto.md`), `dotnet-architect` (the ports and
signatures — `…-architect.md`), `security-auditor` (the attempt budget, the D4 legal basis, the
bindings under "Security bindings" — `…-security.md`), `design-reviewer` (the page form and the
seven code-step states — `…-design.md`)
**Related:** epic [#1732](https://github.com/klasolsson81/jobbliggaren/issues/1732) and its parts
#1733–#1747 · ADR 0017 (custom cookie auth; amended by this ADR: point 7's OAuth-readiness executes
through `AspNetUserLogins`) · ADR 0018 (cookie/CSRF; amended by this ADR: persistent-by-default and
the two dead `ADR 0093` pointers) · ADR 0012/0013 (Identity, separate `AppIdentityDbContext`) ·
ADR 0023 (the Worker has no `ISessionStore`) · ADR 0083 (`RegistrationsOpen` is the kill-switch) ·
ADR 0103 / 0124 (claim-then-send and the cooldown gate are the anti-abuse controls, never a
provider key) · ADR 0132 (the bearer-absence lapse trigger this ADR shares) · ADR 0137 (sv and en
are both product locales) · ADR 0071 (the CV engine stays deterministic; D7 touches only a
requirement) · #1494 (closed by the D4 analysis below) · #1484 (consent never recorded; closed by 1b)
**Measured against:** `C:/tmp/jbl-1733` at `081e4c67`, 2026-09-17; every `file:line` in the four
reports was re-run by the driving session before transcription.

---

## Context

Jobbliggaren's auth is ASP.NET Core Identity with a password, a required display name, a consent
checkbox whose value is discarded (#1484), an email-confirmation link, and opaque Redis sessions
(ADR 0017/0018). Two pages (`/registrera`, `/logga-in`), one "Håll mig inloggad" checkbox, and
re-authentication by password for change-email and delete-account.

Klas's directive of 2026-09-16: *"Vi ska göra om hela auth … Därför ska vi ha en auth sida, inte
2."* One page, OAuth buttons plus one email field, a one-time code or magic link instead of a
password, consent for a new address, no name, "Mina sidor", stay logged in on the same device, OAuth
wired last, *"så få klick och info som möjligt"*. The plan session of 2026-09-17 measured what
exists (the table in #1732), filed the epic and fifteen parts, and took Klas's four answers.

This ADR is part 0: the panel ratifies or amends the ten design decisions D1–D10 before any code
is written, and the later parts cite it. Three things it must carry that the epic could not:

1. **A reversal with conditions.** `DependencyInjection.cs:1658-1676` rejected Identity's
   6-digit "Email" TOTP provider as *"short-lived … and brute-forceable (10^6, stateless)"*. This
   ADR introduces a 6-digit code. The word that carried that rejection was **stateless**, and the
   reversal is a reversal of statelessness, not of the digit count (see "Attempt budget").
2. **The legal basis for persistent login by default.** ADR 0018's amendment of 2026-07-05 chose
   opt-in and cited an "ADR 0093" for the analysis; that number holds a different ADR and the
   analysis exists nowhere (#1494). D4 reverses the opt-in, so the analysis is owed here.
3. **The lapse conditions** for the attempt budget, which is sized for the product as it is today:
   not launched, two accounts, both Klas's, registration closed.

The panel found two epic-body claims false and they are corrected here rather than carried:
`proxy.ts:88-96` documents `Referer` leakage on a page-level `redirect()`, not the SameSite shape
D8 needs; and `IPasswordResetDispatcher.TryEnqueue`'s `bool` does not mean "queued" — under
`BoundedChannelFullMode.DropWrite` it returns `true` on a full queue
(`PasswordResetDispatchChannel.cs:64,83`).

## Decision

### D1 — Challenge, grant and OAuth state live in Redis behind Application ports

**Redis, TTL ≤ 15 min, behind `ILoginChallengeStore`; the consent stamp on `job_seekers` and
provider links in Identity's `AspNetUserLogins` are Postgres.** (CTO: the artefacts' whole
semantics is expiry; `INCR`-before-compare, `DEL` returning 1 on success and `SET NX` on claim are atomic
*because* of Redis; Redis is already the availability dependency of every authenticated request,
so a Postgres challenge would widen the failure surface, not narrow it; and under Art. 5(1)(e)
a self-expiring encrypted address is the data-minimising choice *(false as 1a delivered it; true from Amendment 2026-09-19 (2) on)*.)

**The port exposes the invariant, never the verbs** (architect, CLAUDE.md §2 axis 3):

```csharp
public readonly record struct ChallengeId;                        // SessionId's shape: 128 bit, Base64Url, Reveal()
public enum ChallengeOutcome { Verified, Wrong, Burned, Missing } // expired == Missing
public sealed record ChallengeVerdict { … }                       // factories only: Proof non-null iff Verified,
                                                                  // AttemptsRemaining in 1..MaxAttempts-1 iff Wrong

Task<IssuedCredentials> PutAsync(NewLoginChallenge challenge, CancellationToken ct);
Task<ChallengeVerdict> ConsumeCodeAsync(ChallengeId id, LoginCode presented, CancellationToken ct);
Task<LoginChallengeProof?> ConsumeLinkAsync(LoginLinkToken token, CancellationToken ct);
```

`ConsumeCodeAsync`'s contract, in its XML doc: the counter is incremented **before** the compare, by a
script that does nothing when the record is absent; the record is deleted on a hit and only a `DEL`
that returns 1 yields the proof; `Proof` is non-null **only** on `Verified`; a dummy compare is paid
even when no record exists; a record carrying no code answers like one whose code is wrong. Minting,
hashing, protecting and comparing live in the adapter. How `Wrong/Burned/Missing` are presented is the
handler's policy (see D3). `TryClaimAsync` is 1c's, with `complete`, its only caller, and it is a port of its
own, `IRegistrationClaim`, never a member of `ILoginChallengeStore` (Amendment 2026-09-20).

**`TryClaimAsync` is its own atomic `SET NX`** (`StringSet(..., When.NotExists)` on the registered
`IConnectionMultiplexer` *— from Amendment 2026-09-19 (2) on: `VolatileRedisConnection`*). It **never** reuses `ICooldownGate`: `RedisCooldownGate.cs:26-27` is
read-then-write and says so — a race there costs one extra mail; at `complete` it would cost two
accounts on one address. `RedisCooldownGate` is not changed. The claim is not the home of
uniqueness either: that is the UNIQUE index on the normalised USER NAME, which holds the address only
because both creation paths set `UserName = email`. `RequireUniqueEmail` is a validator that reads before it
writes, and the index on the normalised address is not unique (measured on the box 2026-09-20: `EmailIndex`
non-unique, `UserNameIndex` unique). So D3's registered-meanwhile arm handles the duplicate error even when
the claim was won.

**Record shape:** a Redis hash of two fields — `p`, the DataProtector-protected payload `{email, code,
linkTokenHash}` (`email` is the address the challenge is addressed to, D2; the code and the hash null where
the record carries none), and `a`, the attempt counter. No branch flag is stored: the mail is chosen when the record is written and the outcome is
resolved at proof time (D3). Expiry is the key's TTL.
**Threat model, chosen (security Major 2):** a Redis reader IS in scope. A 6-digit code has a
10⁶ preimage, so an unsalted hash protects nothing against the same reader the address is
encrypted against; therefore **the code is protected with the same DataProtector purpose as the
address**, and only the 128-bit link token is hashed, its hash inside the same protected payload. The
protected code is a confidentiality control and is written as one.
**Keys** follow the delivered cooldown form, versioned: `auth/challenge/v1/{b64url(sha256(id))}` and
its address index `auth/challenge-by-address/v1/{hex}` (1a), `auth/grant/v1/{b64url(sha256(token))}` and
`auth/registration-claim/v1/{hex}` (1c), `auth/challenge-bound/v1/{b64url(sha256(id))}` and its index
`auth/challenge-by-user/v1/{purpose}/{hex}` (3a), `auth/oauth-state/v1/{state}` (6a), `budget/{scope}/v1/{hex}`. No new root
segment beside `session:`; a record-shape change costs a new segment, never a decode crash on live
records.
**TTL 15 min** for code and link (one expiry state). **3 attempts, then the code is burned** — a state of
the code arm only: the record stays until its TTL and its link still signs in (Klas's (A),
security-auditor Q-S1). Code and link share one record, so consuming either removes both — but a wrong
link never consumes the code's three attempts (128 bits needs no attempt budget, and a POSTing scanner
must not burn the user's code), and a record for a subject without an account carries **no link**, so
`/auth/link` cannot succeed for one by construction (magic link for existing accounts only — the
defence is in the record, not in the mail's content).
**The cookie's address is an echo, never an input** (CTO bind 1; security Minor 4): `complete`
uses the proven address from the record. The pin is a test that sends a cookie whose address
differs from the record's and asserts the account is created on the **record's** address — the
reachable bug is a user who changed address in a second tab and would otherwise get an account on
the wrong one.
**`DataProtection:KeyPath` is measured set on the box before 1a goes live** (CTO bind 3). Today
`CreateProtector(` has zero call sites in `src/`; keys persist only when the path is set
(`DependencyInjection.cs:1609-1611`) and persist **unprotected** on the file system (no
`ProtectKeysWith*`), so whoever reads the keyring volume reads every live challenge's address and
code. A lost keyring degrades every live record to the uniform "expired" answer, which is the
right failure. `AddApiDataProtection`'s doc sentence "three token kinds" becomes false in 1a and is
corrected there.
**The mint budget is a counter, not a cooldown:** a small port `IRateBudget.TryConsumeAsync(RateBudgetScope
scope, string subject, ct)`, the scope carrying name, limit and window (Redis `INCR` + `EXPIRE NX` in one
transaction, one key per window, so no key ever exists without a TTL). **The address normaliser has ONE
home:** `RedisCooldownGate.Key`'s `Trim().Normalize().ToUpperInvariant()` is lifted to an internal
shared function that the shipped gate delegates to, and every new hashing site calls it (security
Major 3) — a copy that forgets `ToUpperInvariant` gives 2^k independent windows for one account
(U+017F / NFD, `RedisCooldownGate.cs:43-64`). The lift is pinned by the existing
`RedisCooldownGateTests` sweep, **extended to the new keys** in 1a. `ICooldownGate.cs:16`'s
"lower-invariant" is a wrong comment and is corrected in the same PR.

### D2 — The request path never reads the account

`POST /auth/challenge {email}` = `CanDeliver` → mint `ChallengeId` → **three `IRateBudget` scopes, each
consulted only when the one before admitted, none reading the account** (so they burn at the same rate
for known and unknown addresses): the silent per-address cooldown (a limit-1 scope over
`AuthEmailCooldownOptions.LoginChallengeWindowSeconds`), then the mail budget (refused → no record, no
mail), then the code budget (refused → the request goes on, and the mail carries no code — Klas's (A),
2026-09-19) → `ILoginChallengeDispatcher.Enqueue({challengeId, email, codeBudget, anonIp, ua})` →
uniform 202 `{challengeId}` for known, unknown, cooled and budget-exhausted alike.

**The dispatcher is a second port and a second channel, and it returns `void`** (CTO bind 2,
architect): `ILoginChallengeDispatcher { void Enqueue(LoginChallengeDispatch) }`, its own bounded
channel instance with its own capacity and its own drop log event-id, so a forgot-password flood
cannot silently drop logins now that mail is a hard dependency of login. `IPasswordResetDispatcher`
is untouched; the DRY lives in Infrastructure as an `internal abstract BoundedDispatchChannel<T>`.
No endpoint branches on an enqueue result — there is none.

**The consumer always writes a record** (otherwise "burned" vs "never existed" is an oracle), before it
sends, classifies the address (`LoginSubjectResolver`: no account, active, pending deletion, profile
missing) and sends at most ONE mail. **The record and the mail are addressed to the account's OWN stored
spelling when a row holds the submitted address, and to the submitted spelling otherwise** (Amendment
2026-09-21). An active account within its code budget → code + link; past it → link
only; no account → the closed-registration notice, no credential, in 1a — the
new-account code arm is 1c's (#1737); a missing profile → its record and no mail (Amendment 2026-09-20);
pending deletion → the restore path, no credential. Redis down →
uniform 503 through `StoreUnavailableException` (`Program.cs`, the shipped pattern).

**The consumer registers in the Api composition only** (ADR 0023): inside `AddIdentityAndSessions`,
never `AddCoreIdentityForWorker`. Two structural reasons, both measured: it needs
`IDataProtectionProvider` (only `AddApiDataProtection` registers one) and `ISessionStore`. **No
existing guard catches a mis-registration** — `WorkerLayerTests` scans the Worker assembly, the
consumer lives in Infrastructure, and the Worker runs `ValidateOnBuild = false`
(`Worker/Program.cs:50`). 1a adds the test pair from `AuthOptionsValidatorTests.cs:220-241` for the
new port and its hosted service: positive on `AddIdentityAndSessions`, negative on
`AddCoreIdentityForWorker`. The
residual — a hand-written line in `Worker/Program.cs` — is caught by nothing but a reader.

The flow's position lives in `__Host-jobbliggaren_login`, an httpOnly `SameSite=Strict` cookie set
only by Server Actions and never in a URL: in its code phase the `challengeId` and the address as
typed, for at most 15 minutes (Amendment 2026-09-21 (3) has the phases). The link path carries its
own token and never reads this cookie.

### D3 — Verify, then branch after proof

`POST /auth/challenge/verify {challengeId, code}`. **The cookie holder is told which of
`wrong` / `expired` / `burned` happened** (design B1, option (a)): the holder minted the challenge
themselves, a record was always written, so the distinction carries no account-existence
information — that branch stays hidden until the inbox is proven. `Missing` is presented as
`expired`, and expiry is not its only cause: a successful consume on either arm, a newer challenge the
code budget admitted, a cooled or over-budget request whose id never had a record, a dropped enqueue,
a verify that outruns the consumer, and a lost keyring all answer the same way. A dummy constant-time
compare is paid on every path. **Success is resolved at proof time, by one function the code and the
link share:** an active account whose own stored address is the proven one → `{outcome:"signedIn",
sessionId}`, a `Persistent` session; pending deletion → `{outcome:"pendingDeletion",
permanentDeletionDate}`; no account or a missing profile → `{outcome:"registrationClosed"}`, both without a
session (1a). A proof whose address is another spelling than the resolved account's own is answered like an
address that can neither log in nor register: `registrationClosed` while registration is closed
(Amendment 2026-09-21), `accountUnavailable` while it is open. 1c adds, while registration is open:
`{outcome:"consentRequired", grantToken}` for an address without an account proven by its CODE, and
`{outcome:"accountUnavailable"}` for a missing profile on either arm and for an address without an account
proven by a LINK (the table is in Amendment 2026-09-20).
`pendingDeletion` never restores the account through login — the 30-day clock is untouched, and
restoration is via support (`HardDeleteAccountsJob.cs:28-33`).

**Grants are ONE port with `purpose` as an enum, the bindings asserted inside `Redeem`**
(architect). `IssueAsync(GrantSubject subject)` returns a minted `GrantToken` (the adapter mints, protects and
fixes the TTL, as `PutAsync` does); `RedeemAsync(token, GrantAssertion expected)` does `GETDEL` and returns
`null` for unknown, expired, unreadable, wrong purpose and wrong binding alike, so no handler compares and none
can forget. A purpose whose whole job is to CARRY its binding — `LoginComplete`, whose proven address the caller
of `complete` does not hold (D1's "the record's address wins over the cookie's") — is redeemed with
`GrantAssertion.Bearer(purpose)`, which refuses any purpose not declared bearer-bound; every other purpose is
redeemed with `GrantAssertion.Of(binding)` and asserted by record equality. `GrantPurpose` shipped in 1c with its
one member, `LoginComplete = 1`; 3a added `Reauthentication(userId) = 2` and `ChangeEmail(userId, newEmail) = 3`. TTL
10 min, single use.

`POST /auth/challenge/complete {grantToken, acceptTerms}` → the kill-switch, before any Redis call → redeem
the grant → `TryClaimAsync`, AFTER the redeem (a loser still holding a live grant could retry into the winner's
account) → re-check existence through `LoginSubjectResolver` (registered meanwhile → the outcome function
answers for that account; the duplicate error is handled even when the claim was won) →
`CreatePasswordlessUserAsync` → `JobSeeker.Register(userId, TermsAcceptance, clock)` → one explicit
save of the profile and the `User.AccountCreated` audit row → the outcome function, so a `Persistent` session.
**No `AspNetUsers` or `job_seekers` row is written before the acceptance exists — on the code path
and on the OAuth path** (security Major 6): an external identity waits in the grant record and
expires with it if the user abandons the consent step. Holding `sub` + address before acceptance is
Art. 6(1)(b) second limb (steps at the data subject's request prior to a contract) and holds only
while nothing is written durably.

`POST /auth/link {token}` consumes the link and ends in the same outcome function. A session is
audited as today's login audits one, with the method (`Code`/`Link`) added to `login_succeeded`;
`login_challenge_issued` only for an address with an account, off the request path; a first proof of
an unconfirmed inbox also writes an `audit_log` row (D10).

**The challenge path never calls `IsLockedOutAsync` / `AccessFailedAsync`** — its anti-automation
is the 3-attempt burn plus the mint budget, never Identity's lockout. And **1c closes three holes the
password surface leaves open until 5a** (architect, security Major 11; the third bound by
`security-auditor` in 1c's pre-code form round, 2026-09-20): `ValidateCredentialsAsync` returns
`InvalidCredentials` on a null `PasswordHash` **before** `IsLockedOutAsync`/`CheckPasswordAsync`, paying
the timing equalizer (before 1c the failure was counted, and anyone who knew the address could lock a
passwordless account for 15 min); `TryPreparePasswordResetAsync` returns `null` for a null-hash user
(before 1c it would have given a passwordless account a password, making "passwordless" a starting
state rather than an invariant); and `ResetPasswordAsync` refuses a null-hash user with the uniform
token failure, so the invariant does not rest on every password-removal path rotating the security
stamp. Byte-identical responses; no new oracle.

### D4 — Sessions persistent by default

Every passwordless login issues `Persistent` (30 d sliding / 180 d cap / 24 h rotation,
`SessionStoreOptions.cs`); the cookie always carries Max-Age 180 d; the "Håll mig inloggad" checkbox
goes. **The effective default for an inactive device is 30 days, not 180**, and 30 is the number the
copy leads with. **The disclosure sits on BOTH steps that create a session** — `/logga-in/kod` (the
only step an existing account sees) and `/logga-in/villkor` — directly above the primary button,
`text-body-sm text-text-primary`, never in a footer and never behind a link (design B3):
*"Du förblir inloggad på den här enheten i upp till 180 dagar. Du kan logga ut när du vill, Logga ut
finns på varje inloggad sida."* Backend issues `Persistent` in 1a; `setSessionCookie(id, true)` and
the cookie-policy copy (`content-legal.json:330`) land together in **part 2**, and 1a does not claim
persistent-by-default is delivered (CTO's wording correction). The `Session` profile becomes dead and
is retired in a later PR; `Legacy` untouched. Logout stays the shared-device remedy.

**Legal-basis analysis (security-auditor's text, verbatim — this is what closes #1494):**

> **Frågan.** ADR 0018 `Amendment 2026-07-05` gjorde persistent login till ett opt-in med hänvisning till Art. 25(2) och till en "ADR 0093" som aldrig skrevs (#1494). D4 vänder valet till persistent-by-default på controller-beslut (Klas 2026-09-16). Grunden nedan är den som saknades.
>
> **1. Kakans existens har aldrig varit frågan.** Sessionskakan är *strikt nödvändig* för en tjänst användaren uttryckligen begärt — ePrivacy Art. 5(3)/LEK 6 kap. 18 § andra stycket, WP29 WP194 kriterium B (authentication cookies). Inget samtycke, ingen banner. Det som 2026-07-05 låg till grund för opt-in är **varaktigheten**: WP194 §3.2 undantar autentiseringskakan som *sessionskaka*, och behandlar en kvarstående "kom-ihåg-mig"-kaka som något som kräver en informerad, positiv handling av användaren.
>
> **2. Den positiva handlingen finns kvar, den har bytt form.** Under passwordless är själva inloggningen en aktiv handling per enhet (adress + kod ur egen inkorg). Kravet uppfylls om — och bara om — persistensen **uppges där handlingen görs**: kodsteget och samtyckessteget säger i klartext att man förblir inloggad på enheten i upp till 180 dagar och att Logga ut är kvar på varje sida. Detta är en argumenterbar position, inte en självklar; ADR 0142 ska skriva den som argumenterbar.
>
> **3. Art. 25(2) besvaras, inte kringgås — för att ändamålet har bytts ut.** 25(2) mäter nödvändighet mot ändamålet, inklusive lagringstid. Under lösenord kostade en ominloggning noll extra behandling, och 24 h var därför det minimerande valet. **Under D4 kostar varje ominloggning ett mejl med adressen genom biträdet (Scaleway, fr-par), en ny PII-post i Redis, och en klickträning på inloggningslänkar som ökar phishing-känsligheten.** En 24-timmarsdefault innebär ≈1 kodmejl per användare och dygn; 180 dagar innebär ≈1/180. Persistens är därmed **det dataminimerande valet under det nya ändamålet** — och det är precis den omvändning 25(2) kräver att man motiverar snarare än påstår.
>
> **4. Proportionaliteten bärs av levererade, mätta kontroller.** `Persistent` = 30 d **glidande** fönster + 180 d absolut tak + rotation av session-id var 24:e timme (`SessionStoreOptions.cs:39-43`); `__Host-` + HttpOnly + Secure + SameSite=Strict (`session.ts:88-93`); serverside-återkallelse med per-user-index och tombstone. **Den effektiva defaulten för en inaktiv enhet är 30 dagar, inte 180** — och 30, inte 180, är talet analysen ska föra fram.
>
> **5. Residualen, namngiven.** En användare på delad dator som inte loggar ut är inloggad i upp till 30 dagars inaktivitet, där hen tidigare loggades ut vid webbläsarstängning. Remedy är **copyn plus Logga ut på varje sida**, och copyn (`content-legal.json` rad 330 + inloggningssidan) landar i **samma PR som `setSessionCookie(id, true)`** (del 2, per CTO:ns formuleringsrättelse). Beslutet fattas under Art. 24(1) med risken utskriven; det är inte ett påstående om att opt-in var fel.
>
> **6. ADR 0018 rättas, inte bara upphävs.** Amendmentets **två** `ADR 0093`-pekare (rad 155 och 179) pekar på en fil som inte finns. ADR 0142:s 0018-amendment ska **stryka eller peka om båda** — ett upphävt beslut med död pekare till sin motivering är sämre än endera.

The position in point 2 is argued, not self-evident, and this ADR records it as argued. The two
numbers the 2026-07-05 amendment pointed at "ADR 0093" for are carried here: 180 days is the
absolute cap because it sits under CNIL's 13-month cookie ceiling (`SessionStoreOptions.cs`, web-check
2026-07-04), and rotation every 24 h is the condition under which a reach above 30 days was
accepted at all (security C3/COND-1, #481) — a captured token's replay window collapses to the
rotation interval.

### D5 — Re-authentication by code, not password: two codes, two inboxes

**Option (ii)** (CTO): a re-auth code to the **current** address (`GrantPurpose.Reauthentication`,
bound to `userId`) **and** a code to the new address (bound to `(userId, newEmail)`), plus the notice
to the old address as the detection channel. After D4 the session cookie is the only credential and
it lives 180 days; option (i) — one code to the new address — would let a stolen session repoint the
recovery vector to the attacker's inbox, which is permanent takeover with detection sold as
prevention. Option (iii) — re-auth plus today's DataProtector confirmation link — would keep two
inbox-proof mechanisms and leave #706 open. Between PR 3's deploy and PR 4's the delivered state WAS
(iii), transiently: a grant re-authentication in front of the old mailed link. PR 4 removed it; the
rejection stands on DRY and is not reopened by the interval.

Order: re-auth against the current address runs **first** and refuses before anything is minted.
`IReauthenticatingRequest.Password` is renamed to a purpose-scoped grant (`string? ReauthGrant`);
`ReauthenticationTripwireTests` pins nothing about the member name (its three assertions are the
marker, the validator, and the behavior's namespace — measured) and only its prose is corrected.
`ReauthenticationService.VerifyCurrentUserPasswordAsync` → `VerifyCurrentUserGrantAsync`; the
soft-delete gate (#1349) stays verbatim; the `EmailNotConfirmed` normalisation sentence is deleted,
not rewritten. The marker has THREE implementers, not two: `DeleteAccountCommand`, `ChangeEmailCommand`
and `ChangePasswordCommand`, on which `ReauthenticationTripwireTests`' name pattern forces the marker;
that one carries `ReauthGrant` BESIDE `CurrentPassword`, because Identity's `ChangePasswordAsync`
requires the argument, until 5a removes the surface. "Så få klick som möjligt" is the directive about the
login funnel; changing the recovery vector is rare and its failure is permanent — two inboxes is two
sends, which is arithmetic.

#### Amendment 2026-09-21 (4) (#1739, part 3a) — the form as decided, and the substrate as delivered

*Decided before code by `dotnet-architect`, `security-auditor` and `senior-cto-advisor`
(`docs/reviews/2026-09-21-1739-form-{architect,security,cto}.md`, local review files).* The sentences in D1, D3,
"Attempt budget" and "Implementation status" that the round contradicted were corrected in place; this block records
why. What PRs 3 and 4 deliver is written into this ADR when they deliver it.

**Four PRs, the order binding (CTO).** 1. The address-swap write order, alone and first, as a repair to delivered
code (#1790, below). 2. The substrate: purpose-bound challenges, the two grant purposes, the budget scopes. It is
inert when deployed, because nothing consumes it. 3. Re-authentication becomes a grant; from its deploy until 3b's,
the delivered frontend's delete-account, change-password and change-email will answer 400, because it still sends a
password. The box holds two accounts, both the controller's own, and registration is closed (measured 2026-09-21),
so no data subject meets that window; it is declared here and is not an accepted risk. 4. Change-email proves both
inboxes with codes and retires the mailed link. `POST /auth/verify` is to be retired in PR 3 (D9's migration rule
already named 3a for it). `IReauthenticatingRequest` has THREE implementers, not the two D5 names: `ChangePasswordCommand`
is the third, the tripwire's name pattern forces the marker on it, and it is to carry the grant beside the current
password until 5a removes the surface.

**The address swap takes the unique index first (#1790).** The unique index is on the normalised user name; the
e-mail index is not unique, `RequireUniqueEmail` reads before it writes, and login resolves an account by its
e-mail. `ConfirmChangeEmailAsync` wrote the address first and the user name last, and only logged a failed second
write, so two swaps racing to one address could both take it (`security-auditor`, Blocker against this part's form;
the race predates 3a). The user name is now written first and its refusal is fatal, and the mailed token is verified
explicitly before any write, because the user-name write rotates the security stamp. Measured: a real race reaches
the index and arrives as a `DbUpdateException`.

**A bound challenge carries the user id, in a key family of its own** (`dotnet-architect`). With no user id in the
record, the holder of a
hijacked session for a victim could present the challenge id and code of the attacker's OWN re-authentication,
proven in the attacker's own inbox, and be granted the victim's. So the record changes shape, and D1's rule applies:
a record-shape change costs a new segment. `auth/challenge/v1/` keeps its exact form, so a rollback cannot read a
bound record as a login record. The payload has no link field, so a bound challenge cannot carry one; each purpose
protects under its own sub-purpose; and the STORE compares the record's user id with the caller's. Another purpose
and another user are both answered `Missing`, never `Wrong`, because `Wrong` carries the owner's remaining attempts;
the attempt is spent either way, since the counter is incremented before anything is compared and the user id sits
inside the protected payload. The index is keyed on the user and the purpose, never on the address, so an anonymous
`POST /auth/challenge` for an account's address cannot burn its owner's re-authentication, nor the reverse. The port
gained two members beside the login challenge's three, whose signatures are unchanged; a third kind of challenge is
the signal to split the port.

**The budgets (security-auditor).** Under "same store, same budget" anyone who knows an address could
spend its ten daily login codes anonymously; past that budget the mail carries a link only (Klas's (A)), and a link
yields a session, never a re-authentication. The owner's account deletion (Art. 17) and address change (Art. 16)
would be blockable for a day by a stranger. So the cooldown and the code budget of a re-authentication are keyed by
the USER id, which only a holder of the session can spend. A per-user daily cap on new target addresses (5 / 24 h) bounds the
bounce surface the 60-second cooldown alone would leave at one made-up address a minute. `VolatileAclBudgetScopeParityTests` fails when the scopes the application declares and
the families in `deploy/redis/volatile.acl.template` differ.

**Lapse trigger 5 fires, and was re-run before first use.** `reauth-codes` is a new mint budget. Per account: at most
10 re-authentication codes per 24 h, 3 attempts each, 10⁻⁶ per attempt, so **0.003 %/day and 1.089 %/year**, the
login arm's own figures (`security-auditor`'s arithmetic, 2026-09-21). What differs is the attacker's position: the
guess is only available to someone who ALREADY holds a live session on the account, so the code is a second factor
on a sensitive operation and not a way in. The figure is arithmetic for an account under sustained attack from
inside its own session, not an observation.

**The acceptance line of #1739 that asked for "audit rows as today (`reauth_succeeded` / `_failed`)" rested on a
false premise:** no such event existed anywhere, and a failed re-authentication deliberately writes nothing, since
the behavior sits before the unit of work and the audit stage. PR 3 added two ops-log lines, written from the
service and never from the behavior, carrying the user id and the purpose and never an address, a code or a token
(`security-auditor`): `reauthentication_succeeded` and `reauthentication_failed`. `audit_log` rows were ruled out: a
failure row contradicts the behavior's placement, and a success row duplicates the operation's own.

**Re-authentication is a grant (PR 3).** Two authenticated routes, `POST /auth/reauth` and `POST /auth/reauth/verify`:
the first reads the account's own address from the session's user, never from the client, and answers only the
challenge id, which appears in no log line and no URL; the second presents the code with the binding
`(Reauthentication, userId)` and answers a grant, never a session — the handlers reach neither the outcome
function nor the session store (`ReauthenticationChainTests`). The gates on the request, cheapest first and each
spent only when the one before admitted: the sender's capability (503), the per-user cooldown (409), the shared
per-address mail budget (409 with the cooldown's own code, on purpose), the per-user code budget (409, terminal:
no link to fall back to), then the record and a synchronous send. The two per-user budgets are keyed by the user
id and never the address, pinned on the handler. `IReauthenticatingRequest.Password` became `ReauthGrant` on all
three implementers; `ReauthenticationService` takes `IGrantStore` in place of `IUserAccountService` and redeems
with `GrantAssertion.Of(new Reauthentication(userId))`, so it compares nothing itself; the grant payload is padded
to one ceiling for every purpose. **The declared window, and why it is a non-finding.** From this PR's deploy
until 3b's, the delivered frontend still sends a password on three flows — delete-account, change-email and
change-password — and each answers 400 (`ReauthGrant` binds null; the validator refuses before anything is
redeemed), never a false wrong-password claim. The box holds two accounts, both the controller's own, and
registration is closed; that reading was taken 2026-09-21 and re-taken at this PR, read-only on the box
2026-09-22T00:23:31Z: 2 rows in `AspNetUsers`, 2 the controller's; `Auth__RegistrationsOpen=false`, 1 line. No
data subject meets the
window, so it is a non-finding: no §9.6 (3) acceptance, no Klas grant, no signature. The three Playwright
tests of the delete flow were `test.fixme` naming #1740 until 3b's PR B (Amendment 2026-09-23 (6)).

**Change-email proves both inboxes (PR 4).** Three authenticated routes. `POST /auth/change-email` redeems a
re-authentication grant in the behavior, as before; the handler then runs, each gate spent only when the one before
admitted: the sender's capability (503), the per-user cooldown (409), the per-user daily cap on new addresses,
5 / 24 h (409, its own code), the per-address target cooldown and the per-address daily cap, 3 codes / 24 h, both shared
between users (409 with the user cooldown's code, so the caller is not told that someone else asked for the
address), the address storable (400) and free as an
address or as a user name (409 `Auth.EmailTaken`), then a record bound to `(ChangeEmail, userId)` and addressed to the
new address, and the `AddressChangeCode` mail, synchronously; the answer is 202 with the challenge id. The login arm's
per-address mail budget is not consulted, because the public login arm spends it anonymously before any lookup
(`security-auditor`). `POST /auth/change-email/verify` presents the code with that binding and answers a
`ChangeEmail(userId, provenAddress)` grant, never a session. `POST /auth/change-email/confirm` redeems it with
`GrantAssertion.Of(new ChangeEmail(userId, newEmail))`, moves the account through `SwapConfirmedAddressAsync` in
#1790's order, verifying no token of its own, notifies the old address, and then, as `/change-password` does,
invalidates every session and issues this device a fresh one with its lifetime. An address taken by then is 409
`Auth.EmailTaken`; an unusable grant is 410. `AddressSwapCallerTests` pins the three files that name the swap.

**The mailed link is retired, and the re-authentication request's mail-budget gate with it.** The public
`POST /auth/confirm-email-change`, the `/bekrafta-epost` page, `SendEmailChangeConfirmationAsync` and its template are
deleted, so the token in a query string that #706 tracked is no longer minted. `delete email` stays in the
Caddyfile's query filter for good: a link sent before the retirement can still be opened from an old inbox, and no
measurement can show that none is left (`security-auditor`). `CaddyfileTokenScrubbingPinTests` holds it as a retired
parameter. The re-authentication request no longer consults the per-address mail budget either (`security-auditor`,
a Minor against PR 3, routed into this PR by `senior-cto-advisor`): anyone who knows the address could spend it
anonymously and hold the owner's re-authentication off. The two change-email `cd/` cooldowns left the persistent
Redis and its ACL. The privacy policy said the new address gets a confirmation link; it now says a code, and
`privacy.updated` and `TermsAcceptance.CurrentPrivacyPolicyVersion` moved together (`security-auditor`).

**Lapse trigger 5 fired again, and was re-run (PR 4).** Two mint budgets changed. Re-authentication lost the
per-address mail cap; `reauth-codes`, 10 / 24 h per user, was already the binding one, so the figures stand at
0.003 %/day and 1.089 %/year. The change-email code is new, and a correct guess would attach an address whose owner
never read the code to the guesser's account. Per new address, whoever asks, at most 3 codes per 24 h, 3 attempts
each: 9 guesses a day, so 0.0009 %/day and 0.328 %/year however many accounts guess (`security-auditor`, PR 4's
panel). Arithmetic, re-taken 2026-09-22, not an observation.

### D6 — The consent record: a contract stamp, not Art. 7 consent

`TermsAcceptance` is a Domain value object (`JobSeekers/TermsAcceptance.cs`, a `sealed record`,
with the version constants and one total factory, `AcceptCurrent(IDateTimeProvider clock)` —
corrected 2026-09-17, amendment below). `JobSeeker.Register(Guid userId, TermsAcceptance acceptance,
IDateTimeProvider clock)` (its name parameter left in 4a's PR B, D7) **replaces** the current signature — not an
overload, so the consent-less path stops being callable (§2.2); the cost is one call site in `src/`
(`RegisterCommandHandler.cs`) plus every test call site, swept by the compiler in one mechanical
commit and counted in the PR body with `git grep -c 'JobSeeker\.Register(' -- src tests` plus the calls split before `.Register(`,
`git grep -n -B1 -E '^\s*\.Register\(' -- src tests` read for `JobSeeker`. Mapped as
`OwnsOne(...)` + `Navigation(...).IsRequired(false)` →
three nullable columns `terms_accepted_at`, `terms_version`, `privacy_policy_version` on
`job_seekers` (parity with `Preferences`; not `ToJson` — an Art. 7(1)-grade record is queryable).
Nullable only for the two pre-existing rows; **NOT NULL for every row written after 1b** is a
property the write paths must hold, not a factory convention (security Major 6).

**Naming is deliberate:** `terms_accepted_at` is right — this is contract formation (Art. 6(1)(b)),
not consent in the Art. 7 sense, and `consent_*` would imply an Art. 7(3) withdrawal right that does
not exist. `privacy_policy_version` stamps an **Art. 13 notice version** for Art. 5(2)
accountability, never an acceptance fact: the checkbox accepts **the terms**; the privacy policy is
linked as read in a sibling sentence, never "godkänner … och integritetspolicyn".

**The versions live in Domain** as constants on `TermsAcceptance` (Domain reads no files; the
aggregate is the only thing that can refuse an old version). The value is the ISO date already in
the copy. Parity with `messages/{sv,en}/content-legal.json` (`terms.updated`, `privacy.updated`) is
pinned by a test in the form of `ContactAddressMatchesPublishedContactTests` — walk to the repo root,
parse the prose, assert it **ends with** the Domain constant, both locales.

**The Art. 13 notice moves with the collection point** (security Major 8, design Minor 1): the
address is collected on `/logga-in`, so the same link line the `/registrera` checkbox carries today
sits under the email field as a second hint row — *"Så behandlar vi din e-postadress:
integritetspolicyn."* — no new separate notice.

#### Amendment 2026-09-17 (#1736, part 1b) — the form as delivered, and three corrections above

*Decided by `dotnet-architect` (`docs/reviews/2026-09-17-1736-architect-form.md`) and
`senior-cto-advisor` (`docs/reviews/2026-09-17-1736-cto-signature.md`), both before code.*

**Three sentences in D6 were corrected in place today, and this records why.** (1) The value object is
a `sealed record`, not the `readonly record struct` first written: `OwnsOne` requires a reference type,
so the two halves of the sentence could not both hold, and D6's own "parity with `Preferences`" ground
— `Preferences` is a `sealed record` — picks the class. (2) The factory is **total**:
`TermsAcceptance.AcceptCurrent(IDateTimeProvider clock)` stamps the two Domain constants at the clock's
now. No request in this epic carries the version the user saw — registration and the code-step
`complete` both send a bool — so a `Result`-returning `Create(termsVersion, privacyPolicyVersion,
acceptedAt)` would ship with one caller passing the constants and a failure branch no caller reaches;
it is **deferred to the part that first carries a version, as scheduling, not omitted**. The known-set
refusal has nothing to refuse until then. (3) The cost sentence counted `src/` and called the test side
one file; every test call site is swept, by the compiler, in one mechanical commit, and the count is a
PR-body fact with its regeneration command rather than a number frozen here.

**Also bound.** `Register` refuses a null acceptance with `JobSeeker.TermsAcceptanceRequired` after the
`userId` guard — the non-nullable parameter is defended at runtime the way `Guid.Empty` already is.
The API refuses `acceptTerms=false` in `RegisterCommandValidator` (`Equal(true)`, no default on the
command member, so an omitted field binds to `false` and is refused): the validation behavior runs
before the handler and so before `CreateUserAsync`, leaving no Identity user behind and reading nothing
but the flag. The aggregate's refusal and the validator's are two propositions, not one rule in two
homes. `JobSeekerRegisteredDomainEvent` carries no versions: **the row is the Art. 5(2) accountability
record**, the event announces that a registration happened. The three columns are all-or-nothing at
the database (`ck_job_seekers_terms_all_or_none`, `num_nonnulls(…) IN (0, 3)`): the optional owned
mapping reads "no stamp" from all three NULL, and the constraint holds that sentinel where raw SQL
lives (security-auditor, PR #1751). When the terms change, re-acceptance is recorded **append-only** —
a row per acceptance — never by overwriting these three columns; the shape is named here so the first
terms bump does not reach for the overwrite that would delete the earlier Art. 5(2) evidence. The
parity pin lives in Domain.UnitTests
with the walk-up shared through `tests/Shared/ContentLegalMessages.cs`; it reads `terms.updated` and
`privacy.updated` by path, never by a file-wide sweep — the file carries five `updated` dates.

**The signature conflict with the issue bodies is resolved for this ADR.** #1736's body and #1741's
body wrote `Register` without `displayName`; both were filed before this ADR was ratified. The ADR
governs and the bodies were corrected on 2026-09-17: `DisplayName` stays required and validated until
4a, which also owns the fate of the parameter and of the event's non-nullable `DisplayName`. *(From #1737's
second PR on, 2026-09-21: the EXPAND half moved into 1c — see D7. From 4a's PR B on, `Register` takes no
name and the event carries none: Amendment 2026-09-23 (7).)*

### D7 — The account has no name and the CV stops requiring one

`Resume.FullNameRequired` is removed from `Create`/`ValidateContent`; auto-promote maps `null`;
B3's canonical arm stops grading a name the system never stores; the `PersonnummerInAccountName`
arm becomes unreachable; the parsed contact name is still never used (the 2026-07-16 bind stands).
Nullable-first (4a), drop (4b). The avatar becomes a neutral account icon labelled "Mina sidor"
(form in "Page form").

**The expand half ships with 1c, not 4a** (`senior-cto-advisor`, 2026-09-20; Klas the same day: "Ja – egen
liten PR 2"). `complete` carries no name and `JobSeeker.Register` refused a blank one, while 4a was sequenced
after 1c, so the epic's own order could not be built. 1c's second PR therefore makes
`job_seekers.display_name` nullable (migration `DisplayNameNullable`), lets `Register` store an absent name
while a given name still runs `ValidateDisplayName` inside the aggregate, makes the event's and
`JobSeekerProfileDto`'s name nullable, and makes the FE profile read tolerate `null`. At 1c `ValidateDisplayName`
still refused an absent name, so the password path and `UpdateDisplayName` were unchanged. Everything in the
paragraph above stays 4a's. Until 4a, an account without a name cannot promote an imported CV: the gate
answers `IncompleteContent`, and the only copy the user is shown for it tells her to complete the entries
in the file and upload it again, while the file is clean. 4a closes
that; it is a #734 launch condition, written into #734's table (row 7, 2026-09-21) and into #1741.

#### Amendment 2026-09-22 (#1741, part 4a) — the CV half, and how 4a ships

4a ships in eight PRs (`senior-cto-advisor`, #1741's form round; the ruling is #1741 comment 5783718789, and its
follow-ups added DX and DX2; PR B's form round added B0, #1741 comment 5796245153):
**A0** the frontend's read schema accepts an absent name, merged first so that a revert of the writer cannot
leave the CVs it wrote unreadable; **V1** the preamble's hard cut never ends inside a digit run; **DX** a DOCX line
break ends a line and a tab separates its neighbours, a Major security-auditor found in V1's review, merged
before A; **DX2** (#1801) a DOCX text box starts its own line and an inline object separates its neighbours, a
Major security-auditor found in DX's review, merged before A; **A** the CV half
below, merged once V1 is merged and A0 is live; **RP** the verbatim period strings join the personnummer guard's
field list, independent of the rest; **B0** the frontend's profile read accepts a payload without the `displayName` key, merged and live
before B, since the release builds each image in its own cell and the box can run a new api beside an old web;
**B** the account half, after 3b (#1740). No part of 4a carries a migration:
the CV content is the encrypted `content_enc` shadow, and `display_name` has been nullable since 1c's second PR.

The CV half, as delivered:

1. **The token is retired in two steps.** The backend member `AutoPromoteBlockReason.PersonnummerInAccountName`
   is deleted in A. The frontend's zod member and its copy follow in B, since they sit in 3b's
   files, so between A and B the frontend accepts a superset of what the backend sends (the
   `UnclassifiedPreamble` precedent).
2. **The DQ6 guard stays, and a hit reports `PersonnummerPresent`.** D7's "unreachable" applies to the token,
   not to the guard: the gate runs today's scanner over a parse whose stored scan outcome is the import-time one,
   so a widened scanner can flag an older parse. Every `CreateFromParsed` caller must also call the guard (the
   architecture tripwire).
3. **V1 is closed at its source, as a precondition of A.** The preamble's hard cut at `MaxPreambleChars` could
   end inside a digit run that the whole-text scan had rejected, and leave a personnummer in the carrier that the
   import scan never saw. With point 2, that would have reported a personnummer the file does not hold as one.
4. **The review stops grading a name on the canonical arm.** B3 neither misses nor claims a name there. B1 reads
   the contact section each arm detected (the linearizer's on the canonical arm, the parse's contact confidence
   on the staging arm) instead of `FullName || Email`.

**#734 row 7 is met by A** (Klas, 2026-09-22: "Uppfylld efter PR A"), measured live by a NEW import from an
account without a name, since auto-promote runs only in the import request. The 4b gate below still covers all
of 4a, B included.

#### Amendment 2026-09-23 (7) (#1741, part 4a, PR B) — the account half, and the corrections above

*Decided in PR B's own form round: `dotnet-architect`, `security-auditor` and `design-reviewer`, then
`senior-cto-advisor`, who decided without escalation (#1741 comment 5796245153).* The sentences above that B
made false were corrected in place; this block records why. With B, all of 4a is merged.

**What the account half removes.** `JobSeeker.Register(Guid userId, TermsAcceptance acceptance,
IDateTimeProvider clock)` takes no name, and `JobSeekerRegisteredDomainEvent` carries none. `UpdateDisplayName`,
`ValidateDisplayName`, `MaxDisplayNameLength` and the three `JobSeeker.DisplayName*` codes went with their last
callers; so did the password path's `RegisterCommand.DisplayName`, its validator rule and its pre-check.
`UpdateMyProfileCommand` takes the language only, `JobSeekerProfileDto` carries no name, and the web neither
reads nor shows one. The `/oversikt` kicker is removed rather than rewritten to the address (`design-reviewer`:
a mono, uppercase, 11 px address fails WCAG 1.4.10 at 320 px and the 14 px floor, and the shell's Mina sidor
popup already shows it). The frontend's `PersonnummerInAccountName` left with it (point 1 above). The
personnummer scan on the name left in the same commit as the last writer, never before it (`security-auditor`).
The `DisplayName` property and its EF row left in 4b's first PR, one deploy before the column (Amendment
2026-09-24 (8)), and the one test seam that writes a legacy name (`tests/Shared/LegacyAccountName.cs`) names
`JobSeekerTests.DisplayName_HasNoWritePathOnTheAggregate` and the retired actors. The
password path's frontend (`RegisterForm`, `registerAction`) stays until 5a (C1) and sends a key the endpoint
ignores.

**B0 went first** (#1816). A new api beside an old web is a reachable state: `release-images.yml` builds each
image in its own matrix cell and the box runs `:latest`. So the web's profile read accepted a payload without
`displayName` before the backend stopped sending it, and B's `agents-done` waited until B0's web image was
measured running on the box.

**Copy follows data, refined** (`senior-cto-advisor`, Q2 (c); the strings are `security-auditor`'s): a mention
of data follows the data, and a mention of a purpose follows the purpose. B strikes "Visningsnamn" from the
privacy policy's purpose sentence, since its purpose, the greeting, ends here; `privacy.updated` and
`CurrentPrivacyPolicyVersion` moved to 2026-09-23 together. The stored-data sentence keeps "visningsnamn" and
the Art. 30 register keeps `display_name` until 4b drops the column.

**Legacy CVs: no scrub.** CVs auto-promoted before PR A carry the account name in their encrypted content and
render it. The set is closed, since nothing has written the name into a CV since `69983af5`, and a scrub would
rewrite CV content without a propose-and-approve diff (AGENTS.md §5). **The reading**, read-only on the box,
counted and never printed, 2026-09-23T12:49:27Z: 6 `resumes` rows with `origin = 'Import' AND created_at <
2026-09-23T08:47:47Z`, 1 live and 5 soft-deleted, each with its auto-promote audit row, none created after
PR A, all the controller's. The selector is the origin and the date, never the audit event, whose partitions
drop after 90 days. A soft-deleted CV keeps its content until the account is deleted, which is #1528.

**From B's deploy to 4b: a declared non-finding, not an acceptance** (no §9.6 (3), no Klas grant, no
signature). (a) No writer of `job_seekers.display_name` exists after B, whatever the #734 flip does. (b) **The
reading**, taken fresh at the end of B's content, read-only, counted and never printed, 2026-09-23T16:26:27Z:
`identity."AspNetUsers"` 2 rows, both the controller's; `job_seekers` 2, both named; 0 named rows whose
account is not the controller's; `Auth__RegistrationsOpen=false`, 1 line and the only one. This block is its
home. (c) Any non-null `display_name` whose account is not the controller's stops B and returns this entry to
`security-auditor`. A matched legacy name keeps B5's flow, a human with the holder in the loop; no domain
method nulls it, since that would add a writer in the PR that removes the last one. C2 (Amendment 2026-09-22
(5)) was about the CV gate reading the name; it closes at B's deploy as written.

**DoD 8.** No new personal data: B removes the name's collection, write and display. No new logging.
Retention unchanged until 4b drops the column. No DPIA.

**The 4b gate** (Klas, 2026-09-23): B counts as measured live when Klas himself has checked /oversikt and
/mina-sidor behind basic_auth. The session's read of the box is necessary, not the gate.

#### Amendment 2026-09-24 (8) (#1742, part 4b) — the drop ships as unmap, then drop

*Decided in 4b's form round: `db-migration-writer`, `security-auditor` and `dotnet-architect`, then
`senior-cto-advisor`. 4b opened on the gate above: Klas checked /oversikt on 2026-09-23 and /mina-sidor on
2026-09-24 himself.*

**4b is two PRs, a Parallel Change.**
- **U** removes `JobSeeker.DisplayName` and its EF row through the migration `UnmapJobSeekerDisplayName`. Its
  `Up`/`Down` are empty, so the snapshot forgets a column that stays in the database.
- **D** drops the column with a hand-written `DropColumn` (`DropJobSeekerDisplayName`). The recruiter-erasure
  search over the column goes in the same PR, together with the privacy copy and the runbooks:
  they describe the data, so they follow it.

**Why two PRs: the release pipeline, not the column.**
- `release-images.yml` publishes each image in its own matrix cell, and the box's reconcile does not check
  that its images come from one commit (#1238).
- So a new `migrate` beside an old api or worker is a reachable state. The old model would select the dropped
  column: 42703 on every `JobSeeker` entity load, while `/api/ready` stays green.
- After U, neither skew can fail a `JobSeeker` load. This is Amendment (7)'s B0 → B ruling applied to migrate
  versus api/worker.

**Rejected:**
- One PR with a dispatch-and-verify window: it detects the mixed state and prevents nothing.
- Pinning `IMAGE_TAG` on the box before the merge: a root operation standing in for a design step, on the lever
  #1759's hold also uses.
- Keeping the property mapped but inert (`PropertySaveBehavior.Ignore`): a mapped property is still selected.

**Measured for the choice, 2026-09-24**, with a minimal fixture on local Docker Compose v5.5.1 (the box runs
v5.4.0, not re-measured):
- When every image changes, compose stops the old api before the new migrate starts.
- When only migrate's image changes, the old api keeps running while migrate re-runs.

**D merges only when all three hold:**
1. api's and worker's `latest` digests equal their `sha-<X>` digests for an X at or after U's merge, with
   attestation verified.
2. U is measured live on the box, Klas's own /mina-sidor load included. This is waived while #1759's hold
   is active; U and D then ride its release together.
3. Klas has given the merge GO for the irreversible migration.

**Rollback.** From U on, a pin to an older tag makes `migrate` refuse (exit 3, `vps-deploy-stack.md` §3a),
unless `MIGRATE_ALLOW_SCHEMA_AHEAD` names the exact set of ids it refuses. From D on, the override cannot bring back code older than U: its model selects the dropped column (above). U's tag with the override is the only rollback by tag, and on it the recruiter-erasure search fails with 42703.

**Klas answered two questions from the round on 2026-09-24.** He chose from AskUserQuestion options, and each
question was quoted to him verbatim first.
1. **B5.** `security-auditor` offered: "**(b)** B5 tas bort, och en träff i användarnas profiluppgifter
   besvaras med B2. B2:s text ändras inte." or "**(a)** B5 står kvar med bara de två osanna delarna
   strukna …". Klas: "(b) B5 bort, B2 svarar". D deletes B5 and routes `matched.jobSeekerProfiles > 0` to B2.
2. **#1759's hold.** `dotnet-architect` asked: "Får 4b:s kolumndrop, som inte går att backa med en tagg, ingå
   i den release som din fortsättnings-GO för #1759-holdet deployar? Eller ska 4b vänta med merge tills
   holdet är hävt eller avskrivet?" Klas: "4b får ingå i releasen". The hold was not active when he
   answered.

**DoD 8.** No new personal data: 4b removes a stored category. No new logging. Row versions and WAL are not claimed erased, and nothing is said about backups (STOPP-4). Legacy CVs keep the name in their encrypted content (Amendment (7)). No DPIA.

### D8 — OAuth hand-rolled behind a port, last: Variant B

`POST /auth/oauth/{p}/start` and `POST /auth/oauth/{p}/callback {code, state}` exchange via
`HttpClient` behind `IExternalIdentityProvider` (Google OIDC userinfo, GitHub `/user/emails` with
`primary && verified` **only** — `/user`.`email` is the public profile field and may be unverified,
LinkedIn OIDC userinfo). **What userinfo buys is no JWKS fetch, no key-rotation cache and no
signature-validation path of our own** — a real reduction of security-critical surface. (The
epic's "so no JWT package" is struck: `Microsoft.AspNetCore.Authentication.JwtBearer` already sits
in Infrastructure as the package that gives it its framework reference, ADR 0017 amendment
2026-07-25.) No ASP.NET remote-auth handlers: they own the callback URL and set correlation cookies
on responses that must reach the browser, which ADR 0018's trust model forbids — Variant A is a
topology change needing a new CSRF analysis, not a code saving. Variant C (Arctic/oslo or an
Auth.js adapter in Next) moves the client secret and the identity assertion into the transport
layer, so Next becomes the authority over who the user is; rejected on the dependency rule.

**Contract:** `ExternalIdentity(string ProviderKey, string Subject, VerifiedEmail? Email)` with
`readonly record struct VerifiedEmail(string Value)` — "provider asserts verified" is carried by
the **type**, so no caller can read the string and forget the bool. Every adapter parses the
verified claim **fail-closed** (LinkedIn returns `email_verified` as a string in some responses;
missing or unparsable = not verified). Linking to an existing account by email happens ONLY on a
`VerifiedEmail`, else refuse. PKCE `S256` only. `redirect_uri` is **not** a parameter — the adapter
builds it from `EmailOptions.BaseUrl`, the one home of the public base URL (CTO bind 3; no
`OAuth:RedirectBaseUrl`). `GET /auth/oauth/providers` = the registered `IExternalIdentityProvider`s'
keys; a provider without keys is not registered — fail-closed, no flag. Links via Identity
`AspNetUserLogins` (ADR 0017 point 7 amended); first sign-in goes through the consent step
(security Major 6).

**Next route handlers.** `GET /api/auth/oauth/{p}/start` → 302 to the IdP; **initiated only by an
`<a href>` navigation, never a `<form>` or a Server Action** — `form-action 'self'`
(`security-headers.ts:74`) is enforced across redirects by Chromium and Firefox, so a form submit
whose 302 targets `accounts.google.com` is blocked by CSP; pinned in 6a's Playwright test.
`GET /api/auth/oauth/{p}/callback` answers **200 + a document with a "Fortsätt" continuation, never
a 302**: the session cookie is `SameSite=Strict` (ADR 0018's cookie table), and a cross-site-initiated
redirect chain does not carry it. **The state cookie is mandatory, not complementary** (security
Major 5): `__Host-` `SameSite=Lax` ≤ 10 min — Lax because the callback arrives as a
cross-site-initiated top-level navigation and a Strict cookie would not be sent; the callback
**refuses** when the cookie is missing or does not match, never falling back to the Redis record
alone (login CSRF: an attacker completes their own IdP flow and feeds the victim the callback URL).
The cookie binds the *browser* to the flow; the Redis record (`GETDEL`) is the lookup key for the
PKCE verifier, provider and `next` — two roles, not double storage.

**Provider marks:** none while the buttons are inactive (Amendment 2026-09-21 (3)). Marks, and
whether the official coloured ones get a scoped DESIGN.md §3 exception, are 6a's and Klas's (see
"Open — Klas decides").

**Privacy policy and Chapter V, in the same PR as the first live provider** (security Major 10):
the IdPs enter "Mottagare" as **independent controllers, not processors** (Art. 13(1)(e) with source);
the third-country sentence (`content-legal.json:101`) is rewritten; Chapter V is answered **per
provider with a dated measurement** (EU establishment and/or Art. 45 adequacy, CLAUDE.md §9.5) in
the processing register; a provider that is not covered does not ship; Art. 49 derogations do not
apply to a login path.

### D9 — Test harness first (part 0.5)

`RegisterAndGetSessionIdAsync` becomes a service-level bootstrap in the factory (passwordless user +
`JobSeeker.Register` + `ISessionStore.CreateAsync(Persistent)`); the 187 call sites change
mechanically in one PR with zero behaviour change. It can be built today against
`IUserAccountService.CreateUserAsync`, `JobSeeker.Register` and `ISessionStore.CreateAsync`, so it
has no dependency on 1a and lands first (CTO: SRP at PR level — a 187-file refactor must land
against green main with no semantic change in flight). The bootstrap stays in the test assembly and
never becomes an `IDev*` port. 1b touches it once (the new `Register` signature).

#### Amendment 2026-09-17 (#1734, part 0.5) — two bootstraps, routed by subject

*Decided by `senior-cto-advisor` on a measurement: `docs/reviews/2026-09-17-1734-harness-form-cto.md`.*

**Measured 2026-09-17:** with the passwordless shape applied to every call site, the integration suite
went red and further tests passed vacuously, all of them on password, address-confirmation or
`Session`-profile subjects. D9's "zero behaviour change" for every call site and "built against
`IUserAccountService.CreateUserAsync`" cannot both hold with "passwordless user": that port takes a
password, and a test whose subject is a password cannot run on an account without one.

**The form.** Two service-level bootstraps in the test assembly share one core (`JobSeeker.Register`
→ save → `ISessionStore.CreateAsync(userId, lifetime)`), and neither issues an HTTP call:
`RegisterAndGetSessionIdAsync` produces D10's account shape (`UserManager.CreateAsync(user)` with
`EmailConfirmed = true`, no password) with a `SessionLifetime.Persistent` session;
`RegisterWithPasswordAndGetSessionIdAsync` produces the shape of the flag-OFF branch of
`RegisterCommandHandler` (`IUserAccountService.CreateUserAsync(email, password)`, address
unconfirmed, `SessionLifetime.Session`).

**Routing rule.** A test class uses the password bootstrap when its subject is a password surface
this ADR removes (password login, `/auth/verify`, change-password, forgot/reset-password, lockout,
breached password, the password re-auth on `/me/delete` and change-email, confirm-email-change) or
the `Session` profile. Every other class uses the passwordless one. Membership follows the subject,
never whether a class went red: a vacuous pass is green.

**Migration rule.** A class moves to the passwordless bootstrap in the PR that removes its password
or `Session` premise (3a for `/auth/verify` and `/me/delete`, 5a for the rest). In 3a `/auth/verify` is
RETIRED, not moved — the query, its handler, validator and tests are deleted, since the frontend never
called it — and `DeleteMeTests` moves with its lockout oracle rewritten as grant-refusal parity: an
unknown grant, a spent one, another user's and its owner's afterwards answer one byte-identical 401,
every premise produced by production. The password
bootstrap, `DefaultTestPassword` and `LoginAndGetSessionIdAsync` are deleted in the PR that moves the
last class, which ties them to the surfaces rather than to a part number.

**Pin.** `SessionBootstrapTests` runs both bootstraps against the registrations-closed host, whose
`/auth/register` refuses (`RegistrationsClosedTests` is the counterfactual), and asserts that the
session authenticates there and that each account shape holds (password hash, `EmailConfirmed`,
lifetime).

**Later parts.** The PR that introduces D10's `CreatePasswordlessUserAsync` replaces the passwordless
bootstrap's direct `UserManager.CreateAsync(user)` with it; 1b changes the shared core's one `Register` call. The
`.gitleaks.toml` entry matches the password value, which test files outside this part carry; it
goes when the last literal goes, not in 0.5.

### D10 — The dev seam, the mail dependency, and the boot gate

`CreatePasswordlessUserAsync(string email, ct)` → `Result<Guid>` with the same duplicate collapse
as `CreateUserAsync`; `userManager.CreateAsync(user)` (no password overload) with
`EmailConfirmed = true` and `UserName = email` set before the call; no password validators run, so
`PwnedPasswordValidator` is deliberately not engaged. Its home is the narrow port
`IPasswordlessAccountCreator`, with the compensating delete beside it, never `IUserAccountService`: `complete`
is part of the proof chain, which must not reach the password surface (Amendment 2026-09-20).

**The dev seam is `POST /api/v1/dev/login-code {email}`, mapped in `MapDevEnvironmentOnlyEndpoints`
and registered by `AddDevOnlyTestingSupport`** — the `IsDevelopment()`-only pattern, **never**
`MapDevResetMyDataEndpoint`'s configuration-gated one (security Blocker 1: `DevTools:EnableResetMyData`
is on on the box since 2026-08-29; the route hands out a live login credential for an arbitrary
address, unauthenticated, so the wrong gate is a total auth bypass). No flag may widen it.
`ProductionStartupSmokeTests` covers the route in **both** polarities of
`DevTools:EnableResetMyData`: an explicit 404 with the flag absent, and the universally quantified
route-table test with it on. The capturing decorator calls
**`ConsoleEmailSender.IsReservedRecipient`** (the same member, never a copy) and captures nothing
for a non-reserved recipient (404), size- and TTL-bounded, holding the **code**, never the body
(#1208's gate is not reopened one layer up). The code is minted with `RandomNumberGenerator` and
rejection sampling — never `Random`, never `% 1_000_000`.

**Mail is a hard dependency of login.** The delivered boot refusal (outside Development/Test when the
registered sender cannot deliver) **drops both its `RegistrationsOpen` and its
`RequireEmailConfirmation` conjuncts** (security Major 12): after 1a mail is needed for login, not only
registration. The rule keeps asking the sender's **capability** (`CanDeliver`), never the
`Email:Provider` key. The budget parameters are constants in `LoginChallengePolicy`, never
configuration; `Auth:LoginChallengeDispatch:Capacity` and
`Auth:EmailCooldown:LoginChallengeWindowSeconds` are range-validated options with code defaults, so
neither is a key a fresh dev boot needs and CLAUDE.md §11's contract is not triggered. Both existing
accounts have `EmailConfirmed=true` and log in by code with no data change. An account whose address
is unconfirmed is confirmed by its first passwordless proof, and in the same Identity write its
password is removed and its security stamp rotated; its earlier sessions are revoked before the new
one exists, and a `User.InboxProvenByLogin` `audit_log` row is written (security-auditor Q21/Q-S3).

#### Amendment 2026-09-19 (#1735, part 1a) — the form as delivered, and the corrections above

*Decided before code by `dotnet-architect` (`docs/reviews/2026-09-19-1735-form-architect.md`),
`security-auditor` (`…-form-security.md` and the scoped `…-form-security-qs.md`) and
`senior-cto-advisor` (`…-form-cto.md`), and by Klas on security's escalation.* The sentences in D1, D2,
D3, D10, "Attempt budget", "Page form", "Processing register", "Consequences" and "Implementation status"
that the form contradicted were corrected in place today; this block records why.

**Klas's answer on the budget drain, and the reorder it rests on.** `security-auditor` (Q18) found that
D2's budget, in any gate order, lets anyone who knows an address spend its code budget and — with no
break-glass — keep the owner out for most of a day, every day. Klas, 2026-09-19 (#1735, comment
5737130338): **(A), build the protection in 1a.** Past the code budget an existing account's mail
carries a link and no code; a record is written for every admitted request, known or unknown, and a
record with no code answers Wrong, then Burned, exactly as a wrong code does; the gates run cooldown →
mail budget → code budget, each only when the one before admitted; and a budget key never exists without
a TTL. The link is a 128-bit bearer credential with no attempt budget, so the owner always holds a link
under ten minutes old or can request one. security's condition (iii) stays a named Minor: once
registration opens, a new address's registration can still be drained, with no account or data at
stake.

**Lapse trigger 7 fired and was re-run before first use.** The budget branch changed behaviour: over
the code budget is no longer a no-op, and the counter now counts admitted mints only. security's re-run:
still at most 10 code-bearing mints and so 30 guesses per address per 24 h, **0.003 %/day and
1.089 %/year — identical**; trigger 5's three quantities are unchanged. Trigger 7 fired a second time on
PR #1756 (security-auditor Major 2): mails to addresses without an account are capped globally at 20 per
24 h (`LoginChallengePolicy.UnknownAddressMailBudget`, pinned by `LoginChallengePolicyTests` and
`LoginChallengeIssuerTests`), and above the cap such an address gets its record and no mail, so the
resting copy's premise does not hold for it. Part 2 (#1738) re-binds that copy with design-reviewer.

**Outcomes at proof time; the 1a/1c line (CTO Q1).** The mail is chosen at issue time and the outcome at
proof time, by one function the code and the link share, so an account deleted or a kill-switch thrown
inside the 15 minutes is honoured. 1a never mints a code for a subject without an account, whatever
`RegistrationsOpen` says.
1c adds the open-registration arm in one piece.

**"Missing is shown as expired" (CTO Q2(a)).** D3's *"only expiry removes it"* was false and is replaced
by the list of causes. The hint *"Har du redan begärt en kod nyss kan det vara den som gäller"* and
Page-form state iv are deleted: both were false in every case they were written for. Two Minors from
`security-auditor` are accepted as named residuals: an **address-activity signal** (a no-record
"expired" tells a prober the address was submitted within the cooldown or is over budget), and a
**conditional existence signal** (a prober holding a challenge for an existing account sees "expired"
early if the owner clicks the link in the mail — it needs the owner's click, and the mail alerts them).

**Constants and options (CTO Q3).** The budget is policy: `LoginChallengePolicy` holds code length,
attempts, TTL and both windows as constants, pinned to this ADR's literals with trigger 5 in the failure
message, because a value an env var can move would lapse the acceptance with no PR. Capacity and the
cooldown window are `[Range]` options with code defaults; neither is a key a fresh boot needs, so
CLAUDE.md §11 is not triggered, and D10's sentence saying it was is corrected above.

**Faults (CTO Q5, architect R12).** Redis faults become `LoginChallengeStoreUnavailableException` *(superseded by `VolatileRedisUnavailableException`, Amendment 2026-09-19 (2))*, a
`StoreUnavailableException` that carries the inner exception's type name only — never the exception,
whose message can hold a key and so an address fingerprint — and the Api answers one fixed 503 body. The
reach is the three new routes; the cooldown moved onto `IRateBudget` so its faults are translated too. A
payload that cannot be unprotected or read (a lost keyring) is `Missing`, or no proof on the link path,
logged with its exception type.

**The address index (security Q-S1, Q-S2).** The index swap is keyed on the request path's code-budget
decision, never on whether the record carries a code: keyed on the code, whether an earlier challenge was
burned would tell a prober whether the address has an account. Records minted past the code budget are
not indexed, so a link-only record never burns the live code challenge. **Named residual (Minor):** up
to 8 records can be live for one address inside 15 minutes, at most one of them code-bearing — more
single-use links exposed to scanners and forwarding, still under the reset path's rate.

**The first inbox proof (security Q21/Q-S3).** For an account with `EmailConfirmed=false`, the first
proof sets the flag, removes the password and rotates the stamp in one Identity write; a write that did
not persist throws and nothing follows it; earlier sessions are revoked before the new one is created;
and a `User.InboxProvenByLogin` `audit_log` row is written. Only `PasswordlessSessionGrant` can reach
that write, and only the two proof handlers can reach the grant — pinned by reflection. **Residual (Minor):** a squatter's password on an account its
owner already confirmed survives a code login until 5b. Nobody can be in that position while
registration stays closed. **If the #734 flip happens before 5b, this becomes a Major at the flip.**

**Majors 11 and 12 under "no break-glass".** Major 12 is delivered here: the boot refusal asks
`CanDeliver` alone outside Development/Test, and the five Production test hosts register a delivering
fake so a developer's `Local.json` can neither refuse their boot nor make them send. Major 11 stays in
1c as D3 binds it. `security-auditor` confirms both dispositions in her review of this PR.

**The dev seam.** Captured by a decorator over the composed sender, for reserved recipients only, the
code only, once, for at most the challenge's lifetime, bounded in size; composed by one internal
extension whose one production caller runs under `IsDevelopment()`. Every integration host replaces the
sender, so the production wiring is pinned on the unswapped composition in both environments. The
dev-seam types are named `Dev*`/`IDev*`, and `release-checklist.md` §2.7's teardown grep finds them by
that name.

#### Amendment 2026-09-19 (2) (#1735, part 1a-store) — the challenge's keys move to a Redis that cannot persist

**What was wrong as 1a delivered it (security-auditor, Major 1 on #1756).** D1 placed the challenge
record, the address index and the budget keys on "Redis", and 1a composed them on the registered
`IConnectionMultiplexer`: the durable instance, which runs an append-only file. There an expired key
stays in the file until the next rewrite, so the TTL was not the key's whole lifetime. The
closed-registration mail tells its recipient how long the address is kept ("Därefter finns den inte kvar
hos oss") and the processing register said the same; both were false against the file. No recipient has
been sent that sentence: the registration gate is closed and the route has no caller before #1738
(security-auditor, 2026-09-19).

**Decision.** The keys move; the sentence is not rewritten. A second Redis instance, `redis-volatile`
(`redis-volatile-dev` in the root compose), holds them, and nothing it holds can reach a disk:

- `--save ""` and `--appendonly no` write nothing by themselves, but both are runtime-mutable. Measured
  on 8.6.5 and 8.10.1, 2026-09-19: any client on the network can `CONFIG SET appendonly yes`. So the
  MOUNT carries the guarantee: `CONFIG SET dir` is a protected config, `/data` is therefore Redis's only
  write path, and it is a sized tmpfs under a read-only root. There is no `volumes:` key, and the image
  declares no volume.
- In the deploy stack `mem_limit >= 2 x maxmemory + the tmpfs size`, with `memswap_limit = mem_limit`.
  The dataset cannot reach a disk through swap, and a flood meets Redis's own `noeviction` refusal before
  the cgroup's SIGKILL.

The form is pinned by `DeployComposeVolatileRedisTests`. What the form DOES is measured by
`VolatileRedisPersistenceProbeTests` and `VolatileRedisOutOfMemoryTests`, on a container built from the
compose file's own fields.

**The class on the volatile instance: auth keys whose whole lifetime is their TTL.** Today that is the
challenge record and the address index (15 minutes) and the rate budgets (their windows, at most
24 hours). D1's `TryClaimAsync` (1c) and the OAuth state (6a) join them when they land: where D1 says "the
registered `IConnectionMultiplexer`", it means `VolatileRedisConnection` from this amendment on. Sessions
and `RedisCooldownGate`'s remaining surfaces stay on the durable instance and are not changed here
(#1757).

**How code reaches it (dotnet-architect F1–F6).** `VolatileRedisConnection` is internal and owns a
PRIVATE multiplexer that is never registered as `IConnectionMultiplexer`: an unkeyed second registration
is last-wins and would move every session onto an instance that forgets them at a restart.
`ExecuteAsync` is the only route to the database.
`AbortOnConnectFail = false` with an eager connect; a `Lazy<T>` would cache a first failed attempt for
the life of the process. Its consumers are exactly the two stores and the readiness check *(from Amendment
2026-09-20 on: those two, the grant store and the registration claim)* (`VolatileRedisIsolationTests`), and which instance each key class lands on is pinned through the
production entry point (`VolatileRedisPlacementTests`). The connection string is
`ConnectionStrings:VolatileRedis`, not `RedisVolatile`: `ConnectionStrings__Redis` would be a strict
prefix of that, and the compose pins scan lines. The Api refuses to boot without the key in every
environment. The refusal enforces where the keys live, not rollout discipline, and an exemption for an
environment no host runs would be a way round it. The Worker composes neither store and needs no key.
`/api/ready` gains the check `redis-volatile`.

**Faults.** The first 2026-09-19 amendment's "Redis faults become
`LoginChallengeStoreUnavailableException`" is superseded. The type is
`VolatileRedisUnavailableException`, named for the instance: a rate budget is not a challenge store, and
the instance is what an operator looks at. `StoreUnavailableException` carries `Store`, and the 503 log is
`StoreUnavailableLog`, `event_name=store_unavailable store=… inner_type=…`, throttled per store. Two
instances are two failure domains, and one shared window would let either outage hide the other's first
entry. The old event name had no consumer outside the log class and its tests.

**Measured, R6 (2026-09-19).** At `noeviction` OOM
`transaction.ExecuteAsync()` THROWS `EXECABORT` rather than returning `false`, both stores
answer the translated fault, and no key is left without a TTL. The branch dotnet-architect bound for the
other outcome (assert the bool) does not apply.

**Two corrections above.** D1's ground "under Art. 5(1)(e) a self-expiring encrypted address is the
data-minimising choice" and the rejected-Postgres ground "PII moved from volatile to durable" were both
false as 1a delivered them, for the reason in the first paragraph. They hold from this PR on, for the
class named above and for no other key.

**Security-auditor's position on lapse trigger 5, VERBATIM from her pre-code form round (F9, 2026-09-19):**

> **Lapse trigger 5 and the volatile store (security-auditor, pre-code form round, 2026-09-19).**
> Moving the challenge, index and budget keys to a non-persisted Redis instance does **not** fire trigger 5: code length, attempt count and the mint budgets are unchanged as parameters, so the arithmetic above — 30 guesses per address per 24 h, 0.003 %/day, 1.089 %/year — is re-affirmed unchanged. What changes is the **premise under the 24-hour window**, and it is written here rather than left to be rediscovered.
> A restart of `redis-volatile` resets every budget counter — cooldown, mail budget, code budget and the global unknown-address cap — because the instance holds no AOF and no RDB. The 24-hour window is therefore an **uptime window**, and the arithmetic reads "per uptime window", not "per day", for as long as a restart can be caused by anyone but the operator. Three causes, measured on `redis:8.6-alpine` = 8.6.5 on 2026-09-19:
> 1. **Operator-caused** — a deploy, an image-pin move, a host reboot. Not attacker-timed, infrequent, and accepted: the reset returns budget the operator did not spend.
> 2. **cgroup OOM-kill, attacker-caused through the API** — a flood that pushes the container past `mem_limit` is SIGKILL plus `restart: unless-stopped` plus a fresh counter set: the attacker-chosen budget reset that `noeviction` exists to exclude. **This cause is removed by sizing, and the sizing is a condition of the acceptance, not a tuning choice:** `mem_limit ≥ 2 × maxmemory` with the tmpfs size counted inside it, and `memswap_limit = mem_limit`. Delivered as `maxmemory 64mb` · `tmpfs /data:size=16m` · `mem_limit 160m` · `memswap_limit 160m`. Sized this way the instance meets the flood at the Redis level — measured: `INCR` is refused at queue time with `OOM command not allowed`, `EXEC` answers `EXECABORT`, the transaction is discarded whole, no key is created, and a pre-existing budget key keeps its value and its TTL — so the counters survive the flood that was meant to clear them.
> 3. **A client on the internal bridge** — measured: an unauthenticated peer on the same Docker network answers `ACL WHOAMI` = `default`, lists `budget/*`, and `SHUTDOWN NOSAVE` restarts the container with no memory pressure at all, after which `DBSIZE` = 0. **Sizing does not close this cause and nothing in this PR does.** It is parity with the durable `redis`, which has held every session on the same terms since ADR 0122, and it belongs to #1759 (Redis identities, ACLs, command allowlists), which waits for this PR. Until #1759 lands, the budget guarantee — and the retention guarantee this store exists to make true — hold **against the internet, not against a compromised container on the stack network**. Recorded here because "the TTL is the whole lifetime" is otherwise read as unconditional.
>
> **Trigger 5 gains one operative clause:** it fires if a restart of `redis-volatile` becomes reachable by anything other than the operator — a `mem_limit` that no longer satisfies the relation above, or a bridge that gains a member outside the stack's own services. Both are measurable at the compose file; neither is a judgement call.
> **Not a new trigger, and deliberately not grafted onto trigger 5:** a flood that fills the instance to `maxmemory` refuses login writes (503) until the keys expire, at most 24 h, with sessions untouched. That is an availability loss under Art. 32(1)(b), not a change to the guess arithmetic. It is re-measured when `POST /auth/challenge` becomes reachable without basic_auth, together with the Scaleway bounce reading.

**A third residual the text above does not carry (security-auditor, review of #1773).** A client on the
bridge can `CONFIG SET appendonly yes` against the tmpfs. The file then lands in the RAM-backed `/data`,
and an expired key's payload stays there for the container's lifetime: never on a disk, and gone at a
restart. `VolatileRedisPersistenceProbeTests` measures exactly this. It is an incident surface against a
compromised container (Art. 33), not a false transparency statement, and like cause 3 it belongs to
#1759.

**Bound on #1759 (security-auditor F15, condition 2).** `redis-volatile` is to require AUTH and deny the
`api` identity the command classes that reset or persist its state — at least `CONFIG`,
`BGSAVE`/`SAVE`/`BGREWRITEAOF`, `SHUTDOWN`, `DEBUG`, `FLUSHALL`/`FLUSHDB`, `KEYS` — and #1759 says how
`aof_enabled:0` stays verifiable once those ACLs land. Once the connection string carries a credential,
the compose pin asserts its host:port part, never the whole string.

**Rollout (senior-cto-advisor, option (b)).** `release-images.yml` builds hourly on a schedule and the
box applies hourly, so a merged PR reaches the box with no dispatch, and `web` waits on a healthy api: an
api that refuses boot is the whole site down. The session can order what reaches the box, not time it.
The compose ships first (#1773). The PR that makes the key required is not armed until
`jobbliggaren-redis-volatile` is measured healthy on the box, after Klas's GO for the `git pull` there.

#### Amendment 2026-09-20 (#1737, part 1c) — the open-registration arm as delivered, and the corrections above

*Decided before code by `dotnet-architect` (`docs/reviews/2026-09-20-1737-form-architect.md`), `security-auditor`
(`…-form-security.md`) and `senior-cto-advisor` (`…-form-cto.md`, with one scoped ruling `…-cto-username.md`).* The
sentences in D1, D2, D3, D10, Amendment 2026-09-19, Amendment 2026-09-19 (2), "Attempt budget" and
"Implementation status" that the form or its review contradicted were corrected in place; this block records why.

**Three PRs (CTO), and two more the work found.** The password-surface gates (#1777) repair a surface 1a delivered
and share no type with the arm. The display name becoming optional (#1782) is a schema relaxation on a shipped
invariant and is reviewed alone. The arm is the third. Between them came two repairs to delivered code, each in its
own PR and each with its own amendment below: the challenge follows the account's own address (#1779), and what may
be stored as an address (#1781). A no-caller prep PR for the grant store and the claim was rejected for the third
time in this epic: a primitive with no caller ships its semantics untested.

**What D3 could not be built as.** `IssueAsync(purpose, subjectKey, payloadProtected, ttl)` asked the caller to
protect the payload, and Application has no DataProtection package; it asked the caller to type the lifetime a
second time; and `RedeemAsync(…, expectedSubjectKey)` asked `complete` for an address it must not supply, since D1
binds that the record's address wins over the cookie's. The port is `IGrantStore` with a closed `GrantSubject` and
a `GrantAssertion` whose `Bearer(purpose)` factory refuses every purpose not declared bearer-bound, so 3a's two
purposes stay caller-asserted by record equality. Each purpose protects under its own sub-purpose of
`Jobbliggaren.Auth.Grant.v1`, so a payload issued for one cannot be opened as another's. `security-auditor`'s
condition that no handler compares is met by `RedeemAsync` answering `null` for every refusal.

**The grant key is a hash of the token (security-auditor, Major against D1's literal `{id}`).** The token is the
whole bearer: `complete` takes nothing else. With the literal form, whoever can list the keyspace reads live tokens
out of the key names, past every DataProtector. Measured on the box 2026-09-20: `redis-volatile` runs
`user default on nopass ~* &* +@all` with no `aclfile`; the ACL template is test-only until #1759's second PR. For
the challenge record the hashed key is defence in depth; for the grant it is the only thing between a key listing
and a session.

**The claim** is its own port, `IRegistrationClaim`: on `ILoginChallengeStore` the proof-chain walk would hand it to
the verify and link handlers. Key `auth/registration-claim/v1/{hex}`, the constant value `1`, 60 seconds, never
released, and taken AFTER the grant is redeemed. The loser answers exactly like a replayed grant, so nothing says an
address is being registered. A restart of `redis-volatile` drops live claims with everything else; two completions
straddling one can both win, and Identity's unique user name is what then refuses the second.

**Outcomes, total and explicit (CTO).** `LoginProofOutcome` takes the method and the kill-switch:

| Subject | Method | Registration | Outcome |
|---|---|---|---|
| `Active` | any | any | `signedIn` |
| `PendingDeletion` | any | any | `pendingDeletion` |
| `NoAccount` / `ProfileMissing` | any | closed | `registrationClosed`, as 1a |
| `NoAccount` | `Code` | open | `consentRequired` + grant |
| `NoAccount` | `Link` | open | `accountUnavailable` |
| `ProfileMissing` | any | open | `accountUnavailable` |

A proof whose address is another spelling than the account it resolves to (Amendment 2026-09-21) is answered before
this table, by the same switch: `registrationClosed` while registration is closed, `accountUnavailable` while it is
open. Open, that tells whoever holds the colliding inbox that the address folds onto an account
(`security-auditor`, Minor, accepted: `consentRequired` there would be the squat of #1780).

A link proves an address without an account only when the account went away inside the challenge's lifetime, and no
new account rises from a bearer that has sat in a browser's history. `ProfileMissing` is never adopted and never
replaced: the row is an in-flight sibling registration or the residue of a failed hard delete, and the two cannot be
told apart at the proof. `registrationClosed` would have been the smaller diff and a false statement while
registration is open. An Identity row without a profile is written its record and mailed nothing, in either
registration state (`security-auditor`, PR #1783, M-2), for as long as the orphan sweep leaves the row — between
1 h (`AccountHardDeleter.OrphanGraceWindow`) and about 25 h (the job runs daily at 04:00 UTC), measured 2026-09-20.

**The cap is consulted before the record is written (security-auditor, Major).** In 1a the order was harmless,
because a record for an address without an account never carried a credential. In 1c it would mint a live,
account-creating code above the global cap: no mail, no signal to anyone, three guesses per record, for every
address anyone names. For `NoAccount` the issuer now asks `UnknownAddressMailBudget` first, and
a refused record carries `ChallengeCredentials.None`. `ReplacesLiveChallenge` still follows the request path's
code-budget decision alone, so a capped record displaces the address's live challenge exactly as a
closed-registration record did in 1a: the index decision must not read the account.

**Lapse trigger 7 fired and was re-run before first use (security-auditor).** Per new address: at most 10
code-bearing mints and so 30 guesses per 24 h, **0.003 %/day and 1.089 %/year — identical to 1a's.** What is not
identical is the stake. A guessed code on a new address now yields a grant, an account and a `Persistent` session
that no first-proof revocation removes, because an account created here is born confirmed; and the attacker
chooses the denominator, since any address will do. The cap-before-record order is what bounds it again: at most
20 code-bearing records per 24 h for all new addresses together, so 60 guesses per day against the whole surface,
and an attack that spends the cap stops every registration, which is visible. **`UnknownAddressMailBudget` is
therefore one of trigger 5's quantities from this amendment on.** `RegistrationClaimTtl` and `GrantTtl` are not.
Trigger 7 fires once more with the record-only arm above: for an Identity row without a profile the consumer sends
no mail. That record carries no credential, so the arithmetic does not move, and the resting copy #1738 re-binds
has to hold for it as for a capped record.

**The two mails reach recipient class (3) whenever the resolver found no account.** Both carry the whole Art. 14
notice in every send, shared with the closed-registration mail through one block each. The
retention paragraph of `NewAccountCode` is conditional, in `security-auditor`'s wording: the address is kept
protected for the challenge's lifetime, for the grant's lifetime more if the code is used, a fingerprint for the
code budget's window, and as the account's address if the account is created. The closed mail's "Därefter finns
den inte kvar hos oss" is false there and true in `NewAccountCodeLimitReached`, which carries no code and so leads
to no grant, no claim and no account. Every duration in the copy is derived from the constant that enforces it.

**`complete`.** The kill-switch is the first statement and precedes every Redis call, so a closed host reaches
neither new key family; that is what makes 1c deployable before #1759's ACL is live on the box. One `audit_log`
row, `User.AccountCreated`, written on the create arm only and committed with the profile in one explicit save: the
outcome function reads the profile back from the database, and the unit-of-work behavior saves after the handler.
No terms row: the three `job_seekers` columns are the Art. 5(2) record (D6), and the creation row already
timestamps the acceptance. The acceptance is refused in the validation behavior, so a request without it leaves
the grant usable.

**The address the account is created under is the PROVEN one**, out of the grant, and it passes
`StorableAddress.IsStorable` in `CreatePasswordlessUserAsync` like every other writer of a stored address
(Amendment 2026-09-21 (2)); `StoredAddressWriterGuardTests` fails without it.

**What 1c hands to the `RegistrationsOpen` flip (#734), after Klas's answers of 2026-09-20.** Conditions, all in
#734's table: 3a (#1739) first, since a passwordless account can neither delete itself nor change its address until
re-authentication stops asking for a password ("(a) 3a före #734"); 4a (#1741) first (D7); the accepted residual of
#1780 is read first. One more follows from this block: the cap on mails to addresses without an account is
registration's ceiling, 20 new addresses per 24 h for the whole service, and twenty made-up addresses stop every
registration for a day. **Not conditions, by his word:** a session list or an "account created" mail against a guessed
code on a new address ("för hårt säkerhetstänk"), and Redis AUTH with the ACL live on the box ("Nej inga onödiga
blockers.").

**Accepted risk (CLAUDE.md §9.6 (3)): an account created on a guessed code.** Granted by Klas Olsson 2026-09-21
("Ja, skriv in acceptansen"); signed by `security-auditor` 2026-09-21, in her scoped re-check of PR #1783 against
this text. The finding stands as she graded it, a Major (form round, M-C): a guessed code on an address without an
account yields an account and a `Persistent` session that no first-proof revocation removes, since an account
created here is born confirmed, and whoever later registers on that address logs in to the same account and is
told nothing. The acceptance withdraws the remedy only: neither discharge she named is built. It does not make the
risk measured. The 1.089 %/year above is arithmetic for an address under sustained attack, not an observation, and
whether anyone would attempt it is not known.

The bound: the only data subject whose position is affected is the controller himself, or none at all. Measured:
`Auth__RegistrationsOpen=false` on the box, and `complete`'s first statement is that switch
(`LoginChallengeCompleteTests.With_registration_closed_even_a_live_grant_creates_nothing`), so no account can be
created by this arm; `AspNetUsers` / `job_seekers` = 2 / 2, both the controller's own (`security-auditor`,
read-only, 2026-09-20 11:30Z; the driving session, read-only, 2026-09-21 06:52–06:55Z — her own reading that day
was refused by the tool, so the later figure is the session's).

**It lapses the day `Auth__RegistrationsOpen` is set to `true`, or the first account that is not Klas Olsson's is
created.** Home: #734's condition table. Reader: Klas Olsson. Nothing detects the lapse automatically.


#### Amendment 2026-09-21 (#1737) — the challenge follows the account's own address

**What was wrong as 1a delivered it (security-auditor, Blocker BA-1, 2026-09-20).** D2 said the consumer
classifies the address and sends at most one mail. It never said to which address, and 1a mailed the
submitted spelling. `LoginSubjectResolver` finds the account through Identity's lookup normaliser, which
applies NFC and upper-cases, and that lands four non-ASCII BMP code points on printable ASCII — U+017F on
`S`, U+212A on `K`, U+037E on `;`, U+1FEF on a backtick (a sweep of
`UpperInvariantLookupNormalizer.NormalizeEmail` in the shipping runtime, 2026-09-21). A challenge requested as
`ſam@…` therefore resolved `sam@…`'s account and carried its code and link to the other inbox, and the proof
resolved the record's typed spelling to the same account: a `Persistent` session for whoever receives mail
for the colliding spelling on the account's own mail domain. It did not depend on `RegistrationsOpen`.

**R1.** The consumer addresses the challenge, the record and the mail both, to the account's own stored
spelling when a row holds the submitted address, and to the submitted spelling otherwise: the rule
`TryPreparePasswordResetAsync` already applies. `ILoginAccountLookup.FindAccountAsync` returns the stored
address with the id, the three account-bearing `LoginSubject` variants carry it under `KnownAccount`, and
`NewLoginChallenge.Email` is `Recipient`. A login typed in another letter case so proves the stored spelling
and signs in as before.

**R2.** `LoginProofOutcome` refuses, before its table and on all three account-bearing arms, a proof whose
address is not ordinally the resolved account's own: no session, no
deletion date, and a Warning (event 1016) carrying the user id and the method, never an address. It is the
one place in the chain that applies no normalisation.

**Unchanged:** the by-address index and every budget key (both spellings share one `SubjectFingerprint`),
the audit line and the two issuer log lines (none carries an address), and the dev capture.

**Lapse triggers, read for this change and confirmed by security-auditor 2026-09-21: none fires.**
1: `RegistrationsOpen` is untouched. 2, 3: no account is added. 4: no IdP. 5: code length, attempts and
mint budget are unchanged, and the guard runs after the consume. 6: not 5b. 7: only the mail's recipient
changes, and the budget branch is untouched. The processing register
needs no edit: its sentence that an address with an Identity row belongs to recipient class (1), the
address on the account, was false for a folded spelling and is true after R1.

**What this change does not close (security-auditor MA-1, Major; its ground corrected 2026-09-21).**
`AllowedUserNameCharacters` validates the user name, never the address. She measured that Identity's
`SetEmailAsync` admits an address with leading whitespace, and `ConfirmChangeEmailAsync` is the delivered
writer that reaches it; Identity keeps such a row apart from the unpadded one, while
`SubjectFingerprint.Hex`, which trims, gives both one key. The storable-address predicate closes it (R3: no
control character, no `Cf` format character, no surrogate, no whitespace) at every writer of a stored
address: `CreateUserAsync`, `ConfirmChangeEmailAsync`, and 1c's `CreatePasswordlessUserAsync`. It ships with
the user-name charset change in the PR after this one. Klas, 2026-09-20: this repair in its own PR first
("(b) Egen PR först"), and `björn@…` and `o'brien@…` shall be registrable ("Ja").

#### Amendment 2026-09-21 (2) (#1737) — what may be stored as an address

**The user-name charset is empty.** The user name here IS the address (`UserName = email`), and Identity's
default `AllowedUserNameCharacters` is ASCII, so `CreateUserAsync` refused addresses both email validators
admit (`o'brien@…`, `björn@…`) with Identity's English `InvalidUserName` text, and `ConfirmChangeEmailAsync`'s
user-name sync failed for them and was swallowed, leaving the old address in `UserName`. Klas, 2026-09-20:
such addresses shall be registrable. `senior-cto-advisor` ruled the charset off in its own PR rather than a
second list of letters that drifts from the validators.

**R3, and it closes MA-1.** That charset never validated the address, only the user name. What may be stored
is one predicate, `StorableAddress.IsStorable`, beside `SubjectFingerprint`: no control character, no `Cf`
format character, no surrogate, no whitespace. A predicate over character classes, not a charset, so it bans
every astral character (deliberate). It is asked at every writer of a stored address (`CreateUserAsync`,
`ConfirmChangeEmailAsync`; 1c's `CreatePasswordlessUserAsync` joins them) and, to refuse before a dead token
is minted, at `GenerateChangeEmailTokenAsync`. Never on the request path of the login challenge, which stores
nothing, and never in the two validators. The refusal is `Auth.EmailNotStorable`, in Swedish. In the writer
behind the public confirm endpoint it is judged before the account is read: it is a property of the
submitted spelling alone, so unlike that endpoint's uniform rejections it cannot vary with the user id.
`StoredAddressWriterGuardTests` sweeps the source: no address reaches Identity outside `UserAccountService`,
and every write there follows the check in its own method.

**Accepted residual (security-auditor, Minor; #1780).** Identity's normaliser lands more than one spelling on
one account key, and the spellings are made of visible characters, so they are storable: the four folding
code points, and a decomposed (NFD) spelling of an accented address (test-writer, 2026-09-21). Whoever
registers `ſam@…` first holds `sam@…` out: Identity's unique normalised user name refuses the other
spelling, whose holder can then neither register nor log in. No session and no data cross (R1, R2). The
squat must precede the victim's account and needs mail delivery on the victim's own domain.
`StorableAddressPortTests` measures both mechanisms through real Identity. **It is inert while registration
is closed and becomes reachable at lapse trigger 1:** the `RegistrationsOpen` flip reads this paragraph and
#1780, which is public, before it is made.

**Lapse triggers, read for this change by security-auditor 2026-09-21: none fires.** 1: `RegistrationsOpen`
is untouched. 2, 3: no account is added. 4: no IdP. 5: code length, attempts and mint budget are unchanged.
6: not 5b. 7: the login challenge's request path and its budget branch are untouched.

**Identity's English description pass-through** (`CreateUserAsync`'s non-duplicate arm): its two measured
triggers, an address with an embedded CR LF (`a\r\nb@…`) and one with a trailing LF, are refused earlier by
the predicate. No reachable trigger measured after R3; not verified unreachable.

#### Amendment 2026-09-25 (10) (#1743, part 5a) — the teardown as delivered, and the corrections above

*Decided in 5a's form round: `security-auditor`, `dotnet-architect` and `test-writer`, then `senior-cto-advisor`
(`docs/reviews/2026-09-25-1743-form-{security-auditor,dotnet-architect,test-writer,cto}.md`).*

**5a is two PRs, web first.** W (#1844) took every caller off the password routes; B deletes the routes and
what stood behind them. The image cells have no fan-in (#1238), so an old web beside a new api is reachable,
and web first makes that state a web with no caller. Either order fails closed.

**Derived scope.** `POST /auth/register` is a password route too, so B deletes it with the other six (login,
change-password, verify-email, resend-confirmation, forgot-password, reset-password) and with
`/dev/confirm-email`. "No password symbol reachable" could not hold while `RegisterCommand.Password` existed;
`NoPasswordSymbolTests` now scans Domain, Application, Api and Worker for one, with no exception list.

**The seed seam.** `POST /api/v1/dev/accounts {email}` replaces `/auth/register` and `/dev/confirm-email` as
the E2E suite's seeding path: a run seeds more accounts than the code flow sends to addresses without an
account. It opens the account through `AccountRegistrar`, the writer `complete` uses, only for a reserved
address. Its second gate is `IDevSeedableAddressPolicy`, registered in `AddDevOnlyTestingSupport`. It answers
204 only when the login resolves the address to `Active`, and hands out no credential and sends no mail.

**The boot gate.** `Auth:RequireEmailConfirmation` goes with its rule. `AuthOptionsValidator` keeps one rule:
outside Development and Test, a sender that cannot deliver refuses the boot, whatever `RegistrationsOpen`
says. `RegistrationGateLog` keeps EventIds 4300 and 4301 and drops the confirmation half of its line.

**Retired EventIds, never reused:** 1002 `login_failed`, 1004 `account_locked_out`, 1005
`email_confirmation_resent`, 1006 `password_reset_requested`, 4004 (the reset's failed session invalidation),
4006 (the reset's failed `EmailConfirmed` write).

**The re-auth 401 keeps its byte-identical form** and loses its "lösenord":
`AuthErrorCodes.InvalidCredentialsMessage` is "Det gick inte att bekräfta att det är du."
(`security-auditor`'s string).

**Legal copy.** W moved the terms (:253) and `TermsAcceptance.CurrentTermsVersion` to 2026-09-25: D6's first
terms bump. Only `AcceptCurrent` reads the constant, so nothing asks for re-acceptance, and both accounts are
the controller's. The privacy line "lösenord (hash)" is struck in 5b: Klas chose "5b (Rekommenderat)" from an
AskUserQuestion on 2026-09-25.

**Corrections above.**
- D9's *"it goes when the last literal goes"* is false. The pre-push scan (`gitleaks detect --source .`) reads
  the history, where the literal stays: gitleaks 8.30.1 without the entry reports 53 `generic-api-key`
  findings, measured 2026-09-25. The entry stays, and its description now says why.
- Amendment 2026-09-17 (#1734)'s migration rule ran in B: the password bootstrap, `DefaultTestPassword` and
  `LoginAndGetSessionIdAsync` are deleted, and `SessionBootstrapTests` pins the passwordless bootstrap only.
  The box's two accounts still carry a hash until 5b, so the two proof tests about a legacy password account
  keep running on a private seed in `LoginChallengeProofTests`. The seed writes the hash at runtime, names the
  retired route as its actor (AGENTS.md §5), and goes in 5b with `RemovePasswordAsync`.

**The Identity `bootstrap` procedure** is `vps-deploy-stack.md` §3c (Klas, 2026-09-18). Running it on the box,
which applies 6d, is a separate Klas GO.

**DoD 8.** No new personal data. Four mails end, and their purposes with them: `EmailConfirmation`,
`AccountExistsNotice`, `PasswordReset` and `PasswordChangedNotice`. The HIBP range lookup ends. The three
`cd/*` cooldown keys end, and the persistent Redis ACL template drops their patterns. `password_hash` is kept
unchanged until 5b. No DPIA.

## Open — Klas decides (put to him in plain text 2026-09-17)

### Klas's answers, 2026-09-18 (verbatim; recorded on epic #1732, comment 5724716936)

| # | Question | Answer |
|---|---|---|
| 1 | Is a mail outage = a total login stop acceptable? | **Yes.** *"Ja det ärokej, mejl och OAuth är enda vägen in, vi ska inte ha lösenord."* |
| 2 | A break-glass — (a) a one-time code in the box's log, (b) a second mail provider, (c) a password kept for the admin account only? | **None.** *"ingen"* |
| 3 | Should part 5 (nulling `password_hash`) run before launch at all? | **Yes, delete.** *"Ja, radera"* |

**What the answers settle, and what they do not:**

- **The gate on 1a is met by this block**, written as the first commit of 1a's own PR (#1735) rather than in
  a standalone docs PR.
- **`security-auditor`'s Major 12 lands in 1a as D10 binds it:** the boot refusal drops its
  `RegistrationsOpen` condition and keeps asking the sender's capability (`CanDeliver`), never
  `Email:Provider`. With no break-glass, mail is a login path from 1a on and the only one once 5a removes
  the password surface, until an IdP goes live.
- **`security-auditor`'s Major 11 stays in 1c as D3 binds it.** Form (c) — a password kept for the admin
  account — was the only form that would have kept the password surface alive past 5a and reintroduced the
  lockout DoS on that account, and it was declined. 5b therefore nulls **every** `password_hash`, with
  `security_stamp` rotated in the same statement, never "all but one".
- **No break-glass is designed**; none of forms (a)–(c) is built.
- **5b still does not open until 5a is merged and measured live on `dev.jobbliggaren.se`.** Answer 3 removes
  only the Klas half of its gate.
- **The attempt budget's lapse trigger 6 is now a scheduled event**, and its re-measurement is owed in 5b's PR.
- **Klas, 2026-09-18, on the same thread (1a's plan session):** the manual Identity `bootstrap` procedure
  (parked, #1172) is written **in 5a's PR** — *"Ja, i 5a:s PR"* — because 5b is an Identity-context
  migration and that context has no automatic apply path (`migrate` runs `schema`, `AppDbContext` only).

The default and the escalations below are the record of what was asked; the answers above supersede the
default.

**The default that holds until he answers — parts 0.5 and 1b proceed, parts 1a and 5b wait:**
part 5 is split into **5a** (teardown of the password surfaces, `RequireEmailConfirmation`
retired, runbooks and BUILD.md truth-sync, #734 re-pointed) and **5b** (`password_hash` nulled).
**5b does not open until Klas has answered, and not before 5a is merged and measured live on
`dev.jobbliggaren.se`** (the same gate 4b has against 4a). **1a (#1735) does not open until the
three answers are in this ADR** — `security-auditor`'s condition below, made operative: #1735
carries `blocked` since 2026-09-17 and 0.5 (#1734) and 1b (#1736) are not gated. No break-glass is
designed until he names one. This keeps the irreversible step reversible and the two parts whose
right answer depends on his reply unopened, which is the only default that forecloses nothing.

The escalation, verbatim from `senior-cto-advisor`:

> **D10 gör e-post till ett hårt beroende av att kunna logga in — och efter del 5 finns ingen väg tillbaka.**
>
> I dag bryter ett mailavbrott (Scaleway nere, utgången API-nyckel, en avvisad avsändardomän) bara registrering och lösenordsåterställning — inloggning fungerar ändå, eftersom lösenordet finns. Efter den här epiken är kodmailet den **enda** inloggningsvägen: OAuth är blockerat på nycklar du inte har ännu, och del 5 nollar `password_hash`. Då gäller: **ingen kan logga in alls, inklusive du själv, och det finns ingen reservväg.** Adminseedern löser upp konton via e-post och hjälper inte här.
>
> Tre frågor, och jag behöver ditt svar innan ADR 0142 skrivs:
>
> 1. **Accepterar du att ett mailavbrott = totalt inloggningsstopp?** (Detta är ett tillgänglighetsbeslut med produktkonsekvens, inte ett tekniskt val — därför frågar jag.)
> 2. **Vill du ha en break-glass?** De realistiska formerna är (a) en Development/ops-gated engångskod som skrivs ut i loggen på lådan, (b) en andra mailprovider som fallback, eller (c) att lösenordet behålls för ditt eget adminkonto och bara för det.
> 3. **Ska del 5 (nollningen av `password_hash`) över huvud taget köras före lansering?** Alternativet är att låta lösenordsvägen ligga kvar inaktiv men intakt tills OAuth är live — det kostar att BUILD.md beskriver två auth-vägar ett tag till, men det gör steget reverterbart.
>
> Jag rekommenderar inget här: valet beror på hur mycket driftavbrott du tål på `jobbliggaren.se` under introduktionen av de första testanvändarna, och det är din bedömning. Frågan rör också #734 (go-live-grinden), som är din.

`security-auditor`'s cost per break-glass form (verbatim, no recommendation):

> **(a) Ops-gated engångskod i lådans logg.** Grinden kan inte vara `IsDevelopment()` (lådan kör Production) och inte en vanlig flagga — `DevTools:EnableResetMyData` är själva exemplet på en flagga som står kvar påslagen. Och koden landar då i **två loggsänkor**: Seq på lådan i **klartext, 30 dagars retention**, och Docker `json-file` som enligt behandlingsregistret har **ingen åldersgräns alls och odefinierad Art. 17-position**. Formen är alltså inte "en kod i en logg" utan en inloggningskredential persisterad i en sänka utan raderingsrutin. Kräver: engångsbruk, mycket kort TTL, audit-rad, och medveten uteslutning ur båda sänkorna — plus egen motivering eftersom den passerar #1208:s mottagargrind helt.
>
> **(b) Andra mailprovider som fallback.** Kräver ett andra Art. 28-biträdesavtal, en rad i behandlingsregistret **och** i integritetspolicyns Mottagare-avsnitt, en Kap. V-kontroll, egna credentials på lådan med egen rotation, samt en failover-regel som inte dubbelsänder — den sista finns redan (claim-then-send + `ICooldownGate`, ADR 0103). Kostnaden är nästan helt **compliance-yta**: det är den enda av de tre som lägger till en ny mottagare av varje användares adress. Säkerhetsmässigt den renaste — ingen ny kredentialklass, ingen ny bypass.
>
> **(c) Lösenord kvar enbart för adminkontot.** Kräver att 5b blir "nolla alla utom en", varmed invarianten blir *"passwordless utom ett konto"* — ett villkorat påstående ingen billig test kan uttrycka. Och det kräver att **hela lösenordsytan** lever vidare: `/auth/login`, `ValidateCredentialsAsync`, lockout-vägen, `PwnedPasswordValidator` och lösenords-UI:t. Alltså faller inte bara 5b utan **5a**. Den återinför dessutom lockout-DoS:et (Major 11) på exakt det konto vars tillgänglighet break-glassen finns för att skydda. Högst säkerhetskostnad; enda formen som varken behöver mejl eller logg.

`security-auditor`, verbatim: *"de tre svaren måste in i ADR 0142 innan del 1a öppnas, eftersom
Major 11 och Major 12 (var lockout-hålet stängs, och att boot-vägran tappar sitt
`RegistrationsOpen`-villkor) får olika rätt svar beroende på om en break-glass finns."* That is the
gate on #1735 named in the default above.

`design-reviewer`'s question, verbatim: *"provider-märkenas färgsättning saknar token och kan inte
lösas inom DESIGN.md. … Jag behöver ditt besked om du vill (a) monokromt hela vägen och avstå de
officiella märkena, eller (b) ett scopat DESIGN.md-undantag för de tre officiella märkena i 6a."*
Default until answered: monochrome while inactive (D8); the colour question is 6a's.

## Attempt budget

| Parameter | Value | Where enforced |
|---|---|---|
| Code length | 6 digits, CSPRNG with rejection sampling | adapter |
| Attempts per challenge | 3, counter incremented before compare, then burn (the code arm; the link lives to the TTL) | `ConsumeCodeAsync` |
| Challenge TTL | 15 min, code and link, one expiry state | Redis TTL |
| Live challenges per address | 1 live **code** challenge — a mint the code budget admits burns the previous; records minted past it are not indexed | `PutAsync` |
| Mint budget per address | cooldown first; 3 / 10 min caps mails; 10 / 24 h caps codes, and above it the mail carries no code; silent, consumed before any lookup | `IRateBudget` |
| Mails to addresses without an account | 20 / 24 h, all such addresses together; above it the record is written, carrying no credential (Amendment 2026-09-20), and no mail is sent; an account holder's mail is never counted | `IRateBudget`, in the consumer, consulted before the record is written |
| Re-authentication mint budget (3a) | per USER: `reauth-cooldown` 1 per window and `reauth-codes` 10 / 24 h; past `reauth-codes` the request is REFUSED, since a link cannot re-authenticate | `IRateBudget` |
| Change-email mint budget (3a, PR 4) | per USER: `change-email-user` 1 per window and `change-email-targets-daily` 5 / 24 h; per new address, whoever asks: `change-email-target` 1 per window and `change-email-per-target-daily` 3 / 24 h; each request also spends a re-authentication grant | `IRateBudget` |
| Live bound challenges | 1 per user and purpose: every mint burns the previous | `PutBoundAsync` |
| Per-IP | `AuthWrite` 20/min, unchanged | rate limiter |
| Grant TTL | 10 min, single use, purpose + subject asserted inside `Redeem` | grant port |
| OAuth state | ≤ 10 min, cookie mandatory, Redis record `GETDEL` | 6a |

Success probability per targeted address (security-auditor's arithmetic): 30 guesses/day →
1 − (1 − 10⁻⁶)³⁰ ≈ **0.003 %/day**; **1.089 %/year** under sustained attack; at ≈ 92 accounts
that is one expected takeover per year if every account is attacked continuously *(the existing-account arm
only: for a new address the attacker chooses the denominator, Amendment 2026-09-20)*. This is
accepted for the product as it is today and **lapses on any of these triggers**, each requiring a
new measurement recorded in an amendment here:

1. `Auth:RegistrationsOpen=true` outside Development (the #734 flip).
2. The first registered account that is not Klas's (ADR 0132's trigger, shared, never inherited).
3. The account count passes ~90.
4. An IdP goes live (6a): the premise "mail is the only way in" falls.
5. Code length, attempt count or mint budget changes in either direction. Since Amendment
   2026-09-19 (2) it also fires if a restart of `redis-volatile` becomes reachable by anything other
   than the operator. Since Amendment 2026-09-20 the global cap on mails to addresses without an account
   is one of its quantities: it is what bounds code-bearing records for new addresses.
6. 5b lands (no password fallback remains).
7. D2's "the consumer always sends a mail" premise falls or the budget branch changes behaviour —
   the code-step resting copy (below) was written on that premise (design B2). It fell three times
   (#1756, #1779, #1783), and part 2 re-bound the copy so that it rests on no such premise
   (Amendment 2026-09-21 (3)).

The rejected TOTP provider (`DependencyInjection.cs:1658-1676`) was rejected as **stateless**; the
challenge path never goes through Identity's `opts.Tokens` providers, and re-enabling the "Email"
provider would reintroduce exactly the property that was rejected.

## Page form

Bound by `design-reviewer`, and re-bound by her in part 2's form round (Amendment 2026-09-21 (3)) and in
3b's (Amendment 2026-09-22 (5));
part 2 renders every state below before a design verdict (AGENTS.md §8 point 4), in light only
while `DARK_MODE_ENABLED` is `false`.

- **Routes stay in `(auth)`:** SiteHeader/SiteFooter, centred `max-w-sm`, h1 in flow, no
  `jp-pagehero`, no hero gradient. Own `h1` per route: `/logga-in` "Logga in eller skapa konto" ·
  `/logga-in/kod` "Ange koden" · `/logga-in/villkor` "Skapa ditt konto" · `/logga-in/lank` "Logga in
  på Jobbliggaren", or "Du är redan inloggad" on the arm shown when the browser already holds a
  session. `{email}` in body text, never in `h1` or `<title>`. `robots: {index:false}` on
  kod/villkor/lank. `/registrera` → 308 `/logga-in`; `/installningar` and `/mig` → 308 `/mina-sidor`,
  permanent (`retired-routes.test.ts`; why, in Amendment 2026-09-22 (5)).
- **`/logga-in`, two orders switched on `GET /auth/oauth/providers`** (design M1): **empty list
  (now):** h1 → a lede (*"Du loggar in med en kod som vi skickar till din e-postadress. Du behöver
  inget lösenord."*) → email field + hints (incl. the Art. 13 line) → **Fortsätt** (the only
  `variant="default"`) → hairline → `h2` "Andra sätt att logga in" → the three inactive rows, no
  "Eller" divider. **At least one provider live (6a):** provider buttons → divider "Eller fortsätt
  med e-post" → field → Fortsätt.
- **Provider buttons** (design M2): shadcn `Button` `variant="outline"`, never `.jp-btn` in the same
  view, never three solid fills, never a provider's brand colour as fill. Inactive =
  `aria-disabled="true"` + **kept in the tab order** + no-op click + "Kommer snart" as the visible
  text — never `disabled` (the explanation would leave the a11y tree); never `opacity` as the
  dimming (contrast ≥ 4.5:1 in both themes); `type="button"`. No provider mark while inactive: the
  row is the text "Fortsätt med {provider}" with a trailing "Kommer snart", its accessible name
  computed from that content, in the order Google, LinkedIn, GitHub.
- **Code step** (design M3/M4; the boxes Klas's, Amendment 2026-09-24 (9)): ONE `<input>` with a visible label
  "Sexsiffrig kod", `autocomplete="one-time-code"`, `inputmode="numeric"`, `maxLength=6`, `pattern="^\d+$"`,
  `aria-describedby`, no placeholder, drawn as six joined `aria-hidden` boxes that hold no name, value or tab stop
  of their own. "Skicka ny kod" reuses
  `ResendConfirmationButton`'s form (disabled 60 s, countdown outside the live region, message in
  `role="status"`) and **starts in that cooldown**, with a static line beside it, *"En ny kod
  ersätter den förra. Skriv in koden från det senaste mejlet."* "Byt e-postadress" is a submit
  styled as a text link, last: a GET cannot clear the typed address from the device. The primary
  is "Bekräfta koden". **The login page never states what the system did, only what the user should do**;
  the re-authentication bullet below says where a page may.
  Resting copy: *"Kontrollera inkorgen och skräpposten. Finns det ett mejl från Jobbliggaren följer
  du instruktionerna i det. Innehåller mejlet en sexsiffrig kod skriver du in den här. Koden gäller
  i 15 minuter."* with the hint *"Kommer inget mejl inom några minuter kan du skicka en ny kod,
  eller byta e-postadress."* The typed address is its own statement below the field group, *"Du
  angav {email}."*, never inside a sentence about a mail. A warning before the last attempt: *"Ett
  försök kvar. Sedan behöver du begära en ny kod."*
- **The states**, channel discipline as `RegisterForm` delivers it — user-correctable →
  `role="alert"` + `aria-invalid` + focus to the field; not the user's fault but a way forward here →
  `role="status"` panel, `tabIndex=-1`, focus moved; no way forward here → the form is
  **replaced** by the panel. A panel that replaces the FORM carries an `h2`, and the `h2` is the
  panel's first sentence; one that replaces only the FIELD carries none, since the step's `h1`
  still describes the page:

  | State | Channel | Copy | Action |
  |---|---|---|---|
  | wrong code | alert under the field | "Koden stämmer inte. Kontrollera siffrorna och försök igen." | field stays |
  | expired | status, replaces the field | "Koden går inte att använda längre. Skicka en ny kod och försök igen." | "Skicka ny kod" becomes primary |
  | burned | status, replaces the field | "Du har skrivit fel kod tre gånger, så koden går inte att använda längre. Innehåller mejlet en inloggningslänk kan du använda den i stället, annars skickar du en ny kod." | "Skicka ny kod" primary |
  | registration closed | status, replaces the form, never danger colour | "Registreringen är inte öppen ännu." | link "Till startsidan" |
  | account unavailable | status, replaces the form, never danger colour | "Vi kan inte logga in på den här adressen just nu. Försök igen senare, eller kontakta oss på kontakt@jobbliggaren.se." | mail link |
  | pending deletion | status, replaces the form | "Ditt konto raderas permanent {14 apr 2026}. Fram till dess kan du få det återställt genom att mejla kontakt@jobbliggaren.se." | mail link; no "Ångra" button that does not exist |
  | resting / sent | base render, focus h1 | the resting copy above | field + "Skicka ny kod" + "Byt e-postadress" |
  | throttled (429) or unavailable (503) | status in the form's own message slot, focus to the message, never danger colour | "För många försök. Vänta en stund och försök igen." · "Det går inte att logga in just nu. Försök igen om några minuter." | the form stays |
  | back on `/logga-in` with a notice | status panel with `h2` above the form, focus moved | "Registreringen slutfördes inte" (an unusable grant) · "Inloggningen gick ut" (a code submitted after the flow ran out) | the form, which is the remedy |

- **Consent step:** checkbox label *"Jag godkänner <terms>användarvillkoren</terms>."* — full stop;
  the privacy policy in a sibling sentence under the box (*"Vi behandlar dina uppgifter enligt
  integritetspolicyn."*), never inside the acceptance. "Skapa konto" the only primary. The D4
  disclosure directly above it. The step shows no address: under the `h1`, *"Kontot skapas på den
  e-postadress du nyss bekräftade med koden."*, and last the same submit out, *"Börja om med en
  annan e-postadress"*.
- **Link landing** (design M5): `<form action={consumeLinkAction}>` with the token in a hidden
  input and a submit button — works with JS off; no `useEffect` consumption (scanners GET); this is
the simpler form, so **a live token stays in the browser history for up to 15 min** (the 303-hop
form that moves it into a short-lived cookie was not chosen);
  `Cache-Control: no-store` on GET **and** POST; `Referrer-Policy: same-origin`, as a route header
  and as page metadata from one constant, **measured** to win over the global
  `strict-origin-when-cross-origin` rule (a route rule that does not win is a rule that does not
  exist). It is `same-origin` and not the other token pages' `no-referrer` because the two binds
  in this bullet contradict each other otherwise (Amendment 2026-09-21 (3)). When the browser
  already holds a session the page says what continuing does and asks for a choice between two
  controls, naming no address. At rest: *"Du har öppnat en inloggningslänk från ditt mejl. Länken
  gäller en gång och i 15 minuter."* and the button "Logga in"; expired and used share one sentence: *"Länken går inte att använda. Begär en ny kod på
  inloggningssidan."* The link route answers the same outcome union as the code step (D3). Why a
  login token in a URL is accepted where #706's change-email token was
  not: 15 min against 24 h, single use, one record that burns code and link together, the Caddy
  edge scrub, a referrer policy that never crosses the origin, `no-store`. **The edge-scrub pin is extended in 1a**
  (`CaddyfileTokenScrubbingPinTests`: both link-bearing login templates and `/logga-in/lank` enter
  `RenderedLinks()`
  and the `TokenLink` regex; the parameter is spelled exactly `token`, the filter is case-sensitive;
  5a adds a `ShouldNotBeEmpty` so the derived set can never pass vacuously).
- **"Mina sidor"** (design M6, part 3b): the trigger is a `.jp-icon-btn` like `NotificationsBell`
  with `<UserRound size={18} aria-hidden>` in `currentColor`; no tinted circle; `aria-label` "Mina
  sidor"; `jp-usermenu__name` (the email's local part shown as a name) removed; `initials()` deleted;
  `.jp-avatar` removed after a measured consumer count; the popup follows the bell's
  `aria-haspopup="dialog"` pattern, is `role="dialog"` named by the same label and described by its head;
  drawer and menu re-pointed to `/mina-sidor` with one label (as delivered: Amendment 2026-09-22 (5)).
- **Re-authentication by code** (design D1/D2, part 3b): the dialog only re-authenticates, by a code to
  the account's own address, and closes; change-email's second code, to the new address, is taken in the
  card, and delete-account reuses the dialog (Klas 2026-09-22). **A page may state a send only
  where three facts hold:** the 2xx follows an awaited send, every branch that sends nothing answers a
  visible refusal, and the address shown is the recipient. `POST /auth/reauth` and `POST /auth/change-email`
  meet all three, so *"Vi har skickat en sexsiffrig kod till {email}"* is true there; `POST /auth/challenge`
  meets none, which is why the login code step never says it (Amendment 2026-09-23 (6)).
- **Copy:** every string in `messages/sv/` **and** `messages/en/` in the same PR (ADR 0137); retired
  keys deleted in the PR that removes their last consumer, never orphaned; 5a deletes those part 2
  orphaned (Amendment 2026-09-22 (5)); no em-dash, no literal `...`; English says "log in"
  throughout, the register the catalogue already had. `landing.auth.free`/`fine` are not on
  `/logga-in`: the zone above its primary belongs to the D4 disclosure on the steps that create a
  session, and `fine` moved to the landing account card beside `free`. ≤ 768 px: primary and
  provider buttons `size="lg"`.

#### Amendment 2026-09-21 (3) (#1738, part 2) — the page form as delivered, and the corrections above

*Decided before code in one form round: `design-reviewer` (five hand-backs), `security-auditor`,
`senior-cto-advisor`.* The sentences in D2,
D8, "Attempt budget" trigger 7 and this section that the round contradicted were corrected in place; this
block records why.

**The copy no longer claims a send.** Three delivered facts falsified *"Vi har skickat ett mejl till
{email}"*: the mail goes to the account's own stored spelling (#1779), and showing the real recipient would
disclose it; two branches write a challenge and send nothing (over the global cap; an Identity row without
a profile); and a request inside the cooldown or past the mail budget sends nothing new. The uniform 202
means the page cannot know which happened, so the resting copy and the resend receipt instruct and never
report. "Expired" names no cause for the same reason: the backend answers `Auth.LoginCodeExpired` for a
challenge that ran out, was used, was replaced or never had a record. "Burned" names the link, because
the burn is the code arm's alone and the record's link still logs in.

**The flow's position lives in the cookie, as a closed union of four phases** (`senior-cto-advisor`):
`code` (challenge id, the address as typed, `next`, the mint time, and `dead` once the backend has
answered 410), `consent` (the grant and `next`, no address), `outcome` (a terminal result, no address) and
`notice`. Two measured reasons. In Next 16.3.3 a Server Action that sets or deletes a cookie makes the
handler re-render the current route in the same response (`request-cookies.js`, `action-handler.js`), so a
panel held in action state is lost to a page that redirects on a missing cookie. And the grant must
survive the move from `/logga-in/kod` to `/logga-in/villkor` without entering a URL (D3). The rule that
makes the re-render harmless is pinned per action: a return that writes the cookie ends in `redirect()`,
and a state return never touches it; `resendCode` is the one exception. What must NOT survive a reload
(a wrong code, the last-attempt warning, a transport failure) stays in action state. The cookie is
unsigned: only its holder can forge it, no branch reads it as proof, every arm is a strict object so one
phase's field cannot reach another phase's reader, and `next` is re-validated when it is read
(`security-auditor`). It is deleted in the same action that sets the session cookie, on the code path and
on the link path.

Inside the server's cooldown a second
request is answered with a challenge id that has no record, and storing it would make the code already
mailed unverifiable. The rule is keyed on the cookie's own liveness, so the frontend mirrors no window for
it. The comparison is exact after `trim()`: the backend's fold (`SubjectFingerprint`, NFC then
upper-invariant) is C# in Infrastructure and unreachable from the web app, and a second fold in a second
language could over-match across exactly the territory of #1779. Another spelling simply mints, and a test
pins that the fold is absent on purpose. "Skicka ny kod" does mirror one number, the 60 seconds this
section already bound, against the default of `AuthEmailCooldownOptions.LoginChallengeWindowSeconds`; if
that default is raised, `RESEND_COOLDOWN_SECONDS` is raised with it.

**`same-origin` on the link landing, because this section's own two binds contradicted each other.** It
bound `referrer: "no-referrer"` and a form that works with JavaScript off. Measured 2026-09-21 against the
production build, JavaScript off, the policy rewritten in flight: as served the form POST carries `Origin:
http://localhost:3000` and the action runs; under `no-referrer` it carries `Origin: null`, Next answers 500
and the action does not run (a no-JS form POST is a navigate-mode request, and Next refuses a Server Action
whose `Origin` is not the host's). The other token pages post through `fetch()` and are unaffected.
`same-origin` still strips the referrer on every cross-origin request; the edge drops the whole
request-header map from its log (`CaddyfileTokenScrubbingPinTests`), and Next's production stdout carried
neither the path nor the token of a GET and a POST (measured the same day, `next start`).
`token-link-metadata.test.ts` names this page as its one exception.

**A link opened while a session exists asks first** (`security-auditor`, Major M-2). Without it the press
silently replaced the session with one for the address the link proves, so a link minted for someone
else's address and mailed to a logged-in user was a login CSRF one press long. Put to Klas Olsson
2026-09-21, her question verbatim, whether the guard is (a) two buttons or (b) one warning sentence above
the existing button: **"(a) Två knappar"**. The arm names no address, and on continue leaves the earlier
session alive on the server: revoking it would let any link sign another account out everywhere. Cancel
goes to `/oversikt`.

**No provider mark while inactive** (`design-reviewer`). lucide ships no brand icons, so a monochrome mark
would have meant hand-drawing three third parties' trademarks onto controls that log nobody in.

**The forms are `noValidate`, with `required` kept**, as ruled for the settings name field in #1782. For
the address field it is also a correctness fix. #1781 made `björn@…` registrable in the backend only: the
HTML email production is ASCII-only in the local part, so native validation refuses the address before any
submit. jsdom runs the same validation, and the test that types `björn@` and reaches the action fails with
`noValidate` removed. The input schema checks length only; what an address is stays the backend's.

**Light only.** `DARK_MODE_ENABLED` is `false`, so no user can reach a dark surface and a forced-dark
render would grade one that does not exist. Part 2 adds no colour literal. The day the flag is set, these
four routes are rendered in dark and graded before that change merges.

**The policies.** The privacy policy says that an address entered on the login page is processed and that
an account can be created from it, which bound the first caller of `POST /auth/challenge`
(`security-auditor`, her text verbatim), so `privacy.updated` and
`TermsAcceptance.CurrentPrivacyPolicyVersion` moved together; `CurrentTermsVersion` did not. The cookie
policy lists `__Host-jobbliggaren_login`, the first cookie here that holds a personal datum, and lost
three statements this part made false.

#### Amendment 2026-09-22 (5) (#1740, part 3b, PR A) — Mina sidor replaces Inställningar, and the corrections above

*Decided before code in one form round: `design-reviewer` and `security-auditor` in parallel, then
`senior-cto-advisor` (`docs/reviews/2026-09-22-1740-form-{design,security,cto}.md`).* The
sentences in D7, this section and "Implementation status" that the round contradicted were corrected in place;
this block records why. PR B adds its own block.

**Klas's ruling, and two PRs in a binding order.** Asked by the session that opened 3b, Klas chose on
2026-09-22; the choice is his, and the wording is the asking session's, as recorded in its handover: *"The
dialog is a **pure re-authentication step** — the code to the CURRENT address — and then it closes. The
change-email **card** takes the second code (to the NEW address) in its own inline step. Delete-account reuses the
same dialog unchanged. **Rejected:** one dialog carrying all steps as a three-step wizard."* `senior-cto-advisor`
split 3b in two, one change-reason each: **A**, "Mina sidor replaces Inställningar" (the route and its 308s,
the shell, the page, the copy), then **B**, "Re-authentication by code: delete and change-email", built on A
and opened only after A has merged. A 308 that every mail already sent depends on must not ride in a squash
that a custody defect might revert. 3a's declared window (D5, Amendment 2026-09-21 (4)) runs through A and
closes at B's deploy: until then `/mina-sidor` carries the delete and change-email controls of the password
era, and both answer 400.

**Password-era code goes in the PR that removes its last consumer** (C1). "Deleted in 5a" was the schedule
written for part 2's keys; "not orphaned" is the invariant, and 3b removes the last consumers. A deletes the
name card and the password card with what only they used: the change-password action, its schema and test,
the display-name arm of the profile action with `FieldScopedActionResult`, and their keys. B deletes the
password dialog, the password-era change-email keys and `errors.wrongPassword`. #1740's comment that 5a
deletes the dialog is superseded; its "do not build on it" stands. 5a keeps what part 2 orphaned and the
password surfaces still live, among them the backend's `/auth/change-password`.

**Nothing changes the account name until 4a: a declared non-finding** (C2). `PersonnummerInAccountName`
sends the user to kontakt@, as a mail link inside its sentence and with no button, since a button to
`/mina-sidor` would promise a fix the page does not have (`design-reviewer`: a Blocker if shipped;
`security-auditor`: a false Art. 16 instruction). A nameless account's `IncompleteContent` (D7) has no fix the
user can apply either. The window runs from A's deploy until 4a and is a non-finding, not an acceptance: no
§9.6 (3), no Klas grant, no signature. No data subject meets it. The name arm needs a name written before
#1117, and the nameless case needs a new account while registration is closed (#734 row 7 holds the flip
until 4a). **The reading**, read-only on the box, counted and never printed (`psql -tA` over stdin in the
postgres container; the api container's environment through `docker inspect`), 2026-09-22T22:23:36Z:
`identity."AspNetUsers"` 2 rows, 2 of them the controller's own inbox or a plus-alias of it; `job_seekers`
2; `Auth__RegistrationsOpen=false`, 1 line and the only `Auth__RegistrationsOpen` line (22:23:26Z). This block
is the reading's home. A row that is not the controller's would have stopped A and returned C2 to
`security-auditor`.

**The shell as delivered** (M6, `design-reviewer` D3). The trigger is a `.jp-icon-btn` with `UserRound`,
named by `common.nav.minaSidor` ("Mina sidor", "My pages"), with `aria-haspopup="dialog"`. On ≤ 768 px the
header's icon buttons are 44 × 44, an edit to the unlayered rule, so the bell and the drawer trigger grew
with it (Blocker 1). The popup is `role="dialog"`, the only role that matches its trigger, named by the same
key and described by its head: "Inloggad som" and the address, sans at 14 px in `--jp-ink-1`, the address
semibold and wrapping (Major 4). One key serves the trigger, the popup's name, the popup item and the drawer
item, as `nav.foretag` does; `userMenu.ariaLabel`, `userMenu.installningar` and `drawer.installningar` are
retired. English is "My pages", one concept in both catalogues.

**The page as delivered** (Majors 3 and 5, D4). `/mina-sidor` is V3-native: a `jp-pagehero` with title and
lede and no aside, the grid in `jp-container jp-page`, and `PageHeroSkeleton` while it loads. A new route that
is rebuilt is where the transitional container's allowance ends, as #515 did for `/foretag`. Column 1 is
Matchning, Matchningsnotiser and Notiser om företag du följer; column 2 is Visning, Byt e-postadress,
Sekretess och data and Logga ut. The three account cards read only the session's address and render on every
profile branch, and the branch's sentence takes the place of the cards that read the profile. There is no
branch for a missing profile: `getMyProfile` reads without `includeNotFound`, so the backend's 404 arrives as
`error`, and the branch design gave copy for could never render (measured in the rendered round). It was
deleted, per design's rule for an unreachable branch. For that 404 the error sentence advises a reload that
cannot help. Since part 2 no login
opens a session for one (`LoginProofOutcome`).

**The 308s are permanent.** The notification mails' Art. 7(3) withdrawal link was `/installningar` until
this part, and no measurement can show that no inbox still holds one (`security-auditor` S5). So
`/installningar` and `/mig` answer 308 to `/mina-sidor` with no removal trigger, pinned in
`retired-routes.test.ts`. `NotificationMailLinksLandOnServedRoutesTests` joins the two stacks: every link the
two notification mails build must be served by a page under `src/app/(app)/` (security Minor 7).

#### Amendment 2026-09-23 (6) (#1740, part 3b, PR B) — re-authentication by code for delete and change-email, and the corrections above

*Decided in (5)'s form round (`docs/reviews/2026-09-22-1740-form-{design,security,cto}.md`).* The sentences in
Amendment 2026-09-21 (4) and this section that B made false were corrected in place; this block records why.
3a's declared window closes at this PR's deploy.

**The form as delivered.** `ReAuthCodeDialog` asks for a code to the account's own address, takes it, and hands
the consumer's operation the proof. It holds the challenge in the component that renders the dialog and stays
mounted, so a close and a re-open inside the code's lifetime land on the code step. Delete-account adds the typed
address to the request step. Change-email checks the new address when "Fortsätt" is pressed, before the dialog
opens, then takes the change code in the card, with "Börja om" as the way back: a new change code needs a new
re-authentication, so the card has no resend.

**Custody** (`security-auditor` S1). No Server Action takes a grant or a session id as an argument, and none
returns one: each verifies its code and performs its operation in one request, through a verify helper that is
`server-only` in a module without `"use server"` (a test reads the module). A challenge id lives only in the
state of the component that asked for it, never in a URL, a DOM attribute, storage or an error string, and is
dropped after a verify that answered 200, on a 410, after the code's lifetime and on unmount. `next dev` prints
every Server Action call with its arguments unless `logging.serverFunctions` is off; it is, and pinned (S6).

**The re-issue** (S2). The change-email confirm sets the session cookie only from a strictly parsed 200, with both
of its values; a 200 that does not parse sets nothing and is an unknown outcome.
**What the proxy does to it, measured** 2026-09-23 with the rotation forced
(`Session:Persistent:RotationInterval` 20 s): Next 16.3.3 sends the proxy's rotated id and the re-issued one on
the confirm's response, in that order, and the browser keeps the last, which is live; the rotated one is dead,
because the confirm ends every session. The reading that the action's cookie replaces the proxy's did not hold;
its feared consequence did not follow, because the order puts the live id last. `change-email.spec.ts` pins the
order where the environment forces rotation (`E2E_ROTATION_INTERVAL_SECONDS`) and skips elsewhere.

**Three outcome classes after the operation's request** (Minor 3). A documented refusal says the operation was
not done, and that the code is spent; a 5xx, a transport failure or an unreadable 2xx claims nothing and offers a
reload; done is done. Every refusal after a verify that answered 200 says the code is spent, and no other message
does. The change-email request has no unknown class: the address can only change at the confirm, so every failure
there is "not done". Among its 409s only `EmailTaken` and `ChangeEmailTargetBudgetExhausted` are compared; every
other 409 takes the neutral copy, so no producer of the shared 409 can be told apart (S3).

**Two comparisons fold, and part 2's rule does not reach them.** Delete's typed confirmation and change-email's
same-address check compare in one form (`trim`, NFC, lower-case), in the browser and in the action. The typed
confirmation proves no identity, the code does (S4), so a loose match costs nothing, while a missed one would lock
an account out of Art. 17. The same-address check only refuses before a code is spent, and what an address is
stays the backend's. Part 2's rule keeps a fold out of the login's resend comparison, where an over-match would
decide which challenge a spelling reaches (Amendment 2026-09-21 (3)); neither comparison here reaches anything but
the caller's own account.

**DoD 8** (`security-auditor` S5). No new personal data: the flow writes nothing new on the server (its records,
grants and budgets are 3a's and registered), and the client state lives in the tab's memory.
No new logging. Retention unchanged. No DPIA. The deletion notice rides the login flow cookie's `notice`
phase with its name only, for its 120 seconds; the cookie policy says so and `cookies.updated` moved, while the
privacy policy and its version did not.

#### Amendment 2026-09-24 (9) (#1826, part 4 of epic #1822) — six boxes behind one real input

*Decided by Klas Olsson; the form graded as built, before the PR, by `design-reviewer` and `dotnet-architect`
(`docs/reviews/2026-09-24-1826-form-{design,architect}.md`).* The "Code step" bullet in "Page form" was corrected in
place; this block records why.

**Klas's answer.** Put to him 2026-09-24, verbatim: *"Kodfältet på /logga-in/kod: ett fält eller sex rutor?"* The
options were *"Ett fält med kodutseende (Recommended)"*, described as *"Behåller ADR 0142 och a11y-Blockern. Stort,
centrerat, brett teckenavstånd, numeriskt tangentbord. Ingen ny beroende."*, and *"Sex rutor, riv ADR-raden"*,
described as *"Kräver att du river ADR 0142:569-571, ett nytt bibliotek (input-otp) utanför BUILD.md §3.1 och att
design-reviewers a11y-Blocker överprövas av dig."* He chose **"Sex rutor, riv ADR-raden"**. [The line numbers refer
to the copy the question was written from, where they held the "Code step" bullet; they do not point into this
file.]

**M3's four grounds, measured against the built field.** M3 forbade six boxes because a label cannot pair with six
fields, paste breaks, a screen reader reads six nameless text boxes, and the DOM order becomes six tab stops for one
value. Measured 2026-09-24 on the production build, the built field keeps all four properties M3 protected:
`input-otp` draws the boxes as `aria-hidden` elements behind ONE real `<input>`, which carries the label, the value,
`autocomplete="one-time-code"`, the paste and the only tab stop. What Klas overruled is the visual clause alone.

**Where the library differs from a plain input.**
- Its `pattern` is a RegExp tested against the whole value and written to the attribute, so `"[0-9]*"` would filter
  nothing; the attribute is `^\d+$` and a non-digit is refused.
- A paste is tested whole. Whitespace is stripped first; anything else refuses the whole paste without a message.
  The mail sets the code alone on its line.
- It keeps a value of its own that React's form reset after an action does not reach; the field clears on its
  form's `reset`, as the input it replaces did.
- With JavaScript off the boxes never fill, so the real input is drawn as a plain field in the token colours, in the
  sans face, with the global focus ring. The library's own fallback hard-coded black and white.
- Forced colours repainted the transparent input's text over the first box. The input opts out of colour forcing;
  the boxes, their borders, the active box's outline and the caret take the system colours.

**The visual form** (`design-reviewer`). Six joined boxes, each 44×44 at every width, outer corners `--jp-r-md`;
digits 20px / 600 with `tabular-nums`; the active box carries the global focus ring's value (2px `--jp-focus`,
offset 2px); while the field is invalid every box's border is `--jp-danger-600`, and the alert is unchanged; the
caret stands still under reduced motion.

**Light only.** `DARK_MODE_ENABLED` is `false`. The boxes sit outside the dark field rule in `globals.css`, so the
day the flag is set they are rendered in dark and graded before that change merges, the error border included.

**Order.** Klas ran this part before #1824 (the copy sweep) on 2026-09-24, while #1742 held the i18n hotspot. This
part changes no string; the hint and its colour tier are #1824's.

## Processing register and DoD 8

In the same PR as 1a, `docs/runbooks/gdpr-processing-register.md` "Behandling: Användarkonto och
autentisering" gets a new `Datafält` bullet — *Inloggningsutmaning (Redis: `auth/challenge/v1/*` and
the address index `auth/challenge-by-address/v1/*`, TTL 15 min; the budget keys
`budget/login-challenge-{mails,codes,cooldown}/v1/*`, TTL their windows): the DataProtector-protected
address, code and link hash; the keys are SHA-256 fingerprints of the address, pseudonymised personal
data (Art. 4(5)); self-expiring, no reaper* — and its own `Retention` row. Each part records only the
keys it creates: the grant keys go in with 1c, the OAuth-state keys with 6a. The
`Sessioner` bullet's "payload carries only non-PII" gains *"this holds for the session record, not the
challenge record"*; a line records that the challenge record is practically unreachable for
Art. 15/17 because it expires within the response time. **Copy follows data, never precedes it:**
"lösenord (hash)" (`content-legal.json:33`) is struck in **5b**; "visningsnamn" leaves the purpose sentence in
**4a's PR B**, with its purpose, and the stored-data sentence in **4b**, with the data (Amendment 2026-09-23 (7)); the
register's "the operation carries a credential (the password)" in **3a**. **No DPIA is required**
(Art. 35(3)(a)–(c) all negative: no systematic evaluation with legal effect, no large-scale special
categories, no public-area monitoring) — recorded here for DoD point 8.

## Consequences

**Positive.** One page, one credential class per action, no password to breach or reset; the
request path never reads the account, which is stronger anti-enumeration than today's
`LoginTimingEqualizer` by construction; consent is recorded (#1484 closes); the persistent-login
basis is written (#1494 closes); OAuth-readiness is real (`AspNetUserLogins`) instead of two dead
columns; the CV stops requiring a name the product never needed.

**Negative, accepted.** Mail becomes a hard dependency of login — accepted by Klas on 2026-09-18
(above). Three OAuth adapters are written and tested by hand (≈ 3 × 150 lines + contract tests)
instead of configured. Two mails and two code entries for an email change. Eighteen PRs instead of
fifteen. A new PII key class in Redis for ≤ 15 min, with the keyring as its confidentiality bound.
The attempt budget is a measured acceptance with seven lapse triggers, not a permanent property.

## Alternatives considered

- **D8 Variant A — aspnet-contrib remote-auth handlers with a proxied callback.** Rejected: the
  handler owns a browser-reachable callback and sets correlation cookies on responses that must reach
  the browser, which ADR 0018's trust model forbids; three new top-level dependencies without a §9.2
  ground. **Variant C — Arctic/oslo or an Auth.js adapter in Next.** Rejected on the dependency rule:
  the client secret and the identity assertion leave the backend, and `IExternalIdentityProvider`
  would have nothing to implement.
- **D1 — a Postgres table for challenge/grant.** Rejected on four grounds: login would need both
  stores up; a reaper, an `expires_at` index and a retention rule for 15-minute data; three atomic
  primitives traded for a hand-built transaction protocol; PII moved from volatile to durable *(false as 1a delivered it; true from Amendment 2026-09-19 (2) on)*.
- **D5 (i) — one code to the new address + notice to the old.** Rejected: a stolen 180-day session
  repoints the recovery vector; detection sold as prevention. **(iii) — re-auth code + today's
  confirmation link.** Rejected on DRY: two inbox-proof mechanisms, #706 moved rather than closed.
- **Design B1 (b) — full uniformity on verify with one sentence naming both causes and both
  remedies.** Not chosen: it would forbid the attempt counter and the pre-burn warning, and the
  disambiguation for the cookie holder leaks nothing (a record is always written).
- **Merging 4a+4b or 5a+5b into one PR each.** Rejected: an irreversible migration must not travel
  with the semantic change it might need to revert.

## Implementation status

Parts, one PR each, all `mvp`, sequence as bound by the CTO (issue numbers from #1732's first
comment; 5a/5b are one issue, #1743, until it is split):

**0** #1733 this ADR → **0.5** #1734 harness → **1b** #1736 consent seat + migration
(`Persistence`) → **1a** #1735 in four PRs: **1a-prep** #1755 (merged 2026-09-19; Klas's three D10 answers,
the normaliser's one home, `BoundedDispatchChannel/Service<T>`, `StoreUnavailableException`; no behaviour
change) and **1a** challenge/verify/link, store, both dispatchers, mail, dev seam, `IRateBudget`, the
boot-gate change, the edge-scrub pin, the register, then **1a-store** in two (#1773 the
`redis-volatile` compose, then the stores' move onto it; Amendment 2026-09-19 (2)) → **1c** #1737, preceded by the address repair (Amendment 2026-09-21), the open-registration arm (the
new-account code mail and its budget-exhausted mail, `consentRequired` + grant), `complete`,
the three `UserAccountService` gates (#1777; Amendment 2026-09-20), in five PRs → **2** #1738 the single page, 308s, copy, `setSessionCookie(id,
true)` + cookie-policy copy, Playwright → **3a** #1739 re-auth grants, in four PRs (Amendment 2026-09-21 (4)): the address-swap write order (#1790), the purpose-bound challenge substrate (#1793), re-authentication as a grant (PR 3; `/auth/verify` retired), change-email with two codes (PR 4; the mailed link and `/bekrafta-epost` retired) → **3b** #1740 in two PRs, the order binding
(Amendment 2026-09-22 (5)): Mina sidor replaces Inställningar, then re-authentication by code for delete and
change-email →
**4a** #1741 in eight PRs (Amendment 2026-09-22, #1741): A0 the tolerant reader → V1 the preamble cut → DX and DX2
the DOCX line model → A `Resume.FullName` optional, the CV half → B0 the profile read's tolerant reader → B the
account half, after 3b; RP beside them → **4b** #1742 in two PRs (Amendment 2026-09-24 (8)): U the unmap → D the drop
(opens only after all of 4a
is merged and measured live) → **5a** teardown + truth-sync + #734 re-pointed + the manual Identity `bootstrap` procedure (Klas 2026-09-18) → **5b** `password_hash`
nulled, `security_stamp` rotated in the same statement, `Down` an explicit throw (**Klas answered 2026-09-18: yes, before launch; opens only after 5a is merged and measured live on
`dev.jobbliggaren.se`**) → **6a** #1744 OAuth spine + Google · **6b** #1745 GitHub · **6c** #1746 LinkedIn
(`blocked` until keys) → **6d** #1747 **unblocked and moved into 1b's migration window**: the
columns are measured unused (`ApplicationUser.cs` + its configuration only; `HasConversion<string>`,
so no Postgres enum to clean).

**Migration order (single-owner, CLAUDE.md §6.5):** 1b → 6d → 1c-expand → 4b → 5b. `Persistence` context:
1b, 1c-expand (`DisplayNameNullable`), 4b (`UnmapJobSeekerDisplayName`, then `DropJobSeekerDisplayName`). 4a carries none (Amendment 2026-09-22, #1741). `Identity` context: 6d (two `DropColumn` + `DropIndex
ix_asp_net_users_provider_provider_user_id`), 5b (a data migration —`password_hash` is already
nullable). Exact SQL forms are `db-migration-writer`'s.

The reports: `docs/reviews/2026-09-17-auth-epic-{cto,architect,security,design}.md`, promoted with
this ADR because production decisions point at them.

## References

- Robert C. Martin, *Clean Architecture* (2017) ch. 7, 11, 13, 22 · *Clean Code* (2008) "Meaningful
  Names" · Hunt/Thomas, *The Pragmatic Programmer* (1999) ch. 7 · Fowler, *Refactoring* 2nd ed
  (2018), Parallel Change
- WP29 WP194 (Opinion 04/2012 on cookie consent exemption) · GDPR Art. 5, 6(1)(b), 7, 12, 13, 24(1),
  25(2), 30, 32, 35, 45, 49 · ePrivacy Art. 5(3) / LEK 6 kap. 18 §
- CSP Level 3 `form-action` — enforced across redirects (Chromium, Firefox)
- CLAUDE.md §2, §6.5, §9.2, §9.5, §9.6, §11 · AGENTS.md §2.2, §5, §8, §10 · DESIGN.md §§1, 5, 6,
  7, 9, 12
