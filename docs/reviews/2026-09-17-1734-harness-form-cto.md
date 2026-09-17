# senior-cto-advisor — #1734 (epic #1732 part 0.5): the form of the test harness

**Date:** 2026-09-17 · **Agent:** senior-cto-advisor · **Worktree:** `C:/tmp/jbl-1734`, base `f2961a32`,
nothing committed at the time of the decision · **Input:** the session's measurement brief (full suite
with ADR 0142 D9 applied as written to all 174 call lines: `total: 1991`, `failed: 24`,
`succeeded: 1967`; six vacuous passes and one premise shift read by hand).

**Session note on transcription (not the agent's text):** one count in the report was re-measured
before execution — "That literal appears in 14 test files other than the helper" measured **15** test
files on 2026-09-17 (`git grep -l T3stlosen123456`, `VerifyCredentialsTests.cs` included); the
conclusion it supports (`.gitleaks.toml` untouched) is unaffected. Every other load-bearing fact was
re-measured and holds.

---

## Decision

**Variant A, tightened: two named service-level bootstraps, routed per test class by *subject*, not by
whether the class went red.**

1. **`RegisterAndGetSessionIdAsync(WebApplicationFactory<Program>, email?, displayName, ct)`** — D9's
   target state: `UserManager.CreateAsync(user)` with `EmailConfirmed = true` set before the call (the
   D10 form), `JobSeeker.Register`, `SessionLifetime.Persistent`. No password.
2. **`RegisterWithPasswordAndGetSessionIdAsync(factory, email?, password = DefaultTestPassword,
   displayName, ct)`** — the state today's legacy instant-login register produces, through the
   production port `IUserAccountService.CreateUserAsync(email, password)` (hash set, `EmailConfirmed`
   false), `SessionLifetime.Session`.
3. Both share ONE private core (`JobSeeker.Register` → db save → `ISessionStore.CreateAsync(userId,
   lifetime)`): one piece of knowledge, and 1b touches exactly one `Register` call.
4. **Routing rule:** a class goes on (2) when its subject is a password surface ADR 0142 removes
   (password login, `/auth/verify`, change-password, forgot/reset-password, lockout, breached password,
   the password re-auth on `/me/delete` and change-email, confirm-email-change) or the `Session`
   profile. Everything else goes on (1). Grepped against the 98 call-site files (2026-09-17): **11
   classes** — the 8 red ones (`BreachedPassword`, `ChangeEmail`, `ChangePassword`,
   `ConfirmEmailChange`, `ResetPasswordConfirmsAddress`, `ResetPassword`, `VerifyCredentials`,
   `DeleteMe`) **+ `LockoutTests`** (vacuous pass) **+ `RefreshSessionTests`** (premise shift) **+
   `ForgotPasswordTests`**. The last passes under D9-as-written, but it asserts
   `ResetMailCount(email).ShouldBe(1)` against a passwordless account — the very gap D3 binds 1c to close
   (`TryPreparePasswordResetAsync` null on no hash). Left on (1) the test would pin that gap and break
   at 1c. The rule governs membership, not my list; re-run the grep after the rewrite. (Correction to
   the brief: the red set is **8** files, not 10 — 2+6+5+2+2+1+2+4 = 24.)
5. **Migration:** a class moves from (2) to (1) in the PR that removes its password/Session premise (3a
   for verify and `/me/delete`, 5a for the rest); (2), `DefaultTestPassword` and
   `LoginAndGetSessionIdAsync` are deleted in the PR that moves the last class. Tied to the surfaces, not
   a part number, so Klas's open break-glass answer cannot strand it.

## Q1 — acceptance

**"Mechanical, no body edits beyond the call" holds for all 174 lines, including the 11** — there it is
the helper name + first argument. **The five positional password arguments your measurement dropped are
restored** ((2) takes them). **"Persistent" holds for (1)**; for (2) it is deliberately not true —
recorded in the ADR amendment (Q4).

## Why — principles

- **AGENTS.md §5 `Tests:`**: the obligation attaches to the state the assertion rests on. For the 11 that
  state is password / unconfirmed / Session — produced today by the legacy register branch and produced
  by (2) *through the same port*, so the premise stays producible. For everyone else passwordlessness is
  incidental (they are among the 1967 that passed), so no obligation arises.
- **Your finding 4 decides it**: a green `total:` cannot vouch for D9-as-written because vacuous passes
  exist — so routing must be by subject, never by red.
- **Parallel Change (Fowler 2018, already cited in ADR 0142)**: introduce (1) now, keep (2) for the
  surfaces that still exist, retire (2) class by class as the surfaces go. The harness reaches its target
  state as early as possible without getting ahead of the surface it tests.
- **SRP/CCP (Martin 2017 ch. 7, 13)**: each helper has exactly one reason to change — (2) changes only
  with the password surfaces and dies with them; (1) changes only with the passwordless account shape
  (1a's port swap, 1b's signature).

## Rejected

- **B (mirror today's state everywhere):** leaves the 87 indifferent classes on an account shape
  production stops producing at 1a for the whole epic — parts 2–4 write new tests against the wrong
  shape — and pushes the whole-suite flip into 5a, a production teardown PR: exactly the coupling D9 was
  bound to prevent, inverted. Literal zero change is bought with a larger semantic change later.
- **C (edit the red tests):** body edits 3a/5a delete; seeding a password onto a passwordless user rests
  on the reset/add-password paths D3 binds 1c to close — a test premise production is about to be
  forbidden to produce (§5 `Tests:`); and two change-reasons in one PR.
- **D (one parameterised helper):** flag arguments (Martin, *Clean Code* 2008 ch. 3); hands ~164
  indifferent callers switches they must never touch; permits combinations no production path produces
  (password + confirmed + Persistent); the contract step then changes the signature and touches every
  call site again.

## Q2 — `DefaultTestPassword`, `LoginAndGetSessionIdAsync`, `.gitleaks.toml`

- **`DefaultTestPassword` stays** as (2)'s default. `git grep` 2026-09-17: only in 8 test files, all among
  the 11, plus the helper.
- **`LoginAndGetSessionIdAsync` stays** — its 3 device-B calls are all in the 11. Deleted per rule 5.
- **`.gitleaks.toml`: not touched. The issue's premise is false.** The allowlist entry matches the
  password *value* `T3stlosen123456`, not the constant, and that literal lives in 14 test files other
  than the helper (e.g. `LoginTests`, `RegisterTests`, `AuthOptionsValidatorTests`,
  `Application.UnitTests/Auth/LoginTimingEqualizerTests`) that this PR does not reach. It goes when the
  last literal goes.

## Q3 — the "zero /auth/register" pin

**Your (ii) without its log half, plus (i)'s state facts — one test class, two facts.**

- **Expose the closed host:** one internal member on `ApiFactory` returning the existing cached
  registrations-CLOSED host as `WebApplicationFactory<Program>` (`CreateRegistrationsClosedClient`
  delegates to it). No new derived host, so the EF ceiling (`ManyServiceProvidersCreatedWarning` > 20) is
  untouched.
- **One fact per helper:** bootstrap **against the closed host**, then assert the session authenticates
  on that host (`GET /api/v1/me` → 200), then the state: (1) `PasswordHash` null, `EmailConfirmed` true,
  `GetAsync(id).Lifetime == Persistent`; (2) `PasswordHash` non-null, `EmailConfirmed` false,
  `Lifetime == Session`.
- **Name the counterfactual in the class:** `RegistrationsClosedTests` proves the closed host refuses
  `/auth/register`; that is what makes a successful bootstrap there mean something.
- **Rejected:** a counting `HttpMessageHandler` (the helper holds no client — it could only count the
  test's own clients); `ClosedHostLogs` absence (a missing sink yields the same empty result); (i) as
  no-HTTP proof (a null hash stops distinguishing the paths once 1a gives `src/` a passwordless
  registration path).

## Q4 — ADR amendment and escalation

- **A dated amendment in ADR 0142, directly under D9, in this PR** — the only home. Rules, no counts:
  (a) measured 2026-09-17: D9's "zero behaviour change" for every call site and "built against
  `IUserAccountService.CreateUserAsync`" cannot both hold with "passwordless user"; (b) the
  two-bootstrap form, the routing rule and the migration rule; (c) the closed-host pin; (d) 1a replaces
  (1)'s direct `UserManager.CreateAsync(user)` with D10's port, and 1b touches the shared core once;
  (e) `.gitleaks.toml` is not part 0.5's business.
- **PR body:** one line per changed acceptance criterion pointing at the amendment. Do not edit #1734's
  body.
- **No escalation to Klas.** D9 is my own bind; this touches none of Klas's four answers or the open
  questions (D10 break-glass, 5b timing), and it stays inside the test assembly. Executes on this verdict
  (CLAUDE.md §9.2).

## Q5 — dropping `static`

**Accepted.** It is the minimal change that gives
`CompanyWatchStatusByOrgNrEndpointTests.AuthenticateAsync` the dependency it now needs; passing `factory`
into a static method would change every caller. `factory` is already captured by `NewClient()`, and the
build measured 0 warnings with TreatWarningsAsErrors.

## Fixes in this PR

- **`tests/Jobbliggaren.Api.IntegrationTests/Helpers/AuthTestHelpers.cs`:** the shape above. Each public
  helper's doc comment names the production state it reproduces — ADR 0142 D10's passwordless form for
  (1), the flag-OFF branch of `RegisterCommandHandler` for (2). Name the ADR, not a method that does not
  exist yet. That is §5's name-the-actor requirement and the only comment needed.
- **`tests/Jobbliggaren.Api.IntegrationTests/Infrastructure/ApiFactory.cs:195-211`:** delete the clauses
  that name the helper ("RegisterAndGetSessionIdAsync (142 sites)", "RegisterAndGetSessionIdAsync and
  friends") — false after this PR. Both pins stay: the direct `/auth/register` and `/auth/login` tests
  and (2)'s device-B logins still need them.
- **`RateLimiting/{Strict,ListRead,Me}RateLimitApiFactory.cs`:** both `PostConfigure<AuthOptions>` pins
  exist only because "this factory registers users (RegisterAndGetSessionIdAsync)"; after this PR none of
  their tests do, so delete the pins and their comments (`appsettings.Development.json` sets both true,
  which the validator accepts). Measure the three rate-limit collections green; if one goes red, keep
  that pin, delete only the clause naming the helper, and record what needed it in the verdict table.
- **`Auth/RefreshSessionTests.cs:27`:** "Legacy-profile … (rememberMe threading ships later)" is false —
  `RegisterCommandHandler` yields `Session` when `rememberMe` is absent. Correct the comment, because it
  states the premise this class is routed on. Comment-only, so "no body edits beyond the call" holds.
- **Before reading the run:** the full suite again with the 11 on (2); read the `total:` line.

## References

Robert C. Martin, *Clean Architecture* (2017) ch. 7 (SRP), ch. 13 (Component Cohesion); *Clean Code*
(2008) ch. 3 "Flag Arguments" · Martin Fowler, *Refactoring* 2nd ed. (2018), Parallel Change (as cited in
ADR 0142) · AGENTS.md §5 `Tests:`, `Comments:`; CLAUDE.md §9.2, §9.6 · ADR 0142 D3 (1c's null-hash
gates), D9, D10.
