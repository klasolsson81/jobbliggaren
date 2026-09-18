# ADR 0142 — Passwordless auth: one page, code or link, OAuth-ready

**Status:** Accepted for D1–D10 and the parts sequence · **D10's three questions answered by Klas on
2026-09-18** (mail outage = total login stop accepted, no break-glass, 5b deletes before launch — verbatim
under "Open — Klas decides") · **Date:** 2026-09-17 ·
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
semantics is expiry; `INCR`-before-compare, `GETDEL` on success and `SET NX` on claim are atomic
*because* of Redis; Redis is already the availability dependency of every authenticated request,
so a Postgres challenge would widen the failure surface, not narrow it; and under Art. 5(1)(e)
a self-expiring encrypted address is the data-minimising choice.)

**The port exposes the invariant, never the verbs** (architect, CLAUDE.md §2 axis 3):

```csharp
public readonly record struct ChallengeId(string Value);          // ≥128 bit, Base64Url
public enum ChallengeOutcome { Verified, Wrong, Burned, Missing } // expired == Missing
public sealed record ChallengeVerdict(ChallengeOutcome Outcome, LoginChallenge? Record);

Task PutAsync(LoginChallenge c, TimeSpan ttl, CancellationToken ct);   // burns a live record for the same address
Task<ChallengeVerdict> ConsumeAsync(ChallengeId id, string presentedCode, CancellationToken ct);
Task<bool> TryClaimAsync(string subjectKey, TimeSpan ttl, CancellationToken ct);
```

`ConsumeAsync`'s contract, in its XML doc: the counter is incremented **before** the compare; the
record is deleted atomically on a hit; `Record` is non-null **only** on `Verified`; a dummy compare
is paid even when no record exists. Hashing and comparison live in the adapter. Collapsing
`Wrong/Burned/Missing` into one answer is the handler's policy (see D3).

**`TryClaimAsync` is its own atomic `SET NX`** (`StringSet(..., When.NotExists)` on the registered
`IConnectionMultiplexer`). It **never** reuses `ICooldownGate`: `RedisCooldownGate.cs:26-27` is
read-then-write and says so — a race there costs one extra mail; at `complete` it would cost two
accounts on one address. `RedisCooldownGate` is not changed. The claim is not the home of
uniqueness either: that is `RequireUniqueEmail` plus the index, so D3's registered-meanwhile arm
handles the duplicate error even when the claim was won.

**Record shape:** `{id, emailProtected, codeProtected, linkTokenHash, isNewAddress,
registrationClosed, pendingDeletion, attempts, expiresAt}`.
**Threat model, chosen (security Major 2):** a Redis reader IS in scope. A 6-digit code has a
10⁶ preimage, so an unsalted hash protects nothing against the same reader the address is
encrypted against; therefore **the code is protected with the same DataProtector purpose as the
address**, and only the 128-bit link token is hashed. `codeProtected` is a confidentiality control
and is written as one.
**Keys** follow the delivered cooldown form, versioned: `auth/challenge/v1/{id}`,
`auth/grant/v1/{id}`, `auth/oauth-state/v1/{state}`, `budget/{scope}/v1/{hex}`. No new root
segment beside `session:`; a record-shape change costs a new segment, never a decode crash on live
records.
**TTL 15 min** for code and link (one expiry state). **3 attempts then burn.** Code and link share
one record so consuming either burns both — but a wrong `linkTokenHash` never consumes the code's
three attempts (128 bits needs no attempt budget, and a POSTing scanner must not burn the user's
code), and for `isNewAddress` the `linkTokenHash` is **null**, so `/auth/link` cannot succeed by
construction (magic link for existing accounts only — the defence is in the record, not in the
mail's content).
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
**The mint budget is a counter, not a cooldown:** a small port `IRateBudget.TryConsumeAsync(scope,
subject, limit, window)` (Redis `INCR` + `EXPIRE`, atomic). **The address normaliser has ONE
home:** `RedisCooldownGate.Key`'s `Trim().Normalize().ToUpperInvariant()` is lifted to an internal
shared function that the shipped gate delegates to, and every new hashing site calls it (security
Major 3) — a copy that forgets `ToUpperInvariant` gives 2^k independent windows for one account
(U+017F / NFD, `RedisCooldownGate.cs:43-64`). The lift is pinned by the existing
`RedisCooldownGateTests` sweep, **extended to the new keys** in 1a. `ICooldownGate.cs:16`'s
"lower-invariant" is a wrong comment and is corrected in the same PR.

### D2 — The request path never reads the account

`POST /auth/challenge {email}` = `CanDeliver` → **`IRateBudget` first, existence-independent**
(security Major 1: an over-budget mint is a **no-op that leaves the live challenge untouched** and
writes no record; consumed before any account lookup so it burns at the same rate for known and
unknown addresses) → silent per-address cooldown (`CooldownScopes.LoginChallenge`) → mint
`ChallengeId` → `ILoginChallengeDispatcher.Enqueue({challengeId, email, anonIp, ua})` → uniform
202 `{challengeId}` for known, unknown, cooled and budget-exhausted alike.

**The dispatcher is a second port and a second channel, and it returns `void`** (CTO bind 2,
architect): `ILoginChallengeDispatcher { void Enqueue(LoginChallengeDispatch) }`, its own bounded
channel instance with its own capacity and its own drop log event-id, so a forgot-password flood
cannot silently drop logins now that mail is a hard dependency of login. `IPasswordResetDispatcher`
is untouched; the DRY lives in Infrastructure as an `internal abstract BoundedDispatchChannel<T>`.
No endpoint branches on an enqueue result — there is none.

**The consumer always writes a record** (otherwise "burned" vs "never existed" is an oracle),
decides `isNewAddress`, `registrationClosed` (`isNewAddress && !RegistrationsOpen`) and
`pendingDeletion`, and sends ONE mail: existing → code + link; new → code only + *"Du skapar ett
nytt konto"*; closed → *"vi öppnar snart"* (no code); pending deletion → the restore path. Redis
down → uniform 503 (`Program.cs:304`, the shipped pattern).

**The consumer registers in the Api composition only** (ADR 0023): inside `AddIdentityAndSessions`,
never `AddCoreIdentityForWorker`. Two structural reasons, both measured: it needs
`IDataProtectionProvider` (only `AddApiDataProtection` registers one) and `ISessionStore`. **No
existing guard catches a mis-registration** — `WorkerLayerTests` scans the Worker assembly, the
consumer lives in Infrastructure, and the Worker runs `ValidateOnBuild = false`
(`Worker/Program.cs:50`). 1a adds the test pair from `AuthOptionsValidatorTests.cs:220-241` for the
new port: positive on `AddIdentityAndSessions`, negative on `AddCoreIdentityForWorker`. The
residual — a hand-written line in `Worker/Program.cs` — is caught by nothing but a reader.

`challengeId` + the submitted email live in a 15-min `__Host-jobbliggaren_login` httpOnly
`SameSite=Strict` cookie set by the Server Action, never in a URL. The link path carries its own
token and never reads this cookie.

### D3 — Verify, then branch after proof

`POST /auth/challenge/verify {challengeId, code}`. **The cookie holder is told which of
`wrong` / `expired` / `burned` happened** (design B1, option (a)): the holder minted the challenge
themselves, a record was always written, so the distinction carries no account-existence
information — that branch stays hidden until the inbox is proven. `Missing` is presented as
`expired` (the holder's challenge existed; only expiry removes it). A dummy constant-time compare
is paid on every path. Success → existing: `Persistent` session; new: `{outcome:"consentRequired",
grantToken}`; `registrationClosed` / `pendingDeletion` as outcomes without a session.
`pendingDeletion` never restores the account through login — the 30-day clock is untouched, and
restoration is via support (`HardDeleteAccountsJob.cs:28-33`).

**Grants are ONE port with `purpose` as an enum, the bindings asserted inside `Redeem`**
(architect): `GrantPurpose { LoginComplete, Reauthentication, ChangeEmail }`;
`IssueAsync(purpose, subjectKey, payloadProtected, ttl)`; `RedeemAsync(id, expectedPurpose,
expectedSubjectKey)` does `GETDEL` and returns `null` for unknown, expired, wrong purpose and wrong
subject alike, so no handler compares and none can forget. `subjectKey` is the proven address for
`LoginComplete`, `userId` for `Reauthentication`, `(userId, newEmail)` for `ChangeEmail`. TTL
10 min, single use.

`POST /auth/challenge/complete {grantToken, acceptTerms}` → `TryClaimAsync` → re-check existence
(registered meanwhile → log into the existing account; the duplicate error from
`RequireUniqueEmail` is handled even when the claim was won) → `CreatePasswordlessUserAsync` →
`JobSeeker.Register(userId, displayName, TermsAcceptance, clock)` → `Persistent` session.
**No `AspNetUsers` or `job_seekers` row is written before the acceptance exists — on the code path
and on the OAuth path** (security Major 6): an external identity waits in the grant record and
expires with it if the user abandons the consent step. Holding `sub` + address before acceptance is
Art. 6(1)(b) second limb (steps at the data subject's request prior to a contract) and holds only
while nothing is written durably.

`POST /auth/link {token}` consumes the link. Audit rows as today's login writes them;
`login_challenge_issued` only for known accounts, off the request path.

**The challenge path never calls `IsLockedOutAsync` / `AccessFailedAsync`** — its anti-automation
is the 3-attempt burn plus the mint budget, never Identity's lockout. And **1c closes two holes the
password surface leaves open until 5a** (architect, security Major 11): `ValidateCredentialsAsync`
returns `InvalidCredentials` on a null `PasswordHash` **before** `IsLockedOutAsync`/
`CheckPasswordAsync` (today `UserAccountService.cs:133` counts the failure and anyone who knows the
address can lock a passwordless account for 15 min); and `TryPreparePasswordResetAsync` returns
`null` for a null-hash user (today it would give a passwordless account a password, making
"passwordless" a starting state rather than an invariant). Byte-identical responses; no new oracle.

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
inbox-proof mechanisms and leave #706 open.

Order: re-auth against the current address runs **first** and refuses before anything is minted.
`IReauthenticatingRequest.Password` is renamed to a purpose-scoped grant (`string? ReauthGrant`);
`ReauthenticationTripwireTests` pins nothing about the member name (its three assertions are the
marker, the validator, and the behavior's namespace — measured) and only its prose is corrected.
`ReauthenticationService.VerifyCurrentUserPasswordAsync` → `VerifyCurrentUserGrantAsync`; the
soft-delete gate (#1349) stays verbatim; the `EmailNotConfirmed` normalisation sentence is deleted,
not rewritten. `DeleteAccountCommand` follows. "Så få klick som möjligt" is the directive about the
login funnel; changing the recovery vector is rare and its failure is permanent — two inboxes is two
sends, which is arithmetic.

### D6 — The consent record: a contract stamp, not Art. 7 consent

`TermsAcceptance` is a Domain value object (`JobSeekers/TermsAcceptance.cs`, a `sealed record`,
with the version constants and one total factory, `AcceptCurrent(IDateTimeProvider clock)` —
corrected 2026-09-17, amendment below). `JobSeeker.Register(Guid userId, string? displayName,
TermsAcceptance acceptance, IDateTimeProvider clock)` **replaces** the current signature — not an
overload, so the consent-less path stops being callable (§2.2); the cost is one call site in `src/`
(`RegisterCommandHandler.cs`) plus every test call site, swept by the compiler in one mechanical
commit and counted in the PR body with `git grep -c 'JobSeeker\.Register(' -- src tests`. Mapped as
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
4a, which also owns the fate of the parameter and of the event's non-nullable `DisplayName`.

### D7 — The account has no name and the CV stops requiring one

`Resume.FullNameRequired` is removed from `Create`/`ValidateContent`; auto-promote maps `null`;
B3's canonical arm stops grading a name the system never stores; the `PersonnummerInAccountName`
arm becomes unreachable; the parsed contact name is still never used (the 2026-07-16 bind stands).
Nullable-first (4a), drop (4b). The avatar becomes a neutral account icon labelled "Mina sidor"
(form in "Page form").

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

**Provider marks:** monochrome `currentColor` in `--jp-ink-1` while the buttons are inactive
(no brand requirement is triggered by a button that logs nobody in). Whether the official coloured
marks get a scoped DESIGN.md §3 exception in 6a is Klas's (see "Open — Klas decides").

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
or `Session` premise (3a for `/auth/verify` and `/me/delete`, 5a for the rest). The password
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
`EmailConfirmed = true` set before the call; no password validators run, so `PwnedPasswordValidator`
is deliberately not engaged.

**The dev seam is `POST /api/v1/dev/login-code {email}`, mapped in `MapDevEnvironmentOnlyEndpoints`
and registered by `AddDevOnlyTestingSupport`** — the `IsDevelopment()`-only pattern, **never**
`MapDevResetMyDataEndpoint`'s configuration-gated one (security Blocker 1: `DevTools:EnableResetMyData`
is on on the box since 2026-08-29; the route hands out a live login credential for an arbitrary
address, unauthenticated, so the wrong gate is a total auth bypass). No flag may widen it.
`ProductionStartupSmokeTests` gets a pair for the route in **both** polarities of
`DevTools:EnableResetMyData`, the form `confirm-email` already has. The capturing decorator calls
**`ConsoleEmailSender.IsReservedRecipient`** (the same member, never a copy) and captures nothing
for a non-reserved recipient (404), size- and TTL-bounded, holding the **code**, never the body
(#1208's gate is not reopened one layer up). The code is minted with `RandomNumberGenerator` and
rejection sampling — never `Random`, never `% 1_000_000`.

**Mail is a hard dependency of login.** The delivered boot refusal (outside Development/Test when the
registered sender cannot deliver) **drops its `RegistrationsOpen` condition** (security Major 12):
after 1a mail is needed for login, not only registration. The rule keeps asking the sender's
**capability** (`CanDeliver`), never the `Email:Provider` key. New fail-fast keys
(`Auth:LoginChallengeDispatch:Capacity`, the budget windows) follow CLAUDE.md §11's dev-boot
contract. Both existing accounts have `EmailConfirmed=true` and log in by code with no data change.

## Open — Klas decides (put to him in plain text 2026-09-17; answered 2026-09-18)

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
| Attempts per challenge | 3, counter incremented before compare, then burn | `ConsumeAsync` |
| Challenge TTL | 15 min, code and link, one expiry state | Redis TTL |
| Live challenges per address | 1 — a new mint burns the previous (only after the budget admits it) | `PutAsync` |
| Mint budget per address | 3 / 10 min and 10 / 24 h, silent, consumed before any lookup | `IRateBudget` |
| Per-IP | `AuthWrite` 20/min, unchanged | rate limiter |
| Grant TTL | 10 min, single use, purpose + subject asserted inside `Redeem` | grant port |
| OAuth state | ≤ 10 min, cookie mandatory, Redis record `GETDEL` | 6a |

Success probability per targeted address (security-auditor's arithmetic): 30 guesses/day →
1 − (1 − 10⁻⁶)³⁰ ≈ **0.003 %/day**; **1.089 %/year** under sustained attack; at ≈ 92 accounts
that is one expected takeover per year if every account is attacked continuously. This is
accepted for the product as it is today and **lapses on any of these triggers**, each requiring a
new measurement recorded in an amendment here:

1. `Auth:RegistrationsOpen=true` outside Development (the #734 flip).
2. The first registered account that is not Klas's (ADR 0132's trigger, shared, never inherited).
3. The account count passes ~90.
4. An IdP goes live (6a): the premise "mail is the only way in" falls.
5. Code length, attempt count or mint budget changes in either direction.
6. 5b lands (no password fallback remains).
7. D2's "the consumer always sends a mail" premise falls or the budget branch changes behaviour —
   the code-step resting copy (below) is written on that premise (design B2).

The rejected TOTP provider (`DependencyInjection.cs:1658-1676`) was rejected as **stateless**; the
challenge path never goes through Identity's `opts.Tokens` providers, and re-enabling the "Email"
provider would reintroduce exactly the property that was rejected.

## Page form

Bound by `design-reviewer`; part 2 renders every state below in both themes before a design verdict
(AGENTS.md §8 point 4).

- **Routes stay in `(auth)`:** SiteHeader/SiteFooter, centred `max-w-sm`, h1 in flow, no
  `jp-pagehero`, no hero gradient. Own `h1` per route: `/logga-in` "Logga in eller skapa konto" ·
  `/logga-in/kod` "Ange koden" · `/logga-in/villkor` "Skapa ditt konto" · `/logga-in/lank` "Logga in
  på Jobbliggaren". `{email}` in body text, never in `h1` or `<title>`. `robots: {index:false}` on
  kod/villkor/lank. `/registrera` → 308 `/logga-in`; `/installningar` and `/mig` → 308 `/mina-sidor`.
- **`/logga-in`, two orders switched on `GET /auth/oauth/providers`** (design M1): **empty list
  (now):** h1 → email field + hints (incl. the Art. 13 line) → **Fortsätt** (the only
  `variant="default"`) → hairline → `h2` "Andra sätt att logga in" → the three inactive rows, no
  "Eller" divider. **At least one provider live (6a):** provider buttons → divider "Eller fortsätt
  med e-post" → field → Fortsätt.
- **Provider buttons** (design M2): shadcn `Button` `variant="outline"`, never `.jp-btn` in the same
  view, never three solid fills, never a provider's brand colour as fill. Inactive =
  `aria-disabled="true"` + **kept in the tab order** + no-op click + "Kommer snart" as the visible
  text — never `disabled` (the explanation would leave the a11y tree); never `opacity` as the
  dimming (contrast ≥ 4.5:1 in both themes); `type="button"`.
- **Code step** (design M3/M4): ONE `<input>` with a visible label "Sexsiffrig kod",
  `autocomplete="one-time-code"`, `inputmode="numeric"`, `maxLength=6`, `pattern="[0-9]*"`,
  `aria-describedby`, no placeholder; six boxes are forbidden. "Skicka ny kod" reuses
  `ResendConfirmationButton`'s form exactly (disabled 60 s, countdown outside the live region,
  message in `role="status"`). "Byt e-postadress" is a link to `/logga-in`, last. Resting copy:
  *"Vi har skickat ett mejl till {email}. Följ instruktionerna i mejlet. Innehåller det en sexsiffrig
  kod skriver du in den här. Koden gäller i 15 minuter."* with the hint *"Har du redan begärt en kod
  nyss kan det vara den som gäller. Kontrollera skräpposten om du inte ser mejlet inom några
  minuter."* — the authority is the mail, so the copy is true for the closed, pending-deletion and
  budget-exhausted branches too. A warning before the last attempt: *"Ett försök kvar. Sedan behöver
  du begära en ny kod."*
- **The seven states**, channel discipline as `RegisterForm` delivers it — user-correctable →
  `role="alert"` + `aria-invalid` + focus to the field; not the user's fault but a way forward here →
  `role="status"` panel with `h2`, `tabIndex=-1`, focus moved; no way forward here → the form is
  **replaced** by the panel:

  | State | Channel | Copy | Action |
  |---|---|---|---|
  | wrong code | alert under the field | "Koden stämmer inte. Kontrollera siffrorna och försök igen." | field stays |
  | expired | status, replaces the field | "Koden har gått ut. Den gäller i 15 minuter." | "Skicka ny kod" becomes primary |
  | burned | status, replaces the field | "Du har skrivit fel kod tre gånger. Av säkerhetsskäl behöver du en ny kod." | "Skicka ny kod" primary |
  | used on another device | status, never alert, never danger colour | "Inloggningen är redan klar i ett annat fönster. Vill du logga in även här behöver du en ny kod." | "Skicka ny kod" |
  | registration closed | status, replaces the form, never danger colour | "Registreringen är inte öppen ännu. Vi hör av oss till din adress när den öppnar." | link "Till startsidan" |
  | pending deletion | status, replaces the form | "Ditt konto raderas permanent {14 apr 2026}. Fram till dess kan du få det återställt genom att mejla kontakt@jobbliggaren.se." | mail link; no "Ångra" button that does not exist |
  | resting / sent | base render, focus h1 | the resting copy above | field + "Skicka ny kod" + "Byt e-postadress" |

- **Consent step:** checkbox label *"Jag godkänner <terms>användarvillkoren</terms>."* — full stop;
  the privacy policy in a sibling sentence under the box (*"Vi behandlar dina uppgifter enligt
  integritetspolicyn."*), never inside the acceptance. "Skapa konto" the only primary. The D4
  disclosure directly above it.
- **Link landing** (design M5): `<form action={consumeLinkAction}>` with the token in a hidden
  input and a submit button — works with JS off; no `useEffect` consumption (scanners GET); this is
the simpler form, so **a live token stays in the browser history for up to 15 min** (the 303-hop
form that moves it into a short-lived cookie was not chosen);
  `Cache-Control: no-store` on GET **and** POST; `referrer: "no-referrer"` **measured** against the
  global `strict-origin-when-cross-origin` rule (a route rule that does not win is a rule that does
  not exist); expired and used share one sentence: *"Länken går inte att använda. Begär en ny kod på
  inloggningssidan."* Why a login token in a URL is accepted where #706's change-email token was
  not: 15 min against 24 h, single use, one record that burns code and link together, the Caddy
  edge scrub, `no-referrer`, `no-store`. **The edge-scrub pin is extended in 1a**
  (`CaddyfileTokenScrubbingPinTests`: the new template and `/logga-in/lank` enter `RenderedLinks()`
  and the `TokenLink` regex; the parameter is spelled exactly `token`, the filter is case-sensitive;
  5a adds a `ShouldNotBeEmpty` so the derived set can never pass vacuously).
- **"Mina sidor"** (design M6, part 3b): the trigger is a `.jp-icon-btn` like `NotificationsBell`
  with `<UserRound size={18} aria-hidden>` in `currentColor`; no tinted circle; `aria-label` "Mina
  sidor"; `jp-usermenu__name` (the email's local part shown as a name) removed; `initials()` deleted;
  `.jp-avatar` removed after a measured consumer count; the popup follows the bell's
  `aria-haspopup="dialog"` pattern; drawer and menu re-pointed to `/mina-sidor` with one label.
- **Copy:** every string in `messages/sv/` **and** `messages/en/` in the same PR (ADR 0137); retired
  keys deleted in 5a, not orphaned; no em-dash, no literal `...`; `landing.auth.free`/`fine`
  (`registrera/page.tsx:42-43`) either move under `/logga-in`'s primary or are dropped with the
  reason named in part 2's PR body. ≤ 768 px: primary and provider buttons `size="lg"`.

## Processing register and DoD 8

In the same PR as 1a, `docs/runbooks/gdpr-processing-register.md` "Behandling: Användarkonto och
autentisering" gets a new `Datafält` bullet — *Inloggningsutmaning (Redis, `auth/challenge/v1/*`,
`auth/grant/v1/*`, `auth/oauth-state/v1/*`): DataProtector-protected email and code, `linkTokenHash`,
branch flags; TTL 15 / 10 min, self-expiring, no reaper* — and its own `Retention` row; the
`Sessioner` bullet's "payload carries only non-PII" gains *"this holds for the session record, not the
challenge record"*; a line records that the challenge record is practically unreachable for
Art. 15/17 because it expires within the response time. **Copy follows data, never precedes it:**
"lösenord (hash)" (`content-legal.json:32`) is struck in **5b**, "visningsnamn" in **4b**, the
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
instead of configured. Two mails and two code entries for an email change. Seventeen PRs instead of
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
  primitives traded for a hand-built transaction protocol; PII moved from volatile to durable.
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
(`Persistence`) → **1a** #1735 (Klas's three D10 answers written into this ADR 2026-09-18 as the first commit of its PR)
challenge/verify/link, store, both dispatchers, mail, dev seam, `IRateBudget`, the boot-gate change,
the edge-scrub pin, the register → **1c** #1737 `complete`,
the two `UserAccountService` gates → **2** #1738 the single page, 308s, copy, `setSessionCookie(id,
true)` + cookie-policy copy, Playwright → **3a** #1739 re-auth grants → **3b** #1740 Mina sidor →
**4a** #1741 display name nullable, `Resume.FullName` optional → **4b** #1742 (opens only after 4a
is merged and measured live) → **5a** teardown + truth-sync + #734 re-pointed + the manual Identity `bootstrap` procedure (Klas 2026-09-18) → **5b** `password_hash`
nulled, `security_stamp` rotated in the same statement, `Down` an explicit throw (**Klas answered 2026-09-18: yes, before launch; opens only after 5a is merged and measured live on
`dev.jobbliggaren.se`**) → **6a** #1744 OAuth spine + Google · **6b** #1745 GitHub · **6c** #1746 LinkedIn
(`blocked` until keys) → **6d** #1747 **unblocked and moved into 1b's migration window**: the
columns are measured unused (`ApplicationUser.cs` + its configuration only; `HasConversion<string>`,
so no Postgres enum to clean).

**Migration order (single-owner, CLAUDE.md §6.5):** 1b → 6d → 4a → 4b → 5b. `Persistence` context:
1b, 4a, 4b. `Identity` context: 6d (two `DropColumn` + `DropIndex
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
