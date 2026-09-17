# senior-cto-advisor — #1734 (epic #1732 part 0.5): the form of the test harness

**Date:** 2026-09-17 · **Agent:** senior-cto-advisor · **Worktree:** `C:/tmp/jbl-1734`, base `f2961a32`,
nothing committed at the time of the decision · **Input:** the session's measurement brief (full suite
with ADR 0142 D9 applied as written to all 174 call lines: `total: 1991`, `failed: 24`,
`succeeded: 1967`; six vacuous passes and one premise shift read by hand).

---

## CTO recommendation: #1734 (epic #1732 part 0.5), form of the test harness

### Decision
**Option A, tightened. There are two named service-level bootstraps, and a test class picks one by what it tests, not by whether it failed.**

1. **`RegisterAndGetSessionIdAsync(WebApplicationFactory<Program>, email?, displayName, ct)`** produces D9's target state. It creates the user with `UserManager.CreateAsync(user)` and sets `EmailConfirmed = true` before the call (the D10 form). It then calls `JobSeeker.Register` and creates a `SessionLifetime.Persistent` session. No password.
2. **`RegisterWithPasswordAndGetSessionIdAsync(factory, email?, password = DefaultTestPassword, displayName, ct)`** reproduces what today's legacy instant-login register produces. It goes through the production port `IUserAccountService.CreateUserAsync(email, password)`, so the hash is set and `EmailConfirmed` stays false. The session is `SessionLifetime.Session`.
3. **Shared core.** Both helpers share one private core: `JobSeeker.Register`, then the db save, then `ISessionStore.CreateAsync(userId, lifetime)`. That is one piece of knowledge, and it gives 1b exactly one `Register` call to change.
4. **Which classes use (2).** A class uses (2) if its subject is:
   - a password surface that ADR 0142 removes: password login, `/auth/verify`, change-password, forgot/reset-password, lockout, breached password, the password re-auth on `/me/delete` and change-email, or confirm-email-change;
   - or the `Session` profile.

   Every other class uses (1). I grepped the 98 call-site files for those surfaces (2026-09-17) and got **11 classes**:
   - the failing ones: `BreachedPassword`, `ChangeEmail`, `ChangePassword`, `ConfirmEmailChange`, `ResetPasswordConfirmsAddress`, `ResetPassword`, `VerifyCredentials`, `DeleteMe`;
   - **plus `LockoutTests`**, which passed but tested nothing;
   - **plus `RefreshSessionTests`**, whose premise shifted;
   - **plus `ForgotPasswordTests`.** It passes under D9 as written, but it then asserts `ResetMailCount(email).ShouldBe(1)` for a passwordless account. That is exactly the gap D3 requires 1c to close (`TryPreparePasswordResetAsync` returns null when there is no password hash). Left on (1), the test would lock that gap in and then break at 1c.

   The rule decides membership, not my list; re-run the grep after the rewrite.

   One correction to your brief: the failure list names **8** files, not 10 (2+6+5+2+2+1+2+4 = 24).
5. **When classes move.** A class moves from (2) to (1) in the PR that removes its password or Session premise (3a for verify and `/me/delete`, 5a for the rest). Helper (2), `DefaultTestPassword` and `LoginAndGetSessionIdAsync` are deleted in the PR that moves the last class. This is tied to the surfaces, not to a part number, so Klas's open break-glass answer cannot leave it stranded.

### Q1: acceptance criteria
- **"Mechanical, no test body edited beyond the call" holds for all 174 lines, including these 11 classes.** There the change is the helper name plus the first argument. The five positional password arguments your measurement run dropped are **put back**, because helper (2) takes them.
- **"Sessions minted by the helper are Persistent" holds for (1).** It does not hold for (2), by design. The ADR amendment records this (Q4).

### Why, against the principles
- **AGENTS.md §5 `Tests:`.** The obligation attaches to the state an assertion rests on.
  - For the 11 classes, that state is the password, the unconfirmed address or the Session profile. Today's legacy register branch produces exactly that state, and (2) produces it through the same port, so the premise stays one production can produce.
  - For every other class, having no password is incidental. They are among the 1967 that passed, so no obligation attaches.
- **Your finding 4 decides it.** A green `total:` line cannot vouch for D9 as written, because several tests passed without testing anything. So classes must be assigned by subject, never by whether they went red.
- **Parallel Change (Fowler 2018, already cited in ADR 0142).** Add (1) now, keep (2) for surfaces that still exist, and retire (2) class by class as those surfaces go. The harness reaches the target state as early as it can, but never ahead of the surface a test is testing.
- **SRP / CCP (Martin 2017, ch. 7 and 13).** Each helper has one reason to change. (2) changes only because of the password surfaces and dies with them. (1) changes only because of the passwordless account shape: 1a swaps in its port, 1b changes the signature.

### Rejected alternatives
- **B (mirror today's state everywhere).**
  - It keeps the 87 classes that don't care on an account shape production stops making at 1a, for the whole epic. Tests for parts 2–4 would then be written against that shape.
  - It also pushes the suite-wide flip into 5a, which is a production teardown PR. That is the coupling D9 was bound to prevent, just in reverse.
  - Literally zero change now is paid for with a bigger semantic change later.
- **C (edit the failing tests).**
  - It adds body edits that 3a/5a then delete.
  - Giving a passwordless user a password relies on the reset/add-password paths that D3 requires 1c to close. The test premise would depend on something production is about to be forbidden to do (§5 `Tests:`).
  - It puts two reasons for change into one PR.
- **D (one helper with parameters).**
  - It uses flag arguments (Martin, *Clean Code* 2008, ch. 3).
  - It hands about 164 callers switches they must never touch.
  - It lets any call site ask for combinations no production code produces, such as password + confirmed + Persistent.
  - The contract step would change the signature and touch every call site again.

### Q2: `DefaultTestPassword`, `LoginAndGetSessionIdAsync`, `.gitleaks.toml`
- **`DefaultTestPassword` stays**, as (2)'s default. `git grep` on 2026-09-17 finds it only in 8 test files, all among the 11 classes, plus the helper itself.
- **`LoginAndGetSessionIdAsync` stays.** Its 3 device-B calls are all in the 11 classes. It is deleted under the rule in point 5.
- **`.gitleaks.toml` is not touched.** The issue's premise is wrong:
  - The allowlist entry matches the password **value** `T3stlosen123456`, not the constant.
  - That literal appears in 14 test files other than the helper, for example `LoginTests`, `RegisterTests`, `AuthOptionsValidatorTests` and `Application.UnitTests/Auth/LoginTimingEqualizerTests`. This PR reaches none of them.
  - The entry goes when the last literal goes.

### Q3: the "no /auth/register call" check
**Use your option (ii) without the log part, plus the state checks from (i). One test class, two facts.**
- **Expose the closed host.** Add one internal member on `ApiFactory` that returns the existing cached registrations-CLOSED host as a `WebApplicationFactory<Program>`. `CreateRegistrationsClosedClient` then calls it. No new derived host is created, so the EF ceiling (`ManyServiceProvidersCreatedWarning` > 20) is not affected.
- **One fact per helper.** Run the bootstrap **against the closed host**, then check that the session works on that host (`GET /api/v1/me` returns 200). Then check the state:
  - (1): `PasswordHash` is null, `EmailConfirmed` is true, `GetAsync(id).Lifetime == Persistent`.
  - (2): `PasswordHash` is not null, `EmailConfirmed` is false, `Lifetime == Session`.
- **Name the counterfactual in the class.** `RegistrationsClosedTests` proves the closed host refuses `/auth/register`. That is what makes a successful bootstrap there mean something.
- **Rejected checks:**
  - A counting `HttpMessageHandler`: the helper holds no client, so it could only count clients the test builds itself.
  - Asserting that `ClosedHostLogs` has no entry: a missing sink produces the same empty result, so it proves nothing.
  - (i) as proof of no HTTP: a null hash stops telling the paths apart once 1a adds a passwordless registration path to `src/`.

### Q4: ADR amendment and escalation
- **Add a dated amendment to ADR 0142 in this PR, directly under D9.** That is the only place this is recorded. Rules only, no counts:
  - (a) What was measured on 2026-09-17: D9's "zero behaviour change" for every call site and "built against `IUserAccountService.CreateUserAsync`" cannot both hold together with "passwordless user".
  - (b) The two-bootstrap form, the rule for which classes use which helper, and when classes move.
  - (c) The closed-host check.
  - (d) 1a replaces (1)'s direct `UserManager.CreateAsync(user)` with D10's port, and 1b touches the shared core once.
  - (e) `.gitleaks.toml` is not part 0.5's business.
- **PR body:** one line per changed acceptance criterion, pointing at the amendment. Do not edit #1734's body.
- **No escalation to Klas.** D9 is my own bind. This touches none of Klas's four answers and neither of the open questions (D10 break-glass, 5b timing), and it stays inside the test assembly. It goes ahead on this verdict (CLAUDE.md §9.2).

### Q5: dropping `static`
**Accepted.** It is the smallest change that gives `CompanyWatchStatusByOrgNrEndpointTests.AuthenticateAsync` the dependency it now needs. Passing `factory` into a static method would change every caller. `factory` is already captured by `NewClient()`, and the build showed 0 warnings with TreatWarningsAsErrors on.

### Fixes in this PR
- **`tests/Jobbliggaren.Api.IntegrationTests/Helpers/AuthTestHelpers.cs`:** use the shape above. Each public helper's doc comment names the production state it reproduces: ADR 0142 D10's passwordless form for (1), and the flag-OFF branch of `RegisterCommandHandler` for (2). Name the ADR, not a method that doesn't exist yet. This is the §5 requirement to name the producer, and it is the only comment needed.
- **`tests/Jobbliggaren.Api.IntegrationTests/Infrastructure/ApiFactory.cs:195-211`:**
  - Delete the parts that name the helper ("RegisterAndGetSessionIdAsync (142 sites)" and "RegisterAndGetSessionIdAsync and friends"); they become false with this PR.
  - Both pins stay: the direct `/auth/register` and `/auth/login` tests still need them, and so do (2)'s device-B logins.
- **`RateLimiting/{Strict,ListRead,Me}RateLimitApiFactory.cs`:**
  - Both `PostConfigure<AuthOptions>` pins exist only because "this factory registers users (RegisterAndGetSessionIdAsync)". After this PR none of their tests do that, so delete the pins and their comments.
  - `appsettings.Development.json` sets both flags to true, which the validator accepts.
  - Measure the three rate-limit collections green. If one goes red, keep that pin, delete only the clause that names the helper, and record what needed it in the verdict table.
- **`Auth/RefreshSessionTests.cs:27`:** "Legacy-profile … (rememberMe threading ships later)" is wrong. `RegisterCommandHandler` gives `Session` when `rememberMe` is absent. Correct the comment, because it states the premise this class is routed on. It is a comment-only edit, so "no test body edited beyond the call" still holds.
- **Before reading the run:** re-run the full suite with all 11 classes on (2), then read the `total:` line.

### References
- Robert C. Martin, *Clean Architecture* (2017), ch. 7 (SRP) and ch. 13 (Component Cohesion); *Clean Code* (2008), ch. 3 "Flag Arguments"
- Martin Fowler, *Refactoring* 2nd ed. (2018), Parallel Change (as cited in ADR 0142)
- AGENTS.md §5 `Tests:` and `Comments:`; CLAUDE.md §9.2, §9.6
- ADR 0142 D3 (1c's null-hash gates), D9, D10
