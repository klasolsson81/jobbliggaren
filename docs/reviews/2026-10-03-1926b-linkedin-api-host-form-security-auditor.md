# LinkedIn api-host lever (follow-up to #1926) form round — security-auditor (report-only, before code)

- **Date:** 2026-10-03 (report returned ~2026-10-03T09:5xZ)
- **Agent:** `security-auditor`, report-only, against `.claude/worktrees/jobb-card-density-1828-79e055` at `4e0bf168a`
- **Brief:** `docs/sessions/2026-10-03-1926b-linkedin-api-host-form-brief.md`
- **Transcription:** the agent's SubagentHandback message, byte-extracted from its transcript
  (`subagents/agent-a8a9ab1ce884809ca.jsonl`); nothing below the rule is the session's.

---

## Security audit: LinkedIn's api host, the one lever that asks at every login (follow-up to #1926, epic #1732; form round before code, report-only; no PR yet)

**Status:** ✓ Approved for the form, under conditions. The decision to build it belongs to Klas.
- Form (a), which moves the authorization host and nothing else, is signed under seven conditions. One of them is a reading on the local stack before merge.
- Form (b), Supabase's pair (authorization and token both on the api host), is pre-signed only as the fallback. It applies if (a)'s reading shows the documented token endpoint refusing a code minted on the api host, and it carries three more conditions.
- Whether to build it at all is Klas's decision. One question follows, and I recommend (a) on security grounds.
- 0 Blocker, 0 Major. Two new Minors, and this change causes neither:
  - (22)'s heading has been false of its subject since today's reading;
  - our client's second authorization endpoint has no redirect-matching reading.
- Whichever answer Klas gives, the PR rides `agents-done`. My verdict on the final diff is still owed.

**Authority:**
- GDPR Art. 5(1)(f), 6(1)(b), 25(1), 32(1)(b) and Chapter V (Art. 44); EDPB Guidelines 05/2021, version 2.0.
- RFC 6749 §3.1 and §4.1.2.1; RFC 9700 §2.1 and §4.1 (exact redirect matching); OIDC Core 1.0 §3.1.3.7 (6).
- CLAUDE.md §9.5 and §9.6; AGENTS.md §5 (`Comments:`) and §12.
- ADR 0142 D8 and Amendments (18), (20), (21) and (22); `vps-deploy-stack.md` §3d; the register's LinkedIn Chapter V entry.
- LinkedIn API Terms of Use §1.3, §8.1 and §11.3. These bear on the contract, not on GDPR.

**What I read.**
- The brief in full.
- The session log `2026-10-03-1926b-closeout-linkedin-api-host.md`. It is gitignored and absent from the worktree, so I read it in the main copy. It is not a tracked file there. The brief is byte-identical in both copies (`diff`).
- My `2026-10-03-1926-form-security-auditor.md`, `…-batch2.md` and `2026-10-03-1939-pr-security-auditor.md`.
- In the worktree at `4e0bf168a`:
  - ADR 0142 Amendments (20), (21) and (22) in full;
  - `vps-deploy-stack.md` §3d in full;
  - the register's LinkedIn entries (:1407, :1486-1490, :1673-1703, :1732);
  - `LinkedInIdentityProvider.cs`, `external-login.ts` and `start/route.ts`, all whole;
  - the LinkedIn test pins and `ScriptedLinkedIn.cs` (:25-60);
  - the edge-log inventory's keys;
  - the privacy copy's LinkedIn passages (sv :33, :103, :109);
  - `local-dev-setup.md` :370-385;
  - `release-checklist.md` §2.6 point 3.5. This change does not engage M-7: it adds no data subject, opens no registration and moves no detection leg.
- Via `gh`, read-only:
  - #1926: it was closed at 09:26:17Z, and I read comments 5965185247 and 5967746641;
  - supabase/supabase#50831;
  - `supabase/auth`'s `internal/api/provider/linkedin_oidc.go`.

**What I measured myself, 2026-10-03.**
- **Where the host is bound (`git grep linkedin.com`):**
  - the adapter :33-35;
  - `external-login.ts:22`;
  - the pins: unit :97, integration :159, architecture :73;
  - `ScriptedLinkedIn.cs:36-37`;
  - `route.test.ts:15`;
  - ADR 0142 :1824, :1954 and :2178;
  - §3d :779.
- **Where it is not bound:** `oauth.spec.ts` carries no LinkedIn line, `local-dev-setup.md` names no host, and the privacy copy says "skickar vi dig till LinkedIn, som sköter inloggningen" and names no host.
- **Discovery, by `curl`.** The document is served from **both** hosts, and both copies name `www` for `authorization_endpoint` and `token_endpoint`, `api` for `userinfo_endpoint`, and no `prompt_values_supported`. LinkedIn's own metadata does not name the api host for authorization or for the token even when that host serves it.
- **LinkedIn API Terms of Use** (last revised 2022-12-13):
  - the contracting party for a developer in the Designated Countries is LinkedIn Ireland Unlimited Company;
  - §1.3 makes the Developer Documentation part of the Terms, and use of the APIs "must comply with the technical documentation";
  - §8.1 says a prior version may stop working at any time;
  - §11.3 lets LinkedIn suspend an app it believes violates the Terms.
- **`supabase/auth`:** `defaultLinkedinOIDCAPIBase = "api.linkedin.com"` (:13) supplies both `AuthURL` and `TokenURL` (:52-53). The last commit touching the file is `40d07b5f5`, of 2026-03-02.
- **supabase/supabase#50831:** open since 2026-09-24, 0 comments, labelled `bug`, `auth` and `external-issue`.
- **Event ids:**
  - 1021 is the callback finding no flow; 1022 and 1023 are the adapter's; 1027 is the start budget.
  - **No line is written for a start, or for a callback `error`.**
- **The edge-log inventory keys:** `code`, `state`, `hd` and `error_description` must never reach a stored log post; `scope`, `iss`, `error`, `error_uri`, `authuser` and `prompt` are kept.
- **Each marker anchor below occurs exactly once** (`grep -c -F`).

I did not read the box. I did not probe LinkedIn with our client. I wrote, committed, labelled and posted nothing.

### Answers

**Q1. An undocumented host is an acceptable form, because what it risks is availability, not confidentiality.**
- **What it changes.** One host, in a request we build ourselves.
  - The request carries no personal data on either host: `client_id`, `redirect_uri`, `scope`, `state` and `response_type`.
  - `api.linkedin.com` is LinkedIn's. It already receives our bearer access token for userinfo.
  - The page that takes the credentials is on `www.linkedin.com/uas/login` (measured). So the member types the LinkedIn password on LinkedIn's documented origin, where a password manager expects it.
- **What it cannot change under (a).** Who can obtain a session. The exchange, `aud`, the `sub` equality, the flag, the address invariant and the outcome table are all untouched.
- **The fail-safe.** Every failure mode fails closed, or falls back to the state (22) recorded.
  1. **The api host stops answering the request.**
     - The member sees a LinkedIn error page and stays there.
     - On the box: nothing. No callback arrives, and no start is logged.
  2. **It answers our callback with an `error`.**
     - The member sees `/logga-in` with "Inloggningen med LinkedIn slutfördes inte".
     - On the box: nothing. The web's `error` branch reaches no backend, the web keeps no request log, and the edge logs only 5xx.
  3. **The token endpoint refuses the code.**
     - The member sees the same notice.
     - On the box: EventId 1022 `Provider=linkedin`, with `TokenRefused`, `TokenInvalidRequest`, `TokenInvalidRedirectUri` or `TokenInvalidClient`.
  4. **The web image and the api image name different hosts.**
     - Every LinkedIn start answers the notice.
     - On the box: nothing.
     - This skew is measured to happen: today at 08:48:24Z the api moved to `latest` while the web stayed on `sha-4e0bf16` (#1926, comment 5967746641).
  5. **The api host starts to see the www session.**
     - Logins go through with no page, as in (22).
     - On the box it reads as an ordinary `login_succeeded … Method=LinkedIn`.
- **What detects it.** Only case 3 reaches a log. Cases 1, 2 and 4 are silent, and case 5 looks like success.
  - So the detector is a person: (23)'s lapse, read by Klas at every LinkedIn login he takes on the box.
  - Also the after-rollout reading, with both image digests equal to the merge's tag (Q3), and the event ids 1021, 1022 and 1023.

**Q2. Form (a), measured first; (b) only on a measured refusal of (a); everything else refused.**
- **(a) `AuthorizationEndpoint` only.**
  - **The register's lapse (4) does not fire.** It counts the adapter's calls, and the authorization request is made by the member's browser, not by the adapter. Token and userinfo are unchanged.
  - **Chapter V is unchanged.**
  - **The client secret still goes only to the endpoint LinkedIn's metadata names.**
  - **The comment at :31-32 becomes false for one constant**, so it must stop attributing the authorization endpoint to the discovery document. The other two constants still come from it.
  - **Open:** the mixed pair (a code minted on api, redeemed on www) is unmeasured. Q3 measures it.
- **(b) Both on api.**
  - **The secret and the code go to an endpoint** that neither copy of LinkedIn's discovery document names. Its trust rests on the host being the one LinkedIn's metadata names for userinfo.
  - **The lapses fire by their letter.** The register's (4) does, and so does (20)'s access-token lapse ("a LinkedIn endpoint other than the token endpoint and userinfo").
  - **Chapter V is re-read on the merge day, before merge.** The counterparty does not depend on the host, since the API Terms tie it to the developer's residence. A lapse is never read in the direction of not firing.
  - **(20)'s ground needs a marker.** That ground is that TLS to the pinned token endpoint authenticates the issuer (OIDC Core §3.1.3.7 (6)).
  - **Two constants become undocumented.**
  - Supabase's pair is proven to complete logins. #50831's complaint is the password page, not a failure. It is not yet proven with our client.
- **(c) Refused:**
  - a run-time fallback from api to www, which hides lapses (1) and (2) and leaves a dormant path;
  - the host as an `IOptions` value, or fetched from discovery;
  - a second (www) entry in the web start's allow-list;
  - `enable_extended_login` or any other parameter;
  - a confirmation step of our own after the callback ("Du loggas in som …"), which would show the previous member's address to whoever sits at the browser. That is a disclosure on our page.

**Q3. (i) before merge, and (ii) after it as well. (ii) alone is not enough.**
- **Why (i) is required.**
  - The merge is the rollout: the hourly image build ships it, and LinkedIn's keys are live on the box.
  - (20) merged the nonce form on an unmeasured premise, and the first login on the box failed with `NonceAbsent` and needed a follow-up PR ((21)). (a)'s mixed pair is the same kind of premise.
  - The local client is the client the box uses (m-4, sha256-equal, (21)). LinkedIn's subject ids are per client, so a local login measures the box's client and the box's subject id.
- **(i) The local reading.**
  - **Setup.**
    - Take it on the stack-owner's stack, built from the PR's commit, through **our button**, never a hand-built URL.
    - Klas signs in to LinkedIn himself, and the session enters no credential.
    - Record keys and counts only, never the values of `state`, `code` or `error_description`.
  - **Steps.**
    1. The local web start: a 302 to `https://api.linkedin.com/oauth/v2/authorization` with exactly `client_id`, `redirect_uri`, `response_type`, `scope` and `state`, with `scope=openid email` and localhost's callback.
    2. Klas is signed in to LinkedIn at www in that browser. A www control URL, built by hand with a fresh state, shows no page. If it shows one, the sitting is void and is repeated.
    3. Two logins in a row through the button. Each shows LinkedIn's sign-in page; record its host and path. After he signs in, he lands signed in.
    4. A third flow through the button, cancelled on the sign-in page. It lands on `http://localhost:3000/logga-in` with "Inloggningen med LinkedIn slutfördes inte". Record the callback's keys.
  - **Read back from the local api log over the sitting:**
    - exactly 2 × `login_succeeded … Method=LinkedIn`;
    - 0 × EventId 1022 and 0 × 1023. Under (a), that is the measurement that the documented token endpoint redeems an api-minted code;
    - 1021 at most the control's;
    - the local `linkedin` link count before and after, and the `User.ExternalLoginLinked` count.
  - **If a later commit touches the adapter, the web start or `external-login.ts`, the reading is taken again.**
  - **If 1022 shows a token cause,** (a) fails. (b) is built, and steps 1-4 are repeated.
  - **Every key in step 4 must already be in the edge-log inventory.** A new key comes back to me.
- **(i′) Minor 6's probe, before merge, under either answer** (instrument in Finding 6).
- **(ii) On the box at the first login after the rollout, with Klas.** Under (A) only, never inherited.
  1. **Before the login,** both the api and the web digests equal the merge's `sha-<short>`. One without the other refuses every LinkedIn start.
  2. **The web start,** from inside the web container: as step 1, with the box's callback, and the client id hash-equal to the api's configuration.
  3. **Klas is signed in to LinkedIn at www** in that browser: linkedin.com shows his feed.
  4. **Two logins in a row through the button,** each showing LinkedIn's sign-in page.
  5. **A Cancel there** lands on dev's `/logga-in` with the notice.
  6. **Read back:**
     - exactly 2 × `login_succeeded … Method=LinkedIn`;
     - 0 × EventId 1021, 1022 and 1023;
     - 0 `User.ExternalLoginLinked`: same client, same subject id;
     - `AspNetUserLogins` `github:1,google:1,linkedin:1`;
     - 2 accounts, 0 created, both the controller's; registration `false`.
  7. **Any other result** is (23)'s lapse (2).

**Q4. The consequence for users is Klas's decision; the question is below.**
- **For the member.** Every login through our button means signing in to LinkedIn again: a password, plus whatever second factor the member has set. That costs clicks against directive 6.
- **What it gives for #1926's "must ask".** It asks for the account, never for permission.
- **At a shared computer.** It stops the next person landing **by mistake** in the account of whoever is still signed in to LinkedIn. It does not stop someone who edits our request back to www in the address bar (§S1).
- **What LinkedIn's page shows.** The address of the member last signed in is prefilled. That is LinkedIn's page and LinkedIn's processing, not ours. Today's alternative shows the next person that member's whole account here.

### security-auditor's section

**S1. The residual from (21)/(22) is unchanged, and the PR may not say otherwise.**
- **Why it is unchanged.** The host is a property of a request, not of the client.
  - LinkedIn keeps answering the www host for our client.
  - So a request someone else builds still mints a code for a signed-in member without prompting. So does our own request with the host edited back to www.
  - At a shared computer, someone who edits it can therefore start a flow with our button, change `api` to `www`, and land in the other member's account. Our state cookie matches that flow.
  - (21)'s sentence and its code-injection residual stand as written, and (22)'s "LinkedIn: (21)'s sentence stands" stands too.
- **What the PR may claim:** that its own authorization request goes to the api host, and that on 2026-10-03 the api host showed LinkedIn's sign-in page to a member signed in at www, with no permission page after.
- **What it may not claim:** that the host is documented; that it closes, narrows or binds anything in those residuals; or that someone at a shared computer cannot reach another member's account.
- **The api host does not widen the residual,** on one condition: it must match the redirect exactly. That is Minor 6. A crafted api-host request also needs the victim's password, which makes it less useful than www.

**S2. No new PII and no new log line.**
- The host carries no personal data.
- **The success callback's keys are unchanged.** It carries `code` and `state` (measured), and both are already "must-not-reach-a-stored-log-post".
- **The Cancel's keys are read in Q3 step 4.** The 1022 template carries `Provider`, `Cause` and `Status`, never a host or a value.
- **No log line is added.** Detection therefore cannot come from one (Q1).

**S3. DoD 8 and the register.**
- **Under (a), and if Klas keeps www: the register does not change.**
  - What is received and stored, the source, the recipient and the transfer are all unchanged.
  - The privacy policy names no host, and the cookie policy names no LinkedIn cookie.
- **Under (b):**
  - lapse (4) fires and is discharged by a re-reading of Chapter V on the merge day;
  - (4) is rewritten to name both endpoints by URL, so the next reader has nothing to interpret (texts below).

**S4. Chapter V.**
- **The counterparty is unchanged.** The API Terms name the counterparty of the server's calls by the developer's residence, not by host, and the register already reads `api.linkedin.com` (userinfo) as LinkedIn Ireland's.
- **The navigation and the cookies are LinkedIn's processing.** The member's browser sends LinkedIn's own `.linkedin.com` cookies to whichever LinkedIn host it is sent to. LinkedIn Ireland is the controller for EU/EEA members, and we set and read none of it.
- **The authorization request discloses nothing personal of ours** on either host, so it is no transfer under EDPB 05/2021.
- **Result:** no lapse fires under (a). Under (b), (4) fires by its letter (S3).

**S5. The conditions I sign under.**
1. **Host only, as a constant.**
   - The authorization host is a constant in the adapter beside `Scope`. It is never an `IOptions` value, never fetched, and never derived from input.
   - The web allow-list holds exactly one LinkedIn entry, equal to it.
   - There is no www entry, no fallback and no dormant www constant.
   - Moving either side alone back to www turns a test red.
2. **Nothing else in the request.**
   - The five parameters and their values, the scope and the callback are unchanged.
   - No `prompt`, `enable_extended_login`, `login_hint` or any other parameter is added.
   - Under (a), the token and userinfo endpoints are unchanged.
   - The five-key pins stay and assert the host by literal.
3. **Measured before merge,** as in Q3 (i) and (i′), with the reading recorded in (23) before `agents-done`.
4. **No overclaim (S1).**
   - The comment at `LinkedInIdentityProvider.cs:31-32` stops attributing the authorization constant to the discovery document.
   - The literal pin at `ExternalLoginMirrorWireContractTests.cs:71-73` keeps a literal, pointing at the api host, under a name that does not say "documented".
5. **Not consent.** LinkedIn's sign-in page is LinkedIn's authentication of the member. The legal basis stays Art. 6(1)(b).
6. **The lapse set has one home,** (23), with Klas as its reader. §3d points to it and does not restate it.
7. **The documented host stays on record** in (23), with its reading date, as the target a lapse returns to.
8. **(b) only:** the register's (4) and (20)'s access-token lapse are read as fired. Chapter V is re-read before merge on the merge day.
9. **(b) only:** (4) names both endpoints by URL, and markers go on (20)'s TLS sentence and on its access-token lapse.
10. **(b) only:** a test pins the token request's URL by literal, so the secret cannot be sent anywhere else unnoticed.

The PR rides `agents-done` under either form.
- No line that M-1 (LinkedIn), Major 2 (LinkedIn) or Major 4 cites moves.
- No provider or path joins, and no outcome changes.
- Under (b) the exchange moves its endpoint, but not its checks or what it receives.
- §12's security clause clears on the pins and on my 0/0 against the final diff.

**S6. Texts, verbatim.** ‹…› is a slot, filled only from the reading it names. Any other reading comes back to me.

**(i) Amendment (23), `security-auditor`'s text.** Use Variant A or Variant B by Klas's answer.
```
**`security-auditor`, 2026-10-03: LinkedIn's api host, the one lever that asks.**
Nothing below re-grades a finding, and none of it is a §9.6 (3) acceptance
(`docs/reviews/2026-10-03-1926b-linkedin-api-host-form-security-auditor.md`).

**Measured, 2026-10-03** (#1926, comment 5967746641). Authorization URLs built by hand: our client, the box's
callback, `response_type=code`, `scope=openid email` and a fresh state that no flow held, opened in the session's
browser pane. Klas signed in himself wherever a sign-in page appeared; the session entered no credential.
- Signed in to LinkedIn, `https://www.linkedin.com/oauth/v2/authorization` showed no LinkedIn page and returned a
  code at once. `https://api.linkedin.com/oauth/v2/authorization`, with the same five parameters, showed LinkedIn's
  sign-in page on `www.linkedin.com/uas/login` with the e-mail field prefilled. After Klas signed in there, the
  callback carried `code` and `state`, and no permission page came between.
- No code was redeemed. Each callback was refused by the box's basic_auth before Next, and from 09:00:59Z to
  09:21:53Z the box logged 0 × EventId 1021, 1022, 1023 and 1028, 0 `login_succeeded` and no `User.*` or
  `Account.*` event, with the links unchanged.
- So the api host does not see the member's www session. It asks for the account's credentials, not for permission.
- LinkedIn documents only `www.linkedin.com` for authorization ("3-Legged OAuth Flow", updated 2026-05-15). Its
  discovery document, served from both hosts, names `www` for authorization and the token and `api` for userinfo
  (read 2026-10-03). Supabase's `linkedin_oidc` sends both its authorization and its token request to the api host
  (`supabase/auth`, `internal/api/provider/linkedin_oidc.go`, read 2026-10-03), and Supabase issue #50831 (open, no
  answer, read 2026-10-03) reports the sign-in page at every login.
- LinkedIn's API Terms of Use (last revised 2022-12-13, read 2026-10-03) make the Developer Documentation part of
  the Terms (§1.3) and let LinkedIn suspend an app it believes violates them (§11.3). Whether an undocumented
  authorization host conforms is a question of the LinkedIn contract, not a GDPR finding. Login by code is
  unaffected by it.

‹Variant A or Variant B›

**Our client answers at two authorization endpoints.** The api host minted a code for our client on 2026-10-03, so
(21)'s "exact redirect matching at the authorization endpoint" covers it only as measured there: ‹date›, the box's
callback accepted and ‹the controls›, on a host this box serves, refused with LinkedIn's mismatch page. A crafted
request can use either host, whichever ours uses.

‹Close A or Close B›

*(End of `security-auditor`'s text.)*
```
‹Variant A›:
```
**LinkedIn's authorization request goes to the api host, which LinkedIn does not document for it.**
- Klas's answer, put through AskUserQuestion on ‹date› ("‹question›"): **"‹label›"**, whose text was "‹option
  text›".
- Only the authorization host changes, to `https://api.linkedin.com/oauth/v2/authorization`. The request keeps its
  five parameters and their values; the scope and the callback are unchanged. ‹Form (a): The code is redeemed at the
  documented token endpoint and userinfo is read as before, so the adapter's calls to LinkedIn are unchanged. | Form
  (b): The code is redeemed at `https://api.linkedin.com/oauth/v2/accessToken`, Supabase's pair, because the
  documented token endpoint refused a code minted on the api host (below).›
- The host is a constant in the adapter and the web start's one LinkedIn entry: never a setting, never fetched, and
  the start answers no other LinkedIn host. The documented host, `https://www.linkedin.com/oauth/v2/authorization`
  (read 2026-10-03), is where a lapse returns it.
- **Read before merge, ‹date›,** through the button on the local stack at ‹commit›, with the LinkedIn client the box
  shares (m-4), Klas signed in to LinkedIn at www in that browser: ‹the local reading›.
- **What it buys, for the requests our start builds.** A member signed in to LinkedIn at www is shown LinkedIn's
  sign-in page at every login, on LinkedIn's own www origin, and enters the credentials of the account to be used. At
  a shared computer, the next person who chooses LinkedIn is no longer signed silently into the account whose
  LinkedIn session is still open in that browser (Art. 25(1), 32(1)(b)). It asks for the account, not for
  permission, and it is LinkedIn's authentication of the member, not consent to our processing; the login's basis
  stays Art. 6(1)(b). A measure, not a closed finding.
- **What it does not change.** The host is a property of a request, not of the client, and LinkedIn still answers
  the www host for our client. A request someone else builds, or ours edited back to www in the address bar, mints a
  code for a signed-in member without prompting: (21)'s sentence stands as written, and so does (21)'s
  code-injection residual. At a shared computer it guards against a mistake, not against someone who means to use
  another member's LinkedIn session while it is open in that browser. The sign-in page prefills the address of the
  member last signed in there; that is LinkedIn's page and LinkedIn's processing, and nothing of it reaches us.
- **What a failure looks like.** Each form fails closed or falls back to (22):
  - the api host stops answering the request: the member stays on a LinkedIn page, and nothing reaches the box;
  - it answers our callback with an `error`: `/logga-in` with "Inloggningen med LinkedIn slutfördes inte", and
    nothing is logged;
  - the token endpoint refuses its code: the same notice, and EventId 1022 with a token cause;
  - the web image and the api image name different hosts: every LinkedIn start answers that notice, and nothing is
    logged;
  - the api host starts to see the www session: logins go through with no page, as in (22).
- **Lapse, any of:** (1) a login through the button, by a member signed in at www, shows no LinkedIn page; (2) a
  login through the button fails at LinkedIn or at the exchange: a LinkedIn error page, a callback `error` that is
  not a Cancel, or EventId 1022 with a token cause; (3) LinkedIn documents an authorization host or a parameter that
  asks again; (4) LinkedIn tells the app's holder that the app does not follow its Developer Documentation. At (1) or
  (3) the next PR returns the host to www or to the documented lever (nothing dormant, Amendment (18)). At (2) or (4)
  a PR does so at once, and under (2) §3d's "an active row that never works is not left standing" applies until it
  merges. **Home:** this amendment. **Reader:** Klas Olsson, at every LinkedIn login he takes on the box. Nothing
  detects it automatically: a dead api host writes no line on the box.
```
‹the local reading› is exactly this sentence, with the counts as written:
"the web start a 302 to `https://api.linkedin.com/oauth/v2/authorization` with exactly the five keys and `scope=openid email`; a www control showed no page; two logins in a row through the button each showed LinkedIn's sign-in page on `www.linkedin.com/uas/login` and then signed him in; a Cancel there landed on `http://localhost:3000/logga-in` with "Inloggningen med LinkedIn slutfördes inte", the callback carrying ‹keys›; the local api logged 2 × `login_succeeded … Method=LinkedIn` and 0 × EventId 1022 and 1023‹ (a): , so the documented token endpoint redeemed codes minted on the api host›"
- ‹keys› may name only keys already in the edge-log inventory.
- Under (b), the clause in ‹ (a): …› is dropped.

‹Form (b)›, added after Variant A only if (a)'s reading showed a token cause:
```
**Form (b): the token endpoint follows, because the documented one refused the code.**
- Read ‹date› under form (a) on the local stack: ‹n› × EventId 1022 with cause ‹cause›. Form (b) was then read as
  above.
- The client secret and the code go to `https://api.linkedin.com/oauth/v2/accessToken`, which neither copy of
  LinkedIn's discovery document names. Its host is the one that document names for userinfo, which already receives
  our access token. The id_token now comes from there, so (20)'s ground for reading it without a signature (OIDC Core
  §3.1.3.7 (6)) rests on that host being LinkedIn's userinfo host. `aud` and the `sub` equality are unchanged.
- The register's lapse (4) and (20)'s access-token lapse fire by their letter. Chapter V was read again on ‹date›,
  before merge: ‹the API Terms' party clause and the app holder's residence; the BD DPA's sections 4 and 10 and
  Schedule A.2; the Privacy Policy's controller sentence›. The counterparty is unchanged, so no transfer arises, and
  (4) now names both endpoints by URL. The access token is still received once, used once against userinfo and
  dropped, never stored or logged.
```
‹Close A›:
```
**The findings stand as graded.** M-1 (LinkedIn), Major 2 (LinkedIn) and Major 4 stand unsigned, and no
acceptance exists. This PR moves no line they cite: no provider or path joins, and the flag, the address invariant,
the outcome table and every outcome are unchanged. It therefore rides `agents-done` on `security-auditor`'s verdict
against the final diff, not #1888's §12 STOPP shape.

**Lapse triggers, read for this PR: none fires**‹ (b): beyond the two read in "Form (b)"›.
- 4: no provider joins or leaves, and the `VerifiedEmail` rule, the address invariant and the outcome table do not
  change. The host changes which LinkedIn page a flow passes, not who can obtain a session.
- F10: the callback's response form, the three cookies and the continuation hop are unchanged.
- 1–3 and 5–7: untouched. Registration is closed and both accounts are the controller's (‹reading, date›).
- Chapter V: the authorization request is the member's browser's and carries nothing personal‹ (a): , and the
  adapter's calls are unchanged, so none of the register's LinkedIn conditions fires›. (21)'s binding trigger has
  not fired.

**DoD 8.** No new personal data. The host carries none, and the five parameters, the scope, the callback's keys, what
is received and what is stored are unchanged. The member's browser sends LinkedIn's own cookies to whichever
LinkedIn host it is sent to; that is LinkedIn's processing, and we set and read none. No new log line, recipient,
source or transfer. The privacy policy says LinkedIn handles the login and names no host, and the cookie policy
names no LinkedIn cookie, so neither changes. The register ‹(a): does not change | (b): changes only in lapse (4),
above›.
```
‹Variant B›:
```
**LinkedIn's authorization request stays on the documented host.**
- Klas's answer, put through AskUserQuestion on ‹date› ("‹question›"): **"‹label›"**, whose text was "‹option
  text›".
- (22)'s LinkedIn block stands, except its heading, which is corrected in place: the probe tried parameters only,
  and an undocumented host was measured to ask. Its shared-computer sentence stands as written.
- The question returns if LinkedIn documents a host or a parameter that asks again, and at trigger 1 or 2, when
  `security-auditor` reads it again with (20) and (21). **Home:** this amendment. **Reader:** Klas Olsson. Nothing
  detects it automatically.
```
‹Close B›:
```
**DoD 8.** Nothing in the product changes, so neither the privacy policy, the cookie policy nor the register does.
```

**(ii) In-place markers in ADR 0142.** Append each marker after its anchor, separated by one space. Each anchor occurs once.
1. **Under either answer.** Line 2245, anchor `limitation.**`, which closes the heading carrying "honoured no undocumented one":
   - A: `*(corrected in Amendment ‹date› (23): the probe tried parameters only. An undocumented host, `api.linkedin.com`, was measured on 2026-10-03 to show LinkedIn's sign-in page to a member signed in at www, and our authorization request goes there since (23))*`
   - B: `*(corrected in Amendment ‹date› (23): the probe tried parameters only. An undocumented host, `api.linkedin.com`, was measured on 2026-10-03 to show LinkedIn's sign-in page to a member signed in at www; by Klas's decision our request stays on the documented host)*`
2. **A only.** Line 2275, anchor `Choosing another LinkedIn account means signing out of LinkedIn first.`:
   `*(corrected in Amendment ‹date› (23): no longer for a login through our button, which shows LinkedIn's sign-in page; still for a request edited back to the www host)*`
3. **A only.** Line 2277, anchor `The question returns if LinkedIn documents a parameter that asks again.`:
   `*(answered in Amendment ‹date› (23) by an undocumented host; (23)'s lapse set replaces this trigger)*`
4. **Under either answer** (Minor 6). Line 2030, anchor `exact redirect matching at the authorization endpoint (measured 2026-09-27 and 2026-09-29).`:
   `*(Amendment ‹date› (23): measured at `www.linkedin.com`; our client also answers at `api.linkedin.com`, measured to match exactly on ‹date›)*`
5. **(b) only.** Line 1772, anchor `TLS to the pinned endpoint authenticates it.`:
   `*(Amendment ‹date› (23): the token endpoint is `api.linkedin.com`'s since then, which LinkedIn's discovery document does not name; the TLS ground rests on that host being the one it names for userinfo)*`
6. **(b) only.** Line 1786, anchor `a LinkedIn endpoint other than the token endpoint and userinfo;`:
   `*(Amendment ‹date› (23): the token endpoint is `https://api.linkedin.com/oauth/v2/accessToken` since then; this lapse fired by its letter and was read there)*`

No marker goes on (21)'s "A crafted request therefore mints a code for a signed-in member without prompting." It stays true.

**(iii) `vps-deploy-stack.md` §3d, LinkedIn.**
- **Under either answer** (Minor 6), point 4's "LinkedIn matches them exactly." becomes:
  `LinkedIn matches them exactly, at `www.linkedin.com` and at `api.linkedin.com` (ADR 0142 Amendment (23)); read both on the day, with controls on a host this box serves, never on a foreign one.`
- **Under A, the reading list's web-start bullet:** only `https://www.linkedin.com/oauth/v2/authorization` changes, to `https://api.linkedin.com/oauth/v2/authorization`.
- **Under A,** "A repeat login asks nothing." is replaced as a whole:
```
**Every login through the button asks for the account.** The authorization request goes to `api.linkedin.com`,
which LinkedIn does not document for it and which does not see a member's www session (measured 2026-10-03; ADR
0142 Amendment (23), Klas's decision). Every login the button starts shows LinkedIn's sign-in page on
`www.linkedin.com`, and, measured on 2026-10-03, no permission page follows for a member who has granted the app.
A request edited back to the www host still asks nothing. The web start and the api name the host together, so both
images run the same tag; one on each side refuses every LinkedIn start with the notice. If a login through the button
shows no LinkedIn page, or fails at LinkedIn or at the exchange, (23)'s lapse applies.
```
- **Under A,** in "A Cancel", `(read 2026-10-02)` becomes `(read 2026-10-02, and on ‹date› for a flow through the api host, on the local stack)`.
- **Under B,** "A repeat login asks nothing." gains this sentence at its end:
  `An undocumented host, `api.linkedin.com`, does ask, for the account and not for permission (measured 2026-10-03); by Klas's decision of ‹date› the request stays on the documented host (ADR 0142 Amendment (23)).`

**(iv) The register.** It changes under (b) only.
- (4) is replaced by:
  `(4) adaptern anropar någon annan LinkedIn-ändpunkt än `https://api.linkedin.com/oauth/v2/accessToken` och `https://api.linkedin.com/v2/userinfo`;`
- This bullet follows the **Åtgärd** bullet:
```
- (4) utlöstes ‹datum›, när token-anropet flyttade till `api.linkedin.com`, som LinkedIn inte dokumenterar för det
  (ADR 0142 Amendment (23)). Kap. V lästes om samma dag före merge: ‹API Terms avtalspart och appinnehavarens hemvist;
  BD DPA avsnitt 4 och 10 och Schedule A.2; integritetspolicyns mening om personuppgiftsansvarig›. Motparten är
  fortfarande LinkedIn Ireland Unlimited Company, och värden är densamma som userinfo redan anropas på.
```

### Findings

**Blocker: none. Major: none.**

#### Minor

5. **New, 2026-10-03: (22)'s heading is false of its subject since today's reading.**
   - **File:** `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\decisions\0142-passwordless-auth-one-page-code-or-link-oauth-ready.md:2244-2245`.
   - **Current:** "LinkedIn has no documented lever and honoured no undocumented one". The probe behind it tried `prompt` values only. An undocumented host, measured today, asks.
   - **Required:** marker (ii) 1, in its A or B form, under either answer.
   - **Why:** AGENTS.md §5. A factually wrong tracked doc is a defect, and it is fixed.
   - **Why only Minor:** it understates a lever and widens no exposure.
   - **Delegate to:** the session (docs).
   - **Disposal:** in the PR that records Klas's answer. If his answer brings no code PR, `senior-cto-advisor` routes it, since ADR 0065 rules out a docs-only PR.

6. **New, 2026-10-03: our client has a second, live authorization endpoint with no redirect-matching reading.**
   - **Files:** ADR 0142 :2030 ((21)'s bound) and `vps-deploy-stack.md:750` (§3d point 4). Outside the repo: LinkedIn's api host.
   - **Current:** "exact redirect matching at the authorization endpoint" was measured at www only. The api host minted a code for our client today, and a crafted request can use it whatever we build.
   - **Required:** the probe below, before merge, recorded in (23) and in marker (ii) 4, with point 4's sentence (iii).
     - **Instrument:** an authorization request on the api host with our client and `scope=openid email`:
       - the box's callback;
       - `…/api/auth/oauth/linkedin/callback/x` on dev;
       - `…/api/auth/oauth/linkedin/callbackx` on dev.
       Every control must be on a host the box serves, never a foreign one. A loose match then hands a code only to dev's basic_auth (401), and the edge writes no line for a 4xx.
     - **Expected:** the box's callback reaches LinkedIn's sign-in page, and each control gets LinkedIn's mismatch page.
     - If the api host shows the sign-in page for a control too, Klas signs in once with that control. The mismatch page must follow.
     - Record host, path and page title, never a value.
     - **If a code reaches a control,** the text comes back to me before anything merges, and I grade it then.
   - **Why:** Art. 32(1)(b); RFC 9700 §2.1 and §4.1. (21)'s code-injection residual is bounded by exact matching.
   - **Why only Minor:** it is inert while every account is the controller's, and a crafted api-host request needs the victim's password.
   - **Delegate to:** the session, with Klas if a sign-in is needed.
   - **Disposal:** in-block, under either answer, as for Minor 5.

**Carried, grade unchanged.** Minor 1 (2026-09-29, LinkedIn, extended to Google and GitHub; the apex redirect) stands. A crafted api-host request reaches the apex in the same way, after a sign-in, and it is inert while the apex completes no TLS handshake.

### Praise
- The lever was read with a control in the same sitting, both signed in and signed out, and not one code was redeemed. ✓
- The session named the mixed pair as unmeasured instead of inferring it from Supabase. ✓

### Summary
- 0 Blocker, 0 Major.
- Form (a) is signed under seven conditions. (b) is pre-signed as the measured fallback, under three more.
- A reading on the local stack before merge is a condition, not an option. The box reading follows the rollout.
- Two new Minors, 5 and 6, both in-block under either answer.
- The PR rides `agents-done`, and my verdict on the final diff is owed.
- Re-review after a fix: the same agent, report-only, scoped to the fix delta (CLAUDE.md §9.6).

Verdict-table line: "security-auditor, LinkedIn api-host form round (follow-up to #1926, report-only): ✓ form (a) signed under seven conditions, (b) pre-signed as the measured fallback under three more; reading on the local stack before merge required; 0 Blocker / 0 Major; Minor 5 new ((22)'s heading false since 2026-10-03; marker in-block under either answer), Minor 6 new (api host's redirect matching unmeasured; probe before merge); rides `agents-done`; one Klas question."

**Eskalering till Klas: ja. En fråga. Svaret avgör om något byggs och vilken text som skrivs i ADR:en och driftinstruktionen.**

**Fråga (LinkedIn).**
Text före frågan:
> Google och GitHub frågar nu efter konto och tillåtelse vid varje inloggning. LinkedIn gör det inte: den som redan är inloggad på LinkedIn i webbläsaren släpps igenom utan någon sida. Det finns en väg runt det som LinkedIn inte dokumenterar. Skickar vi inloggningen till LinkedIns andra adress, api.linkedin.com, i stället för www.linkedin.com, ser LinkedIn inte att man redan är inloggad. Det mättes i dag med vår app. LinkedIn visade sin vanliga inloggningssida på www.linkedin.com, med e-postadressen ifylld, och efter inloggningen kom man in direkt utan godkännandesida. Det är samma sak som Yobbers användare möter, eftersom Supabase använder den adressen (Supabase-ärende #50831). LinkedIn frågar alltså efter kontot men inte om tillåtelse.
>
> Ingen ny personuppgift tillkommer, och integritetspolicyn ändras inte. Jag rekommenderar (a) av säkerhetsskäl, eftersom det stänger just fallet med två personer vid samma dator som du ville stänga i #1926. Kostnaden är en ny inloggning på LinkedIn varje gång man loggar in hos oss, och den är din att väga mot "så få klick som möjligt".

Frågan: Ska LinkedIn-inloggningen gå via api.linkedin.com, så att LinkedIn frågar efter kontot vid varje inloggning?
- **(a) Ja, mät lokalt först (rekommenderas):** "Varje inloggning via vår LinkedIn-knapp visar LinkedIns inloggningssida, även för den som redan är inloggad på LinkedIn, och man loggar in på LinkedIn på nytt varje gång. Sidan visar e-postadressen för den som senast var inloggad, men ingen kommer vidare utan lösenordet. Sitter två personer vid samma dator hamnar den andra inte längre av misstag i den förstas konto. Det skyddar mot misstag, inte mot någon som medvetet ändrar adressen i webbläsaren tillbaka till www medan den första fortfarande är inloggad på LinkedIn. Adressen är odokumenterad, så LinkedIn kan när som helst sluta stödja den. Då slutar LinkedIn-inloggningen att fungera eller släpper igenom utan att fråga, och servern märker det inte själv, så det syns först när någon loggar in. LinkedIns API-villkor säger att appen ska följa deras dokumentation, och LinkedIn får stänga av en app som inte gör det. Inloggning med kod till e-posten fungerar som vanligt även då. Innan något mergas loggar du in två gånger i rad lokalt och trycker Avbryt en gång, och samma sak görs på servern efter mergen. Går det inte lokalt provas Supabases sätt, där även appens hemlighet skickas till api-adressen, och går inte heller det blir det som i dag, utan att du behöver svara igen. Slutar LinkedIn senare att fråga, eller slutar inloggningen att fungera, går inloggningen tillbaka till www."
- **(b) Nej, som i dag:** "Inloggningen stannar på LinkedIns dokumenterade adress. LinkedIn frågar bara första gången. Sitter två personer vid samma dator loggas den som klickar på LinkedIn in direkt i kontot för den som fortfarande är inloggad på LinkedIn i den webbläsaren, utan någon sida emellan, och ser det kontots cv och ansökningar. Den som vill använda ett annat LinkedIn-konto loggar först ut från LinkedIn. Ingenting byggs. Frågan kommer tillbaka om LinkedIn dokumenterar ett sätt att fråga igen, och vid lanseringen läser jag den igen."

**En avläsning, oavsett svar (inget beslut).** Innan något mergas provar sessionen api-adressen med serverns adress och två felaktiga varianter av den, alla på dev.jobbliggaren.se. LinkedIn ska då visa sin felsida för de felaktiga. Visar LinkedIn inloggningssidan även för dem behöver du logga in en gång för att avläsningen ska gå att göra.

Files:
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\sessions\2026-10-03-1926b-linkedin-api-host-form-brief.md`
- `C:\DOTNET-UTB\JobbPilot\docs\sessions\2026-10-03-1926b-closeout-linkedin-api-host.md` (gitignored; main copy only)
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\reviews\2026-10-03-1926-form-security-auditor.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\reviews\2026-10-03-1926-form-security-auditor-batch2.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\reviews\2026-10-03-1939-pr-security-auditor.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\decisions\0142-passwordless-auth-one-page-code-or-link-oauth-ready.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\runbooks\vps-deploy-stack.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\runbooks\gdpr-processing-register.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\src\Jobbliggaren.Infrastructure\Auth\ExternalLogins\LinkedInIdentityProvider.cs`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\web\jobbliggaren-web\src\lib\auth\external-login.ts`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\web\jobbliggaren-web\src\app\api\auth\oauth\[provider]\start\route.ts`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\tests\Jobbliggaren.Architecture.Tests\ExternalLoginMirrorWireContractTests.cs`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\tests\Shared\ScriptedLinkedIn.cs`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\web\jobbliggaren-web\src\lib\auth\oauth-callback-edge-log-verdicts.ts`
