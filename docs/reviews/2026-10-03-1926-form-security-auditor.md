# #1926 form round — security-auditor (report-only, before code)

- **Date:** 2026-10-03 (report returned ~2026-10-02T23:4xZ)
- **Agent:** `security-auditor`, report-only, against `c:/tmp/jbl-1926` at `0d1b94ae3` (origin/main)
- **Brief:** `docs/sessions/2026-10-03-1926-form-brief.md` + two addenda (Klas's GitHub app reading and screenshot,
  2026-10-03; the apex/www TLS reading, 2026-10-02T23:04:44Z)
- **Transcription:** the agent's SubagentHandback message, byte-extracted from its transcript; nothing below the rule
  is the session's.

---

## Security audit: #1926, the account and the permission at each provider, and where a Cancel lands (form round before code, report-only; no PR yet)

**Status:** ✓ Approved for the form before code.
- Google's and GitHub's `prompt` are signed under four conditions (Q1).
- 0 Blocker, 0 Major.
- Three Minors: two carried, one new. This change causes none of them.
- Three questions for Klas. None blocks the code, but each decides a text in Amendment (22) or §3d, so they must be asked before the docs are written.
- The PR rides `agents-done` (Q4). My verdict on the final diff is still owed (§9.2).

**Authority:**
- GDPR Art. 5(1)(f), 6(1)(b), 25(1) and 32(1)(b).
- RFC 6749 §3.1 and §4.1.2.1; RFC 9700 §4.5; OIDC Core 1.0 §3.1.2.1 and §3.1.2.6.
- CLAUDE.md §9.5 and §9.6; AGENTS.md §5 (`Comments:`) and §12.
- ADR 0142 D8 and Amendments (15)–(21); `vps-deploy-stack.md` §3d.

**What I read.**
- The brief in full, and the coordinator's two addenda:
  - Klas's reading of the GitHub app page, and his screenshot of it, both 2026-10-03;
  - the apex/www TLS reading of 2026-10-02T23:04:44Z.
- In `c:/tmp/jbl-1926` at `0d1b94ae3`:
  - ADR 0142 D8, Amendments (14)–(21) and the attempt budget's seven triggers;
  - `vps-deploy-stack.md` §3d, whole;
  - the three adapters, whole; the web start and callback routes; `oauth-callback-edge-log-verdicts.ts`;
  - the three `BuildAuthorizeUrl` pins;
  - the privacy copy's provider passages, and the register's provider and Chapter V entries;
  - `release-checklist.md` §2.6 point 3.5, per my charter (this change does not engage M-7);
  - my reports of 2026-09-25, 2026-09-26, 2026-09-27 and 2026-09-29.
- Via `gh`: #1926 with comment 5962296021; #1732 comment 5962295677; #734's body.

**What I measured myself, 2026-10-03.**
- **git:**
  - `git diff` over `src/Jobbliggaren.Infrastructure/Auth/ExternalLogins/`, `src/Jobbliggaren.Application/Auth/ExternalLogins/` and `web/jobbliggaren-web/src/app/api/auth/oauth/` is empty for `9171326f..5307e37`, `5307e37..aedfb7e` and `aedfb7e..0d1b94ae3`, and each commit is an ancestor of the next.
  - `git log cb9f00bb..HEAD` over the callback route, `continuation-document.ts`, `external-login-responses.ts`, `cookie-names.ts`, `session.ts` and `login-flow.ts` is empty.
- **#1732's comments:**
  - None contains "wildcard".
  - The 6a activation comment (5849039714) calls the box's Google client separate, "carrying only the dev and prod redirect URIs". It cites "Klas's answer of 2026-09-26", with no hash reading.
  - The 6b activation comment (5853598143) said the same of GitHub's app. The next comment (5854064737) found that false by hash.
- **Documentation, read 2026-10-03.** Each matches the brief's quotes:
  - GitHub, "Authorizing OAuth apps" (the page shows no date) and "Troubleshooting authorization request errors";
  - Google, "Using OAuth 2.0 for Web Server Applications", and "OpenID Connect" (last updated 2026-06-15);
  - OIDC Core §3.1.2.1 and §3.1.2.6;
  - RFC 6749 §3.1, which says "The authorization server MUST ignore unrecognized request parameters", and §4.1.2.1;
  - LinkedIn, "3-Legged OAuth Flow" (`updated_at` 2026-05-15): five parameters and no `prompt`. A member with an existing grant skips the screen, and a cancel goes to `redirect_uri`.

I did not read the box, and I wrote, committed, labelled and posted nothing.

### Answers

**Q1. Google's and GitHub's `prompt` are signed under four conditions, and they change no residual I hold.**
- **Klas's words map onto the form.** `select_account` answers "den måste ju fråga vilket google konto", and `consent` answers "den skall fråga om tillåtelse". Google documents `prompt` as a space-delimited list in which only `none` must stand alone, so the two values may be combined (OIDC Core §3.1.2.1 agrees).
- **`prompt` is a parameter of the request, not a setting of the client.** It governs the requests our start builds, and nothing else. A request someone else builds for our client carries whatever its builder chose, or no `prompt` at all.
  - **LinkedIn:** (21)'s sentence stands word for word.
  - **Google:** the same shape. An account that has consented to our client is not prompted again by a request without `prompt`. Google documents that it prompts "only the first time your project requests access". Declared, not measured.
  - **GitHub:** a crafted request still needs a click, because the revocation leaves no authorization to skip on, and `prompt` does not change that. A failed revocation (EventId 1028) would leave one, so 1028 stays in every reading.
  - What keeps such a code out of other hands is unchanged: the provider's redirect matching, the callback's `no-referrer` and zero subresources, and no log line that carries a code.
- **What `prompt` gives**, for our own requests only: at Google and GitHub, someone at a shared computer now sees which account is about to be used before any session exists. Today they are signed silently into whichever account's provider session is still open (Art. 25(1), 32(1)(b)). It is a measure, not a closed finding.
- **No finding in the form.**
  - With `access_type` absent, Google's documented default `online` holds and no refresh token is issued. Chapter V lapse (4) is untouched.
  - Google may echo `prompt` and `authuser` on the callback. Both are already "kept" in the edge-log inventory, and the callback reads neither.
- **Conditions:**
  1. **A constant in code.** `prompt` is a constant in each adapter beside `Scope`. It is never an `IOptions` value and never derived from request input, because a value settable at run time can be set to `none` or dropped. Google's value is exactly `select_account consent`; GitHub's is exactly `select_account`.
  2. **No other authorization parameter.** In particular, never:
     - `login_hint` or `login`: either puts an address or a user name in a URL, and GitHub's login name is never read or stored;
     - `access_type`, `include_granted_scopes` or `allow_signup`;
     - a scope change.

     The exact-key pins carry `prompt` (eight keys for Google, seven for GitHub) and assert its decoded value. GitHub's pin comment keeps "no login, no allow_signup". LinkedIn's five-key pin stays unless Klas's answer measures a value.
  3. **No overclaim.** No text in the diff (code comments, tests, the ADR or §3d) says that `prompt` binds or limits a request someone else builds, or a code.
  4. **Not our consent.** No text calls a provider's permission page the user's consent to our processing. It is the provider's authorization of our client, and the login's basis stays Art. 6(1)(b), as the privacy policy states.

**Q2. Lapse triggers: confirmed, none fires.**
- **Trigger 4.** No provider joins or leaves. The `VerifiedEmail` rule, the address invariant and the outcome table are unchanged. `prompt` changes which provider pages a flow passes, not who can obtain a session.
- **F10.**
  - The form changes none of the three things F10 watches: the callback's response form, the three cookies and the continuation hop.
  - No commit since PR P (`cb9f00bb`) has touched them either, per my `git log`.
  - So the Safari reading is not voided, and a successful Safari login after this PR deploys takes it.
- **The rest:**
  - Triggers 1–3 and 5–7 are untouched.
  - No Chapter V lapse fires: Google's (4), GitHub's (4)–(5) and LinkedIn's (3)–(4) each need a scope, an endpoint or a stored token to change, and none does.
  - (21)'s binding trigger has not fired. LinkedIn's access-token lapse has not fired either: the session's search on 2026-10-02 found no revocation documented.
  - The edge-log inventory gains no key.
- The Google-client question (Q7) decides a §3d condition, not a trigger.

**Q3. (e) and the box readings.**
- **Before merge, no trigger requires a reading.**
  - Trigger 4 does not fire, and the merge changes no login's outcome.
  - The reading of 2026-10-02T22:54–22:58Z goes into (22) with its date and is never re-dated.
  - A portal change Klas makes for Q5 or Q7 is read when he makes it. Q7's (b) is a box step under §3d's Google activation, with its reading.
- **At the first login after the rollout.** Klas is at the browser and the session reads the box read-only; use Safari if one is at hand.
  1. Before the login, the running api and web digests equal the merge's `sha-<short>` tag.
  2. The web start, from inside the web container:
     - Google: exactly `client_id, code_challenge, code_challenge_method, prompt, redirect_uri, response_type, scope, state`, with `prompt` decoding to `select_account consent` and no `access_type`.
     - GitHub: exactly `client_id, code_challenge, code_challenge_method, prompt, redirect_uri, scope, state`, with `prompt=select_account`.
     - LinkedIn: the five keys, or as Klas's answer decides.
     - Each client id is hash-equal to the api's configuration, and the flow cookie's attributes are unchanged.
  3. Klas's logins, with the browser stated:
     - two Google logins in a row, each showing the account chooser and then the permission page;
     - two GitHub logins in a row, each showing the account picker and then the authorization page;
     - LinkedIn, as his answer decides.
  4. A Cancel on Google's permission page, on GitHub's authorization page, and on LinkedIn's page if one shows. Record the host and path it lands on, and the notice. Query keys may be recorded, never the values of `state`, `code` or `error_description`.
  5. Read back:
     - `login_succeeded … Method=` by provider;
     - EventId 1022, 1023 and 1028 since the recreate, each 0 or explained by its cause;
     - `AspNetUserLogins` still `github:1,google:1,linkedin:1`, with 0 `User.ExternalLoginLinked` and 0 accounts created;
     - both accounts still the controller's, and registration still `false`.
  6. **Done when:** Google and GitHub show their pages at every login, and every Cancel lands on the host its flow started on. A Cancel that lands elsewhere keeps #1926 open.
- **GitHub's Cancel can be measured as soon as the app changes,** with no rollout: GitHub already shows its authorization page at every login.

**Q4. §12 shape: the PR rides `agents-done`, not #1888's STOPP shape.**
- **Why it is not a STOPP.**
  - The STOPP shape was taken when a merge created a standing Major (#1904), or made one reachable or gave it a new path (#1888, #1919, #1929).
  - This change does neither. No line that M-1 (GitHub, LinkedIn), Major 2 or Major 4 cites moves. No provider or path joins, and no outcome changes.
  - Major 4's path still states nothing about persistence, exactly as before.
- **What gates the merge.** §12's security clause gates on tests (the key pins) and on my 0 Blocker / 0 Major against the final diff.
- **What brings the question back.**
  - A final diff that reaches past the authorization request's parameters, the pins and the docs: the exchange, the outcome, the callback or a scope.
  - A LinkedIn answer that turns into a step of our own. That would be a separate PR with its own form round.

**Q5. GitHub's Cancel: each option, read with the addenda.**
- **What is measured.**
  - The app lists three redirect URIs, each ending `/api/auth/oauth/github/callback`: `http://localhost:3000` first, then dev, then the apex.
  - "Allow wildcard matching" is unchecked on all three, and so is "Enable Device Flow" (Klas's screenshot of the app page, 2026-10-03).
  - #1732 holds no earlier wildcard reading, so §3d point 4's wildcard condition is measured to hold for the first time.
  - The one measured Cancel landed on the first URI.
  - GitHub documents that a refusal goes to "the registered callback URL", and that a request without `redirect_uri` goes to "the first callback URL configured".
- ⚠ **The page's order is also alphabetical.** Every `http://` sorts before every `https://`, and `dev` before `jobbliggaren`. So the page cannot show whether GitHub's "first" follows the order of entry. Reordering is therefore measured by a Cancel, never by the page.
- **Why the order is a security matter.** The first URI is also where GitHub sends a code for a request that names no redirect. It must always be a host we serve.
- **(a) The box's callback first** (my recommendation, if GitHub allows it).
  - Dev's Cancel lands on dev.
  - A localhost Cancel also lands on dev, which is harmless: it carries no code, and the callback answers any `error` with the notice. It reaches no backend and is logged nowhere on the box.
  - A code sent without a redirect goes to our own callback, which refuses it for lack of the flow cookie. That is equivalent to today, where it goes to Klas's own loopback.
  - If localhost stays first after Klas removes and re-adds it, (a) is unavailable.
- **(b) One app per environment** is the cleanest form.
  - Every refusal lands where it started, and the box's secret leaves the developer machine.
  - It reopens m-4, which is Klas's decision.
  - GitHub's `provider_key` is the global numeric user id, so a new app keeps the existing links.
- **(c) Accept it.**
  - Dev's Cancel keeps landing on Klas's own loopback, with no code. That is harmless.
  - #1926 is then not met for GitHub, and this state must not survive the flip.
- **Klas's suggestion, the apex first now: not yet.**
  - The apex is not the box, and it completed no TLS handshake (2026-10-02T23:04:44Z).
  - Dev's Cancel would land on an unreachable host, and a code sent without a redirect would go to a host we do not serve.
  - At the flip, when the apex is the box, it is right.
- **#734: yes, one row** (text in Q9 (iv)). It binds the outcome, not the mechanism; the mechanism stays Klas's under m-4.

**Q6. LinkedIn.**
- **(b) as proposed is not an adequate instrument on its own.** That is: sending the parameter from the code and keeping it if Klas's login on the box shows a page.
  - LinkedIn documents three reasons for its window: a first request, a timed-out request, and a revoked grant. Without a control in the same sitting, a page after the rollout cannot be told apart from those.
  - After the rollout the box carries only the new build, so the control cannot be taken there.
  - It also ships an undocumented parameter in an auth adapter before anyone knows it does anything.
- **The acceptable form is a probe before code.**
  - The session builds authorization URLs by hand: our client id, the box's callback, `response_type=code`, `scope=openid email`, and a fresh random state that no flow holds.
  - Klas opens them in his browser while signed in to LinkedIn: first the control, without `prompt`, then one URL each with `prompt=login`, `prompt=consent` and `prompt=select_account`.
  - This is harmless. RFC 6749 §3.1 says an unrecognised parameter MUST be ignored. A probe's code lands on our callback with a state that no cookie matches, is refused before any backend, and is never exchanged.
- **How the probe is read.**
  - A value is honoured if LinkedIn shows a page with it (sign-in, account choice or consent) in the same sitting in which the control showed none.
  - If the control itself shows a page, the sitting is void and is repeated.
  - Afterwards, the probe window must show 0 `Method=LinkedIn` logins and 0 × EventId 1022.
- **What follows from it.**
  - If one value is honoured, only that value is built. It is recorded as undocumented and measured honoured, with a lapse: a repeat login on the box that shows no page deletes it in the next PR. Tests pin only our URL.
  - If no value is honoured, the fallback Klas chose applies, with no second question.
- The only lever independent of LinkedIn would be a step of our own after the callback. I do not propose it.
- The question to Klas is in the escalation block.

**Q7. The Google client.** I grade it Minor, as m-8 was (finding 1). It is pre-existing ops state that this change did not create, so it does not gate merge.
- **It needs Klas.**
  - Only he can say whether the shared client is his choice, as it is for GitHub and LinkedIn, or a state to repair.
  - An acceptance is never inferred. None is written down for Google, and the record says the opposite.
- **Amendment (16)'s sentence rested on his answer of 2026-09-26, not on a hash.** So the record says "not known whether it held", never "changed".
- **What lands in this PR.** (22) records the measurement and his answer, with three in-place markers. §3d point 3 is rewritten to match his answer. The texts are in Q9.
- **Optional bound, if wanted.** Compare the hash prefix of `AUTH_OAUTH_GOOGLE_CLIENT_ID` in each dated `deploy/.env.bak.*` on the box with today's. It is read-only and reports counts only.

**Q8. DoD 8: confirmed, nothing changes.**
- No new personal data: `prompt` carries none. The scopes, the endpoints, what is received and what is stored are unchanged.
- No new log line, recipient, source or transfer.
- The privacy and cookie copy describe no provider page. "Vi sparar ingen behörighet till ditt Google-konto" stays true.
- The register does not change, except under LinkedIn (c): its LinkedIn line then records the deactivation date.

**Q9. Texts, verbatim.** ‹…› marks what the session fills in from Klas's answers.

**(i) Amendment (22), `security-auditor`'s text** (insert exactly one of the LinkedIn variants at ‹LinkedIn›):
```
**`security-auditor`, 2026-10-03: the account and the permission at the provider, and where a Cancel lands.**
Nothing below re-grades a finding, and none of it is a §9.6 (3) acceptance
(`docs/reviews/2026-10-03-1926-form-security-auditor.md`).

**`prompt` governs our own requests, and only them.**
- Google's authorization request carries `prompt=select_account consent` and GitHub's `prompt=select_account`, each
  a constant in its adapter. Google documents `prompt` as "A space-delimited, case-sensitive list of prompts to
  present the user", of which `none` "Must not be specified with other values", and its absence as prompting "only
  the first time your project requests access" ("Using OAuth 2.0 for Web Server Applications", read 2026-10-03).
  GitHub documents `select_account` as forcing "the account picker to appear" ("Authorizing OAuth apps", read
  2026-10-03; the page carries no date).
- Neither value is personal data. Neither changes the scope, an endpoint or what the callback reads. `access_type`
  stays absent, so Google's documented default, `online`, holds and no refresh token is issued.
- `prompt` is a parameter of a request, not a setting of the client. A request that someone else builds for our
  client carries whatever parameters its builder chose, or none. So `prompt` closes no residual and moves no bound:
  - LinkedIn: (21)'s sentence stands as written. A member who has granted the app before is sent on without a
    consent screen, so a crafted request mints a code for a signed-in member without prompting.
  - Google: the same shape. For a request without `prompt`, an account that has consented to our client is not
    prompted again, as Google documents it. Declared, not measured.
  - GitHub: a crafted request still needs a click on GitHub's authorization page. The revocation gives that, by
    leaving no authorization for GitHub to skip; `prompt` does not. A failed revocation (EventId 1028) leaves one,
    so 1028 stays in every reading.
  - What keeps such a code out of other hands is unchanged: the provider's redirect matching, the callback's
    `no-referrer` and zero subresources, and no log line that carries a code.
- What it buys is for the requests our start builds. At Google and GitHub, a person at a shared computer sees which
  account is about to be used before any session exists, instead of being signed silently into the account whose
  provider session is still open in that browser (Art. 25(1), 32(1)(b)). A measure, not a closed finding.
- A provider's permission page is the provider's authorization of our client, not consent to our processing. The
  login's legal basis stays Art. 6(1)(b), as the privacy policy states it.

‹LinkedIn›

**GitHub sends a refusal to its app's first redirect URI, not to the request's.**
- GitHub documents a refused authorization as redirecting "to the registered callback URL" with `error`,
  `error_description`, `error_uri` and `state` ("Troubleshooting authorization request errors", read 2026-10-03),
  and a request without `redirect_uri` as going to "the first callback URL configured in the OAuth app settings"
  ("Authorizing OAuth apps"). RFC 6749 §4.1.2.1 and OIDC Core §3.1.2.6 return a refusal to the request's own
  redirect URI, and Google and LinkedIn document that form.
- The app the box shares with localhost (m-4) lists three redirect URIs, each ending in
  `/api/auth/oauth/github/callback`: `http://localhost:3000`, `https://dev.jobbliggaren.se` and
  `https://jobbliggaren.se`, in that order. "Allow wildcard matching" is unchecked on all three, and "Enable Device
  Flow" is unchecked (Klas's screenshot of the app page, 2026-10-03, kept local). #1732 holds no earlier reading of
  the wildcard setting. The one measured Cancel, on dev on 2026-09-29, landed on the first of them.
- That order is also the alphabetical one, so the page does not show whether GitHub's "first" is the order of
  entry. A change of order is measured by a Cancel, never by the page.
- The first redirect URI is where every refusal lands, from every host, and where GitHub sends a code for a request
  that names no redirect. It must therefore be a host this box serves. The apex is not one today: it is not the box,
  and it completed no TLS handshake on 2026-10-02T23:04:44Z.
- A refusal carries no code. On the box the callback answers any `error` with the notice, reaches no backend, and no
  log line records it (measured 2026-10-02: the web container writes no request log, and the edge writes only 5xx).
- ‹Klas's answer, verbatim, and what was measured after it: the page's order, and where a Cancel on dev landed›.
- GitHub's page says wildcard matching "allows tokens to be sent to all subdomains and additional paths of the
  redirect URI", and its documentation that "Apps that had a single callback URL enabled prior to August 3, 2026
  have wildcard matching enabled for that callback URL". §3d point 4 keeps it off on every redirect URI, read on
  the day.
- At the flip: #734 row 12. The mechanism is Klas's, under m-4.

**The Google client the box uses is the developer machine's: m-8 (2026-09-25, Minor) does not hold.**
- Measured 2026-10-02 by the session, compared by sha256 prefix and never printed: the box's Google client id and
  secret equal those under `Auth:OAuth:Google` in the main copy's `appsettings.Local.json`. Probed from the box, the
  client accepts the localhost and the apex callbacks besides the box's, and refuses an unregistered control.
- Amendment (16)'s "The Google client is a separate one for the box" rested on Klas's answer of 2026-09-26 (#1732,
  comment 5849039714), not on a hash reading. Whether it held that day is not known.
- ‹(a)› Klas, ‹date›: "‹words›". The box shares the Google client with the developer machine, as it shares
  GitHub's and LinkedIn's (m-4); that covers real users' logins after the flip unless it is changed then. If the
  local secret is rotated, the box's secret file is replaced too. m-8 stands as graded, and the decision is Klas's.
- ‹(b)› Klas, ‹date›: "‹words›". The box got a client of its own on ‹date›, whose only redirect URI is the box's
  callback, and the developer machine keeps the shared one for localhost. Read after the change: ‹the box's client
  id hash-unequal to the developer machine's; a probe with the localhost callback refused; whether the next Google
  login found the existing link›.

**The apex redirect.** All three clients the box uses register the apex's callback (Google and LinkedIn by probe on
2026-10-02, GitHub from the app page on 2026-10-03), and the box does not serve the apex. At Google and LinkedIn a
crafted request can send a code there with no click, at GitHub after one. It is inert while the apex completes no
TLS handshake (2026-10-02T23:04:44Z; IPv6 unmeasured). `security-auditor`'s Minor 1 (2026-09-29, LinkedIn) extends
by name to Google and GitHub: remove the apex redirect from each client until the apex is the box. Klas's portal
settings.

**Lapse triggers, read for this PR: none fires.**
- 4: no provider joins or leaves, and neither the `VerifiedEmail` rule, the address invariant nor the outcome table
  changes. `prompt` changes which provider pages a flow passes, not who can obtain a session.
- F10's re-measure triggers: the callback's response form, the three cookies' SameSite and prefixes and the
  continuation hop are unchanged, and no commit has touched the callback route, the continuation document or the
  cookie and flow helpers since PR P (`cb9f00bb`). The Safari reading stays owed; a successful login in Safari after
  this PR deploys takes it.
- 1–3 and 5–7: untouched. Registration is closed and both accounts are the controller's (read
  2026-10-02T22:54–22:58Z).
- Chapter V: no scope, endpoint or stored token changes, so Google's (4), GitHub's (4)–(5) and LinkedIn's (3)–(4)
  do not fire. (21)'s binding trigger has not fired either.

**The findings stand as graded.** M-1 (GitHub), M-1 (LinkedIn), Major 2 (GitHub), Major 2 (LinkedIn) and Major 4
stand unsigned, and no acceptance exists. This PR touches none of them: no line they cite moves, no provider or path
joins, and no outcome changes. It therefore rides `agents-done` on `security-auditor`'s verdict against the final
diff, not #1888's §12 STOPP shape.

**(21)'s reading at the first login after the rollout is discharged** by #1732's comment of 2026-10-02, transcribed
above. The api ran `sha-5307e37`, not the merge's tag, and the reading is still one of the merged code: `git diff
9171326f 5307e37` over the three adapters, the Application port and the web start and callback routes is empty, as
it is from there to `0d1b94ae3` (`security-auditor`, 2026-10-03). The start carried exactly the five keys, and the
login measured `email_verified` as the JSON `true`, and `sub` and `email` under `openid email`. The Apple-WebKit
residual stays open, since no browser was stated.

**DoD 8.** No new personal data. `prompt` carries none, and the scopes, the endpoints, what is received and what is
stored are unchanged. Google may echo `prompt` and `authuser` on the callback; both are judged "kept" in the
edge-log inventory, and the callback reads neither. No new log line, recipient, source or transfer, and Chapter V
is unchanged. The privacy and cookie policies describe no provider page, so neither changes. The register does not
change‹, except that its LinkedIn line records the deactivation›.

*(End of `security-auditor`'s text.)*
```
‹LinkedIn›, variant (a), and also variant (b) when the probe shows no page (then add "(Klas's answer (b), ‹date›; the probe of ‹date› showed no page for any value)"):
```
**LinkedIn has no documented lever, and its behaviour is recorded as LinkedIn's limitation** (Klas, ‹date›:
"‹words›").
- LinkedIn's authorization request takes five parameters and no `prompt`, and a member with an existing grant is
  sent on without a screen ("3-Legged OAuth Flow", updated 2026-05-15, read 2026-10-03). Measured 2026-10-02: a
  repeat login on the box asked for neither the account nor permission (#1926, comment 5962296021).
- Declared, not measured: at a shared computer, a person who chooses LinkedIn is signed into the account of whoever
  is still signed in to LinkedIn in that browser, with no page in between, and sees that account's CVs and
  applications. Choosing another LinkedIn account means signing out of LinkedIn first. It reaches someone other than
  the controller at trigger 1 or 2.
```
‹LinkedIn›, variant (b) when a value is honoured:
```
**LinkedIn: `prompt=‹value›`, which LinkedIn does not document, measured honoured** (Klas, ‹date›: "‹words›").
- Probed ‹date› in Klas's browser, signed in to LinkedIn with a live grant, with authorization URLs built by hand:
  our client, the box's callback, `scope=openid email` and a random state no flow held. Without `prompt` LinkedIn sent
  him on with no page (the control); with `prompt=‹value›` it showed ‹the page›. No probe reached the api (0
  `Method=LinkedIn` and 0 × EventId 1022 in the window). RFC 6749 §3.1 has an unrecognised parameter ignored, so the
  probe could only show a page or not.
- It is kept while it is measured to work: a repeat login on the box that shows no page deletes it in the next PR
  (nothing dormant, Amendment (18)).
```
‹LinkedIn›, variant (c):
```
**LinkedIn login is deactivated on the box** (Klas, ‹date›: "‹words›") by §3d's deactivation, until LinkedIn
documents a way to ask again. Its `linkedin` rows stay; they match only this app's subject ids.
```

**(ii) `vps-deploy-stack.md` §3d.**

Google point 3 is replaced. Use variant (a) or (b) to match Klas's answer.

Variant (a):
```
3. **The Google client** (security-auditor m-8). The box shares the Google client used on localhost, by
   Klas's acceptance on ‹date›: "‹his words›" (ADR 0142 Amendment (22), where the hash reading is). If the
   local secret is rotated, the box's secret file is replaced too. Its authorized redirect URIs include
   exactly `https://${SITE_HOST}/api/auth/oauth/google/callback`. Probe every registered one on the day, with
   an unregistered control, and record by host whether this box serves it.
```
Variant (b):
```
3. **The Google client** (security-auditor m-8). The box uses a client of its own, whose only authorized
   redirect URI is `https://${SITE_HOST}/api/auth/oauth/google/callback`; localhost uses another, so this
   client's secret never sits on a developer machine. Read on the day: the box's client id is not hash-equal
   to a developer's, and a probe with the localhost callback is refused.
```
Google's reading list gains this bullet:
```
- the web start: a 302 to `https://accounts.google.com/o/oauth2/v2/auth` with exactly `client_id`,
  `code_challenge`, `code_challenge_method`, `prompt`, `redirect_uri`, `response_type`, `scope` and `state`:
  `prompt=select_account consent`, `scope=openid email`, S256, the box's callback and no `access_type`; and the
  flow cookie's attributes;
```
Google's "Expected on the first login" gains this paragraph:
```
**Every login asks.** The request carries `prompt=select_account consent` (#1926), so every Google login shows
Google's account chooser and then its permission page. A Cancel on the permission page returns to `/logga-in` on
the host the flow started on, with "Inloggningen med Google slutfördes inte".
```
GitHub point 4 is replaced as a whole:
```
4. **The GitHub OAuth App** (security-auditor m-4). The box shares the OAuth App used on localhost, by
   Klas's acceptance on 2026-09-27: "varför kan jag inte använda samma nyckel som i appsettings ? Jag
   accepterar risken" (ADR 0142 Amendment (19), where the hash reading is). If the local secret is
   rotated, the box's secret file is replaced too. Read on the day:
   - **Where a refusal lands.** GitHub sends every refusal, a Cancel included, to the redirect URI it treats as
     the app's first, whatever the request named, and sends a request that names no redirect there too (ADR
     0142 Amendment (22)). That one must be `https://${SITE_HOST}/api/auth/oauth/github/callback`. Read the
     app page's order, and take one Cancel on GitHub's authorization page from this box: it lands on this box's
     `/logga-in`. The page's order alone does not show which URI GitHub treats as first.
   - **"Allow wildcard matching" is unchecked on every redirect URI.** It would let a code be sent to any
     subdomain and any further path, and GitHub enables it for an app that had a single callback URL before
     2026-08-03.
   - **"Enable Device Flow" is unchecked.** Nothing here uses it.
   - Whether the app issues expiring user tokens: GitHub does not document whether revoking a token also
     revokes its refresh token.
```
In GitHub's reading list, two bullets are replaced:
```
- the web start: a 302 to `https://github.com/login/oauth/authorize` with exactly `client_id`,
  `code_challenge`, `code_challenge_method`, `prompt`, `redirect_uri`, `scope` and `state`:
  `prompt=select_account`, `scope=user:email`, S256, no `offline_access`, the box's callback; and the flow
  cookie's attributes;
- the Chapter V readings above, and the OAuth App's settings in point 4.
```
GitHub's "Expected on the first login" gains this paragraph:
```
**Every login asks.** The request carries `prompt=select_account` (#1926), and the token is revoked after each
login, so every GitHub login shows the account picker and then the authorization page. A Cancel on the
authorization page lands where point 4 says.
```
LinkedIn, under variant (a), gains this paragraph under "Expected on the first login":
```
**A repeat login asks nothing.** A member with a grant is sent on without any LinkedIn page (documented, and
measured on the box on 2026-10-02), and LinkedIn documents no parameter that asks again (ADR 0142 Amendment
(22), Klas's decision). Someone at a shared computer who wants another LinkedIn account signs out of LinkedIn
first.
```
LinkedIn, under variant (b) with a value honoured: the web-start bullet becomes
```
- the web start: a 302 to `https://www.linkedin.com/oauth/v2/authorization` with exactly
  `response_type=code`, `client_id`, the box's callback as `redirect_uri`, `scope=openid email`, `state` and
  `prompt=‹value›`, and neither `nonce` nor `code_challenge`; and the flow cookie's attributes;
```
and this paragraph is added:
```
**A repeat login shows ‹the page›,** because the request carries `prompt=‹value›`, which LinkedIn does not
document and which was measured honoured on ‹date› (ADR 0142 Amendment (22)). If a repeat login shows no page,
the parameter is deleted in the next PR.
```
LinkedIn, under variant (c), gains this line under "Deactivation":
```
Deactivated ‹date›, by Klas's decision (ADR 0142 Amendment (22)): LinkedIn documents no way to ask again for a
member who has granted the app.
```

**(iii) In-place markers in ADR 0142.** Each anchor occurs exactly once (grep, 2026-10-03). Append each marker after its anchor, separated by one space.
1. Line 1045, anchor `the activation runbook recommends it.`:
   `*(corrected in Amendment ‹date› (22): measured 2026-10-02 not to hold, since the box's Google client is the developer machine's; ‹Klas decided to share it | the box got a client of its own on ‹date››)*`
2. Line 1220, anchor `The Google client is a separate one for the box.`:
   `*(corrected in Amendment ‹date› (22): this was Klas's answer of 2026-09-26, not a hash reading. Measured 2026-10-02, the box's Google client id and secret equal the developer machine's, and the client accepts the localhost callback; whether the sentence held that day is not known)*`
3. Line 1309, anchor `and any wildcard or subdirectory matching is off.`:
   `*(corrected in Amendment ‹date› (22): the app lists three redirect URIs, localhost first, then dev and the apex, with wildcard matching off on all three (Klas's screenshot of the app page, 2026-10-03); GitHub sends every refusal to the one it treats as first)*`

**(iv) #734 condition table, row 12:**
```
| 12 | **The production host's provider settings, read before the flip** — GitHub sends every refusal, a Cancel included, to the redirect URI it treats as its app's first, whatever the request named, and sends a request that names none there too (ADR 0142 Amendment (22)). On the production host, before the flip: a Cancel at each provider lands on that host's `/logga-in` with the provider's notice, measured there; the GitHub app it uses has that host's callback as the URI GitHub treats as first (shown by that Cancel), or the app is its own; and "Allow wildcard matching" is unchecked on every redirect URI of that app, read from its page that day | **Open — Klas's.** The mechanism (the first place in the shared app, or an app of its own) is his, under m-4 (ADR 0142 Amendments (17)–(20)). Bound by Amendment (22) (`security-auditor`, #1926 form round). Klas reads this row before the flip; nothing detects it automatically |
```

### Findings

**Blocker: none. Major: none.**

#### Minor

1. **m-8 (carried from 2026-09-25) does not hold: the box's Google client is the developer machine's.**
   - **Files:** outside the repo, the Google client's settings and the box's `deploy/.env` and secrets mount. In the repo, `c:\tmp\jbl-1926\docs\runbooks\vps-deploy-stack.md:588-590` and `c:\tmp\jbl-1926\docs\decisions\0142-passwordless-auth-one-page-code-or-link-oauth-ready.md:1044-1045, :1220`.
   - **Current:** the box's Google client id and secret equal the developer machine's (the session's sha256 prefixes, 2026-10-02). The client accepts the localhost and apex callbacks. §3d point 3 and two ADR sentences state the opposite.
   - **Required:** Klas decides (question 3). The record and §3d follow his answer in this PR. Under his (b), a new client for the box, read as §3d sets out.
   - **Why:** Art. 32(1)(b). The box's secret on a developer machine widens who can redeem a code. It is inert for third parties while every account is the controller's.
   - **Why only Minor:** the grade of 2026-09-25 stands, and nothing here changes it. This change did not create the state.
   - **Delegate to:** Klas (the console, and the box with his GO); the session for the texts.
   - **Disposal:** docs in-block; the console step is Klas's.

2. **Minor 1 (carried from 2026-09-29), extended by name to Google and GitHub: the apex redirect on all three clients.**
   - **Files:** the three clients' settings, outside the repo.
   - **Current:**
     - The apex callback is registered at Google and LinkedIn (probe, 2026-10-02) and at GitHub (the app page, 2026-10-03).
     - The apex is not the box (A record 217.160.0.188), and it completed no TLS handshake (2026-10-02T23:04:44Z). IPv6 is unmeasured.
   - **Required:** remove the apex redirect from each client until the apex is the box. It returns at the flip, first at GitHub (#734 row 12).
   - **Why:** Art. 25(1) and 32(1)(b); RFC 9700 §4.5. At Google and LinkedIn a crafted request mints a code to that host with no click, and `prompt` does not change that. At GitHub it takes one click.
   - **Why only Minor:** inert while no TLS handshake completes. Redeeming such a code also needs the client secret.
   - **Optional measurement:** TLS to the apex over IPv6, from a host that has IPv6.
   - **Delegate to:** Klas, in the three portals; no code.
   - **Disposal:** a named skip in the PR body (portal settings no lane touches), plus the note under "Till din kännedom" below.

3. **New, 2026-10-03: the tracked description of GitHub's app is measured false.**
   - **Files:** `c:\tmp\jbl-1926\docs\decisions\0142-passwordless-auth-one-page-code-or-link-oauth-ready.md:1305-1310`, which is (17)'s "The box's OAuth App"; `c:\tmp\jbl-1926\docs\runbooks\vps-deploy-stack.md:658-661`, which is §3d point 4.
   - **Current:** the texts say "its callback URL is exactly the box's …" and "Its authorization callback URL is exactly `https://${SITE_HOST}/…`, and nothing else". Klas's screenshot of 2026-10-03 shows three redirect URIs, with localhost first. The wildcard sentence holds.
   - **Required:** marker (iii) 3 on (17), and §3d point 4 replaced by the text in (ii).
   - **Why:**
     - AGENTS.md §5: a factually wrong doc is a defect, and it is fixed.
     - "Exactly … and nothing else" hides both the first-redirect rule and the localhost entry.
   - **Delegate to:** the session, in-block. This PR already plans the correction.

### Praise
- The session probed all three providers anonymously and without credentials, and named the limit of the GitHub probe instead of guessing past it. ✓
- `prompt` as a code constant, pinned by exact key sets, keeps a security-relevant request shape out of configuration. ✓
- Wildcard matching and Device Flow were read off on every redirect URI from the app page, not inherited. ✓

### Summary
- 0 Blocker, 0 Major.
- Three Minors: m-8 carried and measured not to hold; Minor 1 carried and extended by name; one new (GitHub's app description). This change causes none of them.
- The form for Google and GitHub is signed under four conditions.
- LinkedIn's outcome, GitHub's order and the Google client are Klas's decisions; three questions follow.
- The PR rides `agents-done`. My verdict on the final diff is owed.
- Re-review after a fix: the same agent, report-only, scoped to the fix delta (CLAUDE.md §9.6).

Verdict-table line: "security-auditor, #1926 form round: ✓ form signed (four conditions); 0 Blocker / 0 Major; Minors: m-8 measured not to hold (Klas question 3), Minor 1 extended to Google and GitHub (named skip), GitHub app description false (in-block); rides `agents-done`; three Klas questions."

**Eskalering till Klas: ja. Tre frågor och en sak till din kännedom. Ingen av dem stoppar koden, men svaren avgör vad som skrivs i ADR:en och driftinstruktionen. De behövs därför innan dokumenten skrivs.**

**Fråga 1 (LinkedIn).**
Text före frågan:
> Google och GitHub kan tvingas att fråga om konto och tillåtelse vid varje inloggning, med en inställning som de själva dokumenterar, och den läggs till nu. LinkedIn dokumenterar ingen sådan. Enligt LinkedIns dokumentation hoppas godkännandesidan över för den som redan har godkänt appen, och det mättes på servern 2026-10-02: din andra inloggning med LinkedIn gick rakt igenom utan att LinkedIn frågade något. Det går att prova odokumenterade inställningar innan något byggs. Sessionen ger dig då fyra länkar som du öppnar i webbläsaren där du är inloggad på LinkedIn, och du ser om LinkedIn visar en sida. Ingen inloggning sker, eftersom länkarna inte startas från vår knapp och vi därför avvisar svaret.

Frågan: Hur ska LinkedIn-inloggningen fungera för den som har loggat in förut?
- **(a) Som i dag:** "LinkedIn frågar bara första gången. Sitter två personer vid samma dator loggas den som klickar på LinkedIn in direkt i kontot för den som fortfarande är inloggad på LinkedIn i den webbläsaren, utan någon sida emellan, och ser det kontots cv och ansökningar. Den som vill använda ett annat LinkedIn-konto loggar först ut från LinkedIn. Det skrivs in som en begränsning hos LinkedIn."
- **(b) Prova först (rekommenderas):** "Du öppnar sessionens fyra länkar, en utan inställning och en för varje kandidat. Visar LinkedIn en sida för någon av dem byggs just den, och den tas bort igen om LinkedIn senare slutar visa sidan. Visar LinkedIn ingen sida gäller (a), utan att du behöver svara igen."
- **(c) Stäng av LinkedIn:** "Nycklarna tas bort på servern tills LinkedIn dokumenterar ett sätt att fråga igen. Inloggning med LinkedIn fungerar inte under tiden."

**Fråga 2 (GitHub).**
Text före frågan:
> GitHub skickar den som trycker Avbryt till den adress som GitHub räknar som först i appens lista, oavsett var inloggningen startade. Det följer av GitHubs dokumentation och stämmer med din skärmdump: localhost står överst, och Avbryt på dev.jobbliggaren.se hamnade 2026-09-29 på localhost. Dit skickar GitHub också en inloggningskod när en begäran inte anger någon adress, så den första adressen ska alltid vara en server vi själva kör. Jokertecken är avstängda på alla tre adresserna, som de ska vara.
>
> Du frågade om du ska lägga jobbliggaren.se överst, eftersom det är den som gäller vid lanseringen. Inte än: jobbliggaren.se pekar i dag inte på servern och svarar inte på https, så Avbryt på dev skulle hamna på en sida som inte går att nå. Vid lanseringen ska den stå först eller få en egen app, och det skrivs in som en rad i lanseringslistan (#734).
>
> Listan på sidan står också i bokstavsordning, så sidan visar inte säkert vilken adress GitHub räknar som först. Det avgörs av att du trycker Avbryt efter ändringen.

Frågan: Hur ska GitHub-appen se ut fram till lanseringen?
- **(a) dev.jobbliggaren.se först (rekommenderas):** "Ta bort localhost-raden och lägg till den igen, och tryck sedan Avbryt i en GitHub-inloggning på dev. Landar du på dev är det klart, och Avbryt när du kör lokalt landar då också på dev, vilket är ofarligt. Landar du fortfarande på localhost sorterar GitHub listan själv, och då återstår (b) eller (c)."
- **(b) En egen app per miljö:** "Servern och din dator får var sin GitHub-app, med bara sin egen adress, och Avbryt landar alltid rätt. Det ändrar ditt beslut från 2026-09-27 att servern delar nyckel med din dator."
- **(c) Låt det vara:** "Avbryt på dev landar på localhost fram till lanseringen, och #1926 blir inte klar för GitHub."
- **(d) jobbliggaren.se först nu:** "Avbryt på dev hamnar på en adress som inte svarar förrän jobbliggaren.se pekar på servern, och fram till dess skickar GitHub en kod utan adress till en server vi inte kör."

**Fråga 3 (Google-nyckeln).**
Text före frågan:
> När Google-inloggningen slogs på 2026-09-26 skrevs att servern har en egen Google-klient med bara dev- och produktionsadresserna. Det byggde på ditt svar den dagen, inte på en mätning. Driftinstruktionen kräver en separat klient för localhost, så att serverns hemlighet aldrig ligger på en utvecklardator. Det är mitt fynd m-8 från 2026-09-25, ett mindre fynd. Mätt 2026-10-02: serverns Google-klient är i dag samma som den i din appsettings.Local.json, både id och hemlighet, och den godtar inloggningar som går tillbaka till localhost. Om den någonsin var en egen vet vi inte. För GitHub och LinkedIn har du valt att servern delar nyckel med din dator (2026-09-27, "Jag accepterar risken"). För Google finns inget sådant beslut nedskrivet, och dokumenten säger fortfarande motsatsen.

Frågan: Ska servern dela Google-nyckel med din dator?
- **(a) Ja, som GitHub och LinkedIn:** "Jag accepterar risken. Kommer någon åt din appsettings.Local.json får hen också serverns Google-hemlighet, och byter du den lokala hemligheten byts serverns också. Det gäller också riktiga användares inloggningar efter lanseringen, om du inte ändrar det då. Dokumenten skrivs om med dina ord."
- **(b) Nej, servern får en egen klient:** "Du skapar en ny Google-klient med bara serverns adress och lägger in den på servern. Den du har lokalt blir kvar för localhost. Kopplingen till ditt Google-konto väntas fungera vidare, och nästa inloggning visar om den gör det."

**Till din kännedom (inget beslut behövs för mergen).**
> jobbliggaren.se står som callback-adress i alla tre apparna, Google, GitHub och LinkedIn, men adressen pekar i dag inte på servern. Hos Google och LinkedIn kan någon som bygger en egen inloggningslänk få en inloggningskod skickad dit utan att den inloggade klickar något, och hos GitHub efter ett klick. I dag kommer koden ingenstans, eftersom adressen inte svarar på https, så långt vi har mätt. Jag rekommenderar att du tar bort jobbliggaren.se i alla tre apparna tills lanseringen och lägger tillbaka den då, först hos GitHub eller i en egen app.

Files:
- `c:\tmp\jbl-1926\docs\sessions\2026-10-03-1926-form-brief.md`
- `c:\tmp\jbl-1926\src\Jobbliggaren.Infrastructure\Auth\ExternalLogins\GoogleIdentityProvider.cs`
- `c:\tmp\jbl-1926\src\Jobbliggaren.Infrastructure\Auth\ExternalLogins\GitHubIdentityProvider.cs`
- `c:\tmp\jbl-1926\src\Jobbliggaren.Infrastructure\Auth\ExternalLogins\LinkedInIdentityProvider.cs`
- `c:\tmp\jbl-1926\web\jobbliggaren-web\src\app\api\auth\oauth\[provider]\start\route.ts`
- `c:\tmp\jbl-1926\web\jobbliggaren-web\src\app\api\auth\oauth\[provider]\callback\route.ts`
- `c:\tmp\jbl-1926\web\jobbliggaren-web\src\lib\auth\oauth-callback-edge-log-verdicts.ts`
- `c:\tmp\jbl-1926\tests\Jobbliggaren.Application.UnitTests\Auth\ExternalLogins\GoogleIdentityProviderTests.cs`
- `c:\tmp\jbl-1926\tests\Jobbliggaren.Application.UnitTests\Auth\ExternalLogins\GitHubIdentityProviderTests.cs`
- `c:\tmp\jbl-1926\tests\Jobbliggaren.Application.UnitTests\Auth\ExternalLogins\LinkedInIdentityProviderTests.cs`
- `c:\tmp\jbl-1926\docs\decisions\0142-passwordless-auth-one-page-code-or-link-oauth-ready.md`
- `c:\tmp\jbl-1926\docs\runbooks\vps-deploy-stack.md`
- `c:\tmp\jbl-1926\docs\runbooks\gdpr-processing-register.md`
- `c:\tmp\jbl-1926\web\jobbliggaren-web\messages\sv\content-legal.json`
