# #1926 form round — dotnet-architect (report-only, before code)

- **Date:** 2026-10-03 (report returned ~2026-10-02T23:2xZ)
- **Agent:** `dotnet-architect`, report-only, against `c:/tmp/jbl-1926` at `0d1b94ae3` (origin/main)
- **Brief:** `docs/sessions/2026-10-03-1926-form-brief.md` + the 2026-10-03 addendum (Klas's GitHub app reading)
- **Transcription:** the agent's SubagentHandback message, byte-extracted from its transcript; nothing below the rule
  is the session's.

---

## Arkitektur-analys

*This is a report-only form ruling for #1926. I read the files in `c:/tmp/jbl-1926` at `0d1b94ae3`, together with the brief and its 2026-10-03 addendum (Klas's reading of his GitHub app). I did not read #1926 or the #1732 comments myself, so their content is as the brief gives it. I wrote nothing. One `echo` went through Bash by mistake, which my charter forbids; it read nothing and changed nothing.*

### Sammanfattning
The session's form for Google and GitHub holds, and it can be built as proposed: **0 Kritiskt, 7 Viktigt (work owed in the PR), 4 Nice-to-have.** No Clean Architecture boundary moves. `prompt` is a per-provider protocol constant inside two Infrastructure adapters, and the port, the handlers, the api and the web production code do not change.
- **Q1:** a per-adapter `internal const string Prompt` beside `Scope`, shared with nothing. `%20` is acceptable (R1).
- **Q2:** flip the pin in place inside A1 and delete "no prompt," (R2).
- **Q3:** two A1 rows change. LinkedIn's two five-key pins stay as the guard. e2e gains one observe-only line (R3, N1).
- **Q4:** no production change outside the adapters, but one web test mirror becomes false and must be fixed (R4).
- **Q5:** (22) follows (21)'s order. (17)'s callback clause and §3d's GitHub point 4 are false today. Dated readings are never rewritten (R5a, R5b).
- **Q6:** one PR, with Klas's two answers in before the docs commits (R6).

### Rekommendation

**[Viktigt] R1 (Q1): one constant per adapter, beside `Scope` and sent after it.** `src/Jobbliggaren.Infrastructure/Auth/ExternalLogins/GoogleIdentityProvider.cs:36, :51`; `GitHubIdentityProvider.cs:41, :67`
- **The constants.** Google gets `internal const string Prompt = "select_account consent";`. GitHub gets `internal const string Prompt = "select_account";`. Each is sent as `("prompt", Prompt)` directly after `("scope", Scope)`, so the web mirror (R4) has one position to copy.
- **Nothing is shared.** Each value carries only its own provider's documented meaning: two OIDC values at Google, GitHub's single account-picker value, and nothing at LinkedIn.
  - A shared constant or enum would claim a common meaning that no provider documents.
  - It would also couple adapters that my 6b ruling kept apart on policy (`docs/reviews/2026-09-26-1745-form-dotnet-architect.md:180-187`).
- **Not a port parameter.** No caller varies it per flow, and the port takes only per-flow inputs (`IExternalIdentityProvider.cs:13-14`).
- **Not an `IOptions` setting.** It is a protocol constant, like `Scope` and the endpoints. A setting would let one environment stop asking.
- **`%20` is acceptable.**
  - The same encoder already sends `scope=openid%20email` to the same endpoint.
  - `AuthEndpoints.cs:231` serialises the URL with `AbsoluteUri`, which keeps `%20`.
  - The first Google login on 2026-09-26 succeeded with it (ADR 0142 :1223-1230).
  - Google's prompt list is space-delimited, and `%20` is the RFC 3986 encoding of that space.
  - No pin on the raw wire form is needed: `+` and `%20` decode to the same value.
- **Comments:** at most one pointer line per constant, naming what the provider is made to show plus #1926 and (22). The reasons belong in (22) (AGENTS.md §5 `Comments:`).
- **The authorize-URL join stays duplicated.** Extracting it is a different change-reason; my 6b note above calls it "scheduling, not verified".

**[Viktigt] R2 (Q2): flip GitHub's pin in place, inside A1.** `tests/Jobbliggaren.Application.UnitTests/Auth/ExternalLogins/GitHubIdentityProviderTests.cs:92-97`
- **`:93-94`:** the key set becomes `["client_id", "code_challenge", "code_challenge_method", "prompt", "redirect_uri", "scope", "state"]`. Add `query["prompt"].ShouldBe("select_account");` beside the `scope` assertion.
- **`:92`:** delete "no prompt,". The line then reads "Exactly these keys: no response_type (GitHub documents none), no login, no allow_signup." This is a pure deletion, and the new assertion gets no comment.
- **No other shape.** The exact key set already lives in A1, so a second row would assert the same thing twice. No rename is needed: "the email scope alone" in the test name stays true.
- **The flip is within 6b's own terms.**
  - test-writer's A1 row left `prompt` out "om inte arkitekten väljer dem" (`docs/reviews/2026-09-26-1745-form-test-writer.md:307`).
  - My 6b rule was "documented parameters only" (`…-1745-form-dotnet-architect.md:85`), and GitHub documents `prompt=select_account` (read 2026-10-02, per the brief).
  - `RevokeAsync` is not touched, per Klas's standing decision.

**[Viktigt] R3 (Q3): two rows change, and these mutations must go red.**
- **Google A1** (`GoogleIdentityProviderTests.cs:67-70`): the key set gains `"prompt"`, placed after `code_challenge_method` in ordinal order. Add `query["prompt"].ShouldBe("select_account consent");`.
- **GitHub A1:** as in R2.
- **Assert the literal value.** Never compare with `GoogleIdentityProvider.Prompt` or `GitHubIdentityProvider.Prompt`: a comparison with the constant survives any change to its value.
- **What stays unchanged, and why:**
  - Under (a), LinkedIn's exact five-key pins (`LinkedInIdentityProviderTests.cs:100-101`, `LinkedInLoginTests.cs:161-162`) stay as they are and act as the guard.
  - No integration row pins Google's or GitHub's key set. The path adapter → `AbsoluteUri` → response body is the same for every provider.
  - `ScriptedGoogle` and `ScriptedGitHub` model only the token and API endpoints. A double that reacted to `prompt` would assert provider behaviour that nothing measures (AGENTS.md §5 `Tests:`).
- **Mutations that go red in the gating suite:**
  - the parameter deleted from either adapter → the key set;
  - Google's value cut to `select_account` or to `consent`, or replaced by `none` or `login` → the value;
  - two `prompt` pairs, which RFC 6749 §3.1 forbids and `ParseQueryString` joins as `select_account,consent`, or a comma-delimited value → the value;
  - one provider's value copied into the other → the value;
  - `prompt` moved into the token POST → T1's exact form keys (`GoogleIdentityProviderTests.cs:105-106`, `GitHubIdentityProviderTests.cs:139-140`);
  - `prompt` added to LinkedIn → both five-key pins.
- **One form pin.** Exact equality also kills `consent select_account`, which only reorders a documented list. That is the same kind of pin `scope`'s exact value already is (`:74`).
- **No test can measure the acceptance itself:** whether each provider shows its screens, and where its Cancel lands. No log on the box records a callback (brief). Klas therefore reads both in his browser at the first login after the rollout, once per provider and dated, next to the session's reading of each start's `Location` (R5b; security-auditor's (e)).

**[Viktigt] R4 (Q4): no production code outside the adapters changes; one web test mirror must.**
- **Unchanged, checked by reading:**
  - the port (`IExternalIdentityProvider.cs:13-14`);
  - the start handler, which passes the adapter's `Uri` through untouched (`StartExternalLoginCommandHandler.cs:42`);
  - the api (`AuthEndpoints.cs:231`);
  - the web start, which checks only origin, path and `state` (`start/route.ts:53-60`);
  - the callback's `error` branch, which is the same for every provider (`callback/route.ts:46`);
  - the edge-log inventory, where `prompt` already has a verdict (`oauth-callback-edge-log-verdicts.ts:61-64`);
  - the Strict harness, which builds its own authorize URL (`tests/oauth-strict/servers.ts:113-120`);
  - the flow's TTLs.
- **Owed:** `web/jobbliggaren-web/src/app/api/auth/oauth/[provider]/start/route.test.ts:11-13`.
  - `GITHUB_AUTHORIZE` is declared to be "`GitHubIdentityProvider.BuildAuthorizeUrl`'s shape", and after R1 it no longer is. Add `&prompt=select_account` after `scope=user%3Aemail`.
  - This is my R5a rule of 2026-09-29: the route reads only origin, path and `state`, so the fixture is only a mirror, and a mirror comment that is false is still a defect.
  - `AUTHORIZE` (`:10`) claims no shape and stays as it is.
- **"No FE change" holds for production FE only.** The PR stages web files, so pre-commit runs `pnpm lint` and `tsc`. No UI surface changes.

**[Viktigt] R5a (Q5): Amendment (22), and the in-place corrections in ADR 0142.**
- **Structure, in the order (20) and (21) use:**
  1. An italic preface: the form reports, what is corrected in place, and "this block records why".
  2. Klas's words verbatim, each measured as a substring of its source:
     - the four #1926 quotes;
     - his answers to the LinkedIn question and the Cancel question, with each question as it was sent.
     His "Med ett klick menar jag såklart inte ett klick" means that "one click" in (18) and (20) is read as "no code step". Their headings stay unedited.
  3. The 6c re-activation reading, transcribed (#1732 comment 5962295677; (21)'s "(e) … at the first login after the rollout"), counts only. If that comment carries the first LinkedIn login's classes, they answer (20)'s open question about D8's string-form sentence (:692-694, :1726-1728), and D8 gets a forward pointer.
  4. The provider documentation read on 2026-10-02 (CLAUDE.md §9.5).
  5. The measurements of 2026-10-02, plus Klas's app reading of 2026-10-03: localhost first, and wildcard matching off on every callback. Also the apex TLS failure.
  6. The form (`dotnet-architect`, binding): R1–R4.
  7. GitHub's Cancel, then LinkedIn, each with Klas's decision and its consequence.
  8. `security-auditor`'s text verbatim, closed by "(End of …)".
  9. The lapse triggers as read for this PR.
- **In-place corrections:**
  - **(17), :1305-1310.** "its callback URL is exactly the box's `/api/auth/oauth/github/callback`" has been false since m-4's sharing began: the app registers three callbacks, localhost first (Klas 2026-09-29; addendum 2026-10-03). Correct it with the ADR's parenthetical form; the content waits for Klas's Cancel decision. The wildcard clause is now measured as off.
  - **(16), :1220,** "The Google client is a separate one for the box". This is a dated transcription (2026-09-26T19:09Z), and when the state changed is unknown. Do not rewrite it. At most, add a forward pointer to (22)'s reading, worded the way security-auditor routes that measurement.
  - **(15)'s m-8, :1044-1045:** the same rule. It is a recommendation, not a false statement.
  - **The shared Google client:** nothing may call it accepted without Klas's own words.
  - **(21), :2041-2042:** this is her verbatim text, and hers to answer (her Q1).
  - **Implementation status, :3303-3304:** add the #1926 PR and (22) after #1929.
  - **README :87:** add a "(22) (#1926)" clause in the row's Swedish form.
- **Never edited:** the dated readings of the old key sets (:1223-1224, :1675-1677). They are provenance (AGENTS.md §1.6).

**[Viktigt] R5b (Q5): `docs/runbooks/vps-deploy-stack.md` §3d, per provider.**
- **Google:**
  - Point 3 (:588-590) is contradicted by the 2026-10-02 probes and hashes: its "exactly …" and its "a separate client for localhost" no longer describe the box. Its new text follows security-auditor's routing (her Q7).
  - "The reading" (:606-611) gains a web-start bullet in the form GitHub's and LinkedIn's lists use: a 302 to `https://accounts.google.com/o/oauth2/v2/auth` with exactly the eight keys, `scope=openid email`, S256, `prompt=select_account consent`, the box's callback, no `access_type`, and the flow cookie.
  - "Expected on the first login" gains two lines: the account chooser and the consent screen appear at every login, and a Cancel returns to the box's `/logga-in` with Google's not-completed notice (OIDC §3.1.2.6; read as in R3).
  - In the same edit, delete the `+`-alias paragraph (:615-617), which has been false since one address changed on 2026-09-26 (ADR :1226-1227). Also bring the expected provider lists (:611, :685) up to today's set.
- **GitHub:**
  - Point 4 (:655-662) says "exactly … and nothing else", which is false: three callbacks are registered. Rewrite it per Klas's Cancel option. Keep "wildcard off", which is now measured, and keep the token-expiry note.
  - The web-start bullet (:686-687) gains `prompt=select_account`.
  - "Expected" (:693-698) gains: the account picker and the authorization page appear at every login, and where a Cancel lands.
- **LinkedIn, per Klas.**
  - Under (a), add one more difference at :704-709: LinkedIn documents no way to force a screen, so a member who is signed in and has granted the app before is sent straight back. A second person on the same computer therefore signs out of LinkedIn first.
  - The sign-in Cancel names the box's callback (read 2026-10-02); the consent Cancel goes to `redirect_uri` (documented).
  - :755-757 is unchanged.
- **The processing register is gitignored.** Whatever security-auditor rules there is written in the main copy.

**[Viktigt] R6 (Q6): one PR, with Klas's two answers in before the docs commits.**
- **One change-reason:** every external login asks for the account and the permission, and Cancel returns to the site where it started (#1926).
  - The two constants, their pins, the mirror, (22), §3d and the README row go in together.
  - Splitting per provider would duplicate (22), §3d and the review panel without isolating anything: each change is one reversible constant, with no migration and no hotspot.
- **None of these needs its own PR:**
  - GitHub's Cancel is configuration and produces no diff (addendum). Its record goes into (22) and §3d.
  - The 6c transcription is (21)'s "next amendment" obligation.
  - The Google-client reading goes into (22) as a dated measurement. Any remedy is an ops step, not code (her Q7).
  - Her §12-shape ruling (her Q4) changes how the PR merges, not how many PRs there are.
- **Order:** put the LinkedIn and Cancel questions to Klas before the docs are written. ADR 0065 forbids a docs-only PR, so an answer that arrives after the merge would have to wait for the next code PR.
- **The only second change-reason** is LinkedIn under (b). If the first login reads no screen, deleting the parameter is its own follow-up PR, the way (21) followed (20).

**[Nice-to-have] R7: LinkedIn — an architecture reading for security-auditor's draft.** The question and its outcome belong to her and Klas.
- **(a)** needs no code. The five-key pins stay as the guard (R3).
- **(b)** must name its value.
  - OIDC defines `login` (re-authenticate) and `consent`. `select_account` chooses among current sessions (OIDC Core §3.1.2.1), and whether LinkedIn holds more than one session is not measured.
  - (b) is acceptable only if its deletion is written down before merge: the trigger (the first login after the rollout shows no screen to Klas, who is signed in and has granted the app), the home ((22)) and the reader (Klas). Until that reading exists, no text may say LinkedIn asks.
  - RFC 6749 §3.1 has the server ignore a parameter it does not recognise, which I did not re-read today. "Ignored" is therefore the likely reading, and then (b) costs a second PR, as the nonce did.
- **A measure-first variant** leaves nothing speculative in `src/`:
  1. The session mints a start, as it did on 2026-10-02, and appends the parameter by hand.
  2. Klas opens that URL while signed in to LinkedIn. A screen means the parameter is honoured; a straight return means it is ignored.
  3. His browser holds no flow cookie, so the callback refuses before the api is called (`callback/route.ts:52`), and no code is redeemed.
  Whether this counts as a measurement is security-auditor's call.

**[Nice-to-have] R8: GitHub's Cancel options, read architecturally.** The decision is Klas's. Per the addendum, localhost is first and wildcard matching is off on every callback.
- **(a) The box's callback first.**
  - The only host served today receives every Cancel.
  - A localhost Cancel lands on dev's `/logga-in`. That is harmless: the `error` branch answers before any cookie or the api is read (`callback/route.ts:46`).
  - The callback order is console state that no test observes, so the first Cancel after the reorder is the reading.
  - At go-live the apex cannot share an app whose first callback is another host, so it needs an app of its own.
- **(b) One app per environment.** This is the only form where every host's Cancel lands on itself, whatever GitHub's ordering rule is.
  - No `github` row moves, because the stored key is GitHub's public account id (ADR :1353-1356). LinkedIn is different: its `sub` is pairwise (§3d :728-731).
  - It needs no code, only keys and §3d steps.
  - It reopens m-4.
- **(c) Accept the current state.** Dev's Cancel keeps landing on localhost, which shows a connection error when no local server is running. The go-live consequence is the same as under (a).

**[Nice-to-have] N1: e2e.** Add `expect(authorization.searchParams.get("prompt")).toBe("select_account consent");` to `web/jobbliggaren-web/tests/e2e/oauth.spec.ts:33-38`.
- The suite is observe-only (`e2e.yml:51`, outside `ci`), so this line gates nothing.
- What it adds is the end-to-end reading through the real api and the real web start, including the `%20` path through a URL parser.
- If it is not built, name it as a skip in the PR body.

**[Nice-to-have] N2:** under (a), extend LinkedIn's absence comment (`LinkedInIdentityProviderTests.cs:98-99`) with "and no prompt, which LinkedIn does not document (ADR 0142 Amendment (22))". LinkedIn is the only provider without the parameter, so a "consistency" edit is the likely mutation. The pin already catches that edit; the comment says why the absence is deliberate.

### Referenser
- AGENTS.md §1.6, §2.1, §5 (`Tests:`, `Comments:`); CLAUDE.md §9.5, §9.6; ADR 0065.
- ADR 0142: D8 (:670-725); (15) (:1044-1045, :1223-1230); (16) (:1220, :1226-1227); (17) (:1305-1310, :1353-1356); (20) (:1726-1728, :1889-1891); (21) (:1969-1980, :2041-2042); Implementation status (:3298-3304); `docs/decisions/README.md:87`.
- `docs/runbooks/vps-deploy-stack.md` §3d (:573-778).
- Earlier reports: `docs/reviews/2026-09-26-1745-form-dotnet-architect.md`, `docs/reviews/2026-09-26-1745-form-test-writer.md` and `docs/reviews/2026-09-29-1746-nonce-form-dotnet-architect.md`.
- The provider documentation and the OIDC Core sections as the brief quotes them (read 2026-10-02). RFC 6749 §3.1, which I did not re-read today.

Files:
- `C:\tmp\jbl-1926\src\Jobbliggaren.Infrastructure\Auth\ExternalLogins\GoogleIdentityProvider.cs`
- `C:\tmp\jbl-1926\src\Jobbliggaren.Infrastructure\Auth\ExternalLogins\GitHubIdentityProvider.cs`
- `C:\tmp\jbl-1926\src\Jobbliggaren.Infrastructure\Auth\ExternalLogins\LinkedInIdentityProvider.cs`
- `C:\tmp\jbl-1926\tests\Jobbliggaren.Application.UnitTests\Auth\ExternalLogins\GoogleIdentityProviderTests.cs`
- `C:\tmp\jbl-1926\tests\Jobbliggaren.Application.UnitTests\Auth\ExternalLogins\GitHubIdentityProviderTests.cs`
- `C:\tmp\jbl-1926\tests\Jobbliggaren.Application.UnitTests\Auth\ExternalLogins\LinkedInIdentityProviderTests.cs`
- `C:\tmp\jbl-1926\tests\Jobbliggaren.Api.IntegrationTests\Auth\LinkedInLoginTests.cs`
- `C:\tmp\jbl-1926\web\jobbliggaren-web\src\app\api\auth\oauth\[provider]\start\route.test.ts`
- `C:\tmp\jbl-1926\web\jobbliggaren-web\tests\e2e\oauth.spec.ts`
- `C:\tmp\jbl-1926\docs\decisions\0142-passwordless-auth-one-page-code-or-link-oauth-ready.md`
- `C:\tmp\jbl-1926\docs\decisions\README.md`
- `C:\tmp\jbl-1926\docs\runbooks\vps-deploy-stack.md`
