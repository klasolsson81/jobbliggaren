# LinkedIn api-host lever (#1945) — security-auditor's batch after Klas's answer and the local reading (report-only)

- **Date:** 2026-10-03 (report returned ~2026-10-03T10:5xZ)
- **Agent:** `security-auditor`, report-only, the same agent as `2026-10-03-1926b-linkedin-api-host-form-security-auditor.md`,
  resumed with the session's batch (Klas's answer (a), issue #1945, C1 `bd1bb249c`, the mutations, Minor 6's probe, the
  local reading (i) with N2, the merge-day reading, five slot questions)
- **Transcription:** the agent's SubagentHandback message, byte-extracted from its transcript
  (`subagents/agent-a8a9ab1ce884809ca.jsonl`, the second hand-back); nothing below the rule is the session's.

---

## Security audit: LinkedIn's api host, the batch after Klas's answer and the local reading (#1945, follow-up to #1926; report-only)

**Status:** ✓ Approved. The form stays signed: 0 Blocker, 0 Major.
- Klas chose "(a) Ja, mät lokalt först (rekommenderas)".
- Form (a) holds on the local reading. The documented token endpoint redeemed codes minted on the api host, so form (b) is not needed, and its block, markers (ii) 5–6 and the register text fall away.
- **Conditions 1, 2 and 4 hold in the code at `bd1bb249c`. Condition 3 holds on the reading.** Conditions 5, 6 and 7 land with the texts below.
- Minor 6's probe holds, and the finding closes when its three texts land. Minor 5 is in-block, in its A form.
- Three of my sentences change only so that they stay true of what was measured: the prefilled address, and the permission page for a member without a grant. The other slots are confirmed or tightened.
- The after-rollout reading on the box (Q3 (ii)) is still owed.

**Authority:**
- GDPR Art. 6(1)(b), 25(1) and 32(1)(b); RFC 9700 §2.1 and §4.1.
- CLAUDE.md §9.6; AGENTS.md §5 (`Comments:`).
- ADR 0142 Amendments (18), (20), (21) and (22).
- My `2026-10-03-1926b-linkedin-api-host-form-security-auditor.md`.

**What I read, 2026-10-03.** I made no edits and ran no git; I read files at the worktree's checkout of `fix/linkedin-api-host-1945`.
- `LinkedInIdentityProvider.cs`, lines 28–66:
  - `TokenEndpoint` and `UserInfoEndpoint` sit under the discovery comment.
  - `AuthorizationEndpoint` has its own comment: "LinkedIn's api host, not the documented www host, so that LinkedIn asks for the sign-in (#1945, ADR 0142 Amendment (23))".
  - `BuildAuthorizeUrl` still has exactly the five tuples.
- `external-login.ts:20-24`: exactly one LinkedIn entry, the api host.
- The architecture pin, `The_linkedin_adapter_points_at_linkedins_api_host_authorize_endpoint`, and its comment.
- The unit pin (:97) and the integration pin (:159), both on the api host, both with the five-key assertion intact.
- `route.test.ts` :14–16, :168–216, including the N1 row.
- `PasswordlessSessionGrant.cs`, and ADR 0142 :1449 for `User.InboxProvenByLogin`.

A search of `src`, `web/…/src` and `deploy` for both hosts finds:
- the token endpoint on www (:33) and userinfo on api (:34) in the adapter;
- the authorization endpoint on api in the adapter (:38) and in the web (:22);
- no `www.linkedin.com/oauth/v2/authorization` literal anywhere in product code or tests. The www URL is built only inside the N1 test, by `.replace`.

**Not re-measured by me.** I did not re-run the seven mutants, since that needs edits. Condition 1's mutation clause rests on the session's report: "red exactly where R4 names".

### Answers

**1. The prefilled-address sentence: replace it.**
- In the reading, the first login prefilled the member's own address and the second showed an empty field, with the same member both times. My sentence asserted a rule that was never measured.
- In Variant A, "What it does not change", replace `The sign-in page prefills the address of the member last signed in there; that is LinkedIn's page and LinkedIn's processing, and nothing of it reaches us.` with:
```
LinkedIn's sign-in page may prefill an address: on 2026-10-03 it showed the signed-in member's own address at the
first of two logins and an empty field at the second. Whether it shows another member's address at a shared
computer is not measured. Whatever it shows is LinkedIn's page and LinkedIn's processing, and nothing of it reaches
us.
```
- Klas's option text, recorded as "whose text was", stays verbatim. It is the text he chose, not a claim of the ADR's, and the sentence above is what stands beside it.

**2. ‹the local reading›: corrected in three places.**
- **The window.** The N2 login made a third `login_succeeded`, so the counts are bounded to the two logins.
- **1021.** The count is added, since I required it.
- **The permission page.** It was absent both times, and that is the measurement my "What it buys" rests on.

The bullet's lead also changes: "that browser" had no antecedent, and the void first sitting is named, not dropped. The whole bullet now reads:
```
- **Read before merge, 2026-10-03,** through the button on the local stack at `bd1bb249c`, with the LinkedIn client
  the box shares (m-4), Klas signed in to LinkedIn at www in Brave (a first sitting in the session's browser pane was
  void: its LinkedIn session had expired, and the control showed the sign-in page): the web start a 302 to
  `https://api.linkedin.com/oauth/v2/authorization` with exactly the five keys and `scope=openid email`; a www
  control showed no page; two logins in a row through the button each showed LinkedIn's sign-in page on
  `www.linkedin.com/uas/login` and then signed him in, with no permission page; a Cancel there landed on
  `http://localhost:3000/logga-in` with "Inloggningen med LinkedIn slutfördes inte", the callback carrying `error`,
  `error_description` and `state`; for the two logins the local api logged 2 × `login_succeeded … Method=LinkedIn`
  and 0 × EventId 1021, 1022 and 1023, so the documented token endpoint redeemed codes minted on the api host.
```
- ‹Form (a)› is selected: "The code is redeemed at the documented token endpoint and userinfo is read as before, so the adapter's calls to LinkedIn are unchanged."
- The ‹Form (b)› block and markers (ii) 5–6 are dropped, and the register does not change.
- In Close A, the ‹ (b): …› clause and the ‹(b)› alternative in DoD 8 drop. The ‹ (a): …› clause in the Chapter V bullet stays. DoD 8 reads "The register does not change."

**3. N2: two of my sentences change, so that they stay true. The measurement itself stays the session's.**
- My text said, unqualified, that the lever asks "not for permission". N2 measured the permission page for a member without a grant, so the unqualified sentence is false of that path.
- In the common "Measured, 2026-10-03" block, the third bullet becomes:
```
- So the api host does not see the member's www session. It asks for the account's credentials and, for a member
  who has granted the app, not for permission.
```
- In Variant A, "What it buys", replace `It asks for the account, not for permission, and it is LinkedIn's authentication of the member, not consent to our processing; the login's basis stays Art. 6(1)(b).` with:
```
For a member who has granted the app it asks for the account, not for permission; a member who has not is shown
LinkedIn's permission page after the sign-in, as at www (measured 2026-10-03, after Klas removed the app's grant).
Either page is LinkedIn's, never consent to our processing; the login's basis stays Art. 6(1)(b).
```
- §3d's "…no permission page follows for a member who has granted the app" is already qualified, and it stands.

**4. Close A's slot: the form changes.** The sentence already says "Registration is closed and both accounts are the controller's", so the slot carries only where and when it was read:
- "(read on the box 2026-10-03T10:39:12Z)".
- If the merge falls on a later day, the slot takes that day's reading instead, never this one. That is the (e) rule: never inherited.

**5. The two-endpoints paragraph: confirmed, with the controls spelled out.**
- ‹date›: 2026-10-03.
- ‹the controls›: "`…/api/auth/oauth/linkedin/callback/x` and `…/api/auth/oauth/linkedin/callbackx` on dev.jobbliggaren.se". "`…/callbackx`" alone does not say which path was varied.
- Marker (ii) 4's ‹date› is 2026-10-03.
- §3d point 4's sentence (iii) needs no slot. The www arm answering identically supports "at `www.linkedin.com` and at `api.linkedin.com`".

**6. Conditions and grades.**
1. **Constant, one entry: holds.**
   - A constant beside `Scope`; no `IOptions` value and no fetch.
   - One LinkedIn entry in the web allow-list, with no www entry, fallback or constant.
   - Mutation clause: the session's report, not re-run by me.
2. **Nothing else in the request: holds.**
   - Five tuples, unchanged.
   - Token and userinfo endpoints unchanged.
   - No `prompt` or other parameter added.
   - The five-key pins assert the host by literal.
3. **Measured before merge: holds,** on `bd1bb249c`, under form (a).
4. **No overclaim: holds.**
   - The discovery comment now covers only the token and userinfo endpoints.
   - The architecture pin's name and comment say "api host" and "does not document".
   - The adapter's comment gives the purpose, "so that LinkedIn asks for the sign-in", which is true of our requests. It claims no binding and no documentation.
   - `route.test.ts`'s "LinkedIn's own endpoint" means the provider's own as against another provider's, and the api host is LinkedIn's.
   - The N1 comment's "measured on the box 2026-10-03T08:48:24Z" is a dated historical measurement, which is §1.6 provenance (AGENTS.md §5).
5. **Consent, the lapse set and the documented host on record:** these land with the texts above and in my form report. Each goes to me against the final diff.

**Not findings:**
- **`User.InboxProvenByLogin` at the first local login is expected.** The local controller account was unconfirmed, and LinkedIn's flag confirmed it and revoked its earlier sessions. Amendment (18) records this for GitHub (:1449) and (20) extends it to LinkedIn. It is M-1 (LinkedIn)'s, and this diff does not change it.
- **The local registration gate was open, and 0 accounts were created.**
- **The rollout skew fails closed in both directions.** A new web with an old api is pinned by N1. An old web with a new api is refused by the old allow-list.

**Ungraded caution.** The web dev server's lines for the two www controls carried a callback with `code` and `state`. Only keys belong in the record, never the values. The codes were never redeemed and LinkedIn documents a 30-minute life, so this is a habit to keep, not a finding.

**Owed after merge (Q3 (ii) of my form report).**
- Both api and web digests equal the merge's `sha-<short>`.
- The web start goes to the api host with the five keys.
- Klas, signed in at www, takes two logins through the button, each showing the sign-in page.
- A Cancel lands on dev's `/logga-in`.
- Read back:
  - 2 × `login_succeeded … Method=LinkedIn`;
  - 0 × 1021, 1022 and 1023;
  - 0 `User.ExternalLoginLinked`;
  - `github:1,google:1,linkedin:1`;
  - 2 accounts, 0 created; registration `false`.
- Anything else is (23)'s lapse (2).

### Findings

**Blocker: none. Major: none.**

#### Minor
5. **(22)'s heading is false of its subject.** Grade unchanged. It goes in-block with marker (ii) 1 in its A form, which ‹date› fills.
6. **Our client's second authorization endpoint.** Grade unchanged.
   - Measured to hold, anonymously, at 2026-10-03T09:44:36Z: both controls on dev got LinkedIn's mismatch page, with no sign-in and no code reaching a control.
   - It closes with its three texts: the (23) paragraph, marker (ii) 4 and §3d point 4.

**Carried, grade unchanged.** Minor 1 (2026-09-29, the apex redirect).

### Praise
- The void first sitting was named and repeated, not explained away. N2 then measured the one path my text had not reached. ✓

### Summary
- 0 Blocker, 0 Major.
- Form (a) holds on the local reading, and (b) falls away.
- Conditions 1–4 hold. Conditions 5–7 land with the texts.
- Three sentences of mine change to stay true, and the slots are given.
- The reading after the rollout is owed.
- Re-review after a fix: the same agent, report-only, scoped to the fix delta (CLAUDE.md §9.6).

Verdict-table line: "security-auditor, LinkedIn api host batch after Klas's (a) and the local reading (report-only): ✓ form (a) holds (mixed pair redeemed: 2 logins, 0 × 1021/1022/1023); (b) not needed; conditions 1–4 hold at `bd1bb249c`, 5–7 land with the texts; three sentences corrected to stay true (prefill; permission page without a grant); slots confirmed or tightened; Minor 6 measured to hold, closes with its texts; Minor 5 in-block; the after-rollout reading is owed; 0 Blocker / 0 Major."

**Eskalering till Klas: nej. Inget nytt beslut.** Efter mergen behövs din inloggning på servern för avläsningen: två inloggningar via knappen och en Avbryt, medan du är inloggad på LinkedIn.

Files:
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\src\Jobbliggaren.Infrastructure\Auth\ExternalLogins\LinkedInIdentityProvider.cs`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\web\jobbliggaren-web\src\lib\auth\external-login.ts`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\web\jobbliggaren-web\src\app\api\auth\oauth\[provider]\start\route.test.ts`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\tests\Jobbliggaren.Architecture.Tests\ExternalLoginMirrorWireContractTests.cs`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\tests\Jobbliggaren.Application.UnitTests\Auth\ExternalLogins\LinkedInIdentityProviderTests.cs`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\tests\Jobbliggaren.Api.IntegrationTests\Auth\LinkedInLoginTests.cs`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\src\Jobbliggaren.Application\Auth\LoginChallenges\PasswordlessSessionGrant.cs`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\decisions\0142-passwordless-auth-one-page-code-or-link-oauth-ready.md`
