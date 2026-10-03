# #1926 form round — security-auditor follow-up batch after Klas's answers, second hand-back (report-only)

- **Date:** 2026-10-03 (report returned ~2026-10-03T03:5xZ)
- **Agent:** `security-auditor`, report-only, the same agent as `2026-10-03-1926-form-security-auditor.md`, resumed
  with the session's batch (Klas's three answers, the LinkedIn probe, GitHub's reorder and Cancel, #734's free row)
- **Transcription:** the agent's last SubagentHandback message (its second batch hand-back, which supersedes the first in `2026-10-03-1926-form-security-auditor-batch.md`), byte-extracted from its transcript; nothing below the
  rule is the session's.

---

## Security audit: #1926, the batch after Klas's three answers (report-only)

**Status:** ✓ Approved. My verdict is unchanged.
- The form stays signed, with 0 Blocker and 0 Major.
- Klas's three answers change texts, not grades.
- Two readings are owed before C4: the apex over IPv6, and the wildcard setting on the two re-added redirect URIs.

**Authority:**
- GDPR Art. 5(1)(f), 25(1) and 32(1)(b).
- RFC 6749 §3.1 and §4.1.2.1; RFC 9700 §4.4.2.2, read 2026-10-03: *"clients MUST use a distinct redirection URI for each issuer they interact with"*.
- CLAUDE.md §9.6; AGENTS.md §5 (`Comments:`).
- ADR 0142 Amendments (17)–(21); the CTO's routing (`2026-10-03-1926-form-cto.md`, decisions 1–6).

**What I read and measured, 2026-10-03.**
- The CTO's routing, decisions 1–6.
- The transcribed report: md5 `ad643b33e390f5415af307c5741c5455`, which equals yours.
- Each of the four option texts I quote below occurs exactly once in that report (`grep -c -F`).
- I did not read the adapter files, since the mutation harness is running. I wrote, committed, labelled and posted nothing, and I did not read the box.

### (A) GitHub under Klas's (d)

**(i) `vps-deploy-stack.md` §3d, GitHub point 4.** Replace it as a whole:
```
4. **The GitHub OAuth App** (security-auditor m-4). The box shares the OAuth App used on localhost, by
   Klas's acceptance on 2026-09-27: "varför kan jag inte använda samma nyckel som i appsettings ? Jag
   accepterar risken" (ADR 0142 Amendment (19), where the hash reading is). If the local secret is
   rotated, the box's secret file is replaced too. Read on the day:
   - **Where a refusal lands.** GitHub sends every refusal, a Cancel included, to the app's first redirect URI
     by order of entry, whatever the request named, and sends a request that names no redirect there too (ADR
     0142 Amendment (22)). By Klas's decision of 2026-10-03 the first is
     `https://jobbliggaren.se/api/auth/oauth/github/callback`, the host real users will register on (#734).
     Until the apex is this box, a Cancel on this box lands on a host that does not answer, so read that the
     apex completes no TLS handshake over IPv4 or IPv6; if it does, the apex stops being the first that day
     (Amendment (22)). Take one Cancel on GitHub's authorization page from this box and record the host it
     lands on: the order of entry is read from a Cancel, not from the page's list.
   - **"Allow wildcard matching" is unchecked on every redirect URI.** It would let a code be sent to any
     subdomain and any further path, and GitHub enables it for an app that had a single callback URL before
     2026-08-03. A redirect URI removed and added again is read again.
   - **"Enable Device Flow" is unchecked.** Nothing here uses it.
   - Whether the app issues expiring user tokens: GitHub does not document whether revoking a token also
     revokes its refresh token.
```

**(ii) Amendment (22).**

**1. The GitHub section.** It replaces my whole section, from `**GitHub sends a refusal` through `- At the flip: #734 row 12. The mechanism is Klas's, under m-4.`:
```
**GitHub sends a refusal to its app's first redirect URI by order of entry, not to the request's.**
- GitHub documents a refused authorization as redirecting "to the registered callback URL" with `error`,
  `error_description`, `error_uri` and `state` ("Troubleshooting authorization request errors", read 2026-10-03),
  and a request without `redirect_uri` as going to "the first callback URL configured in the OAuth app settings"
  ("Authorizing OAuth apps"). RFC 6749 §4.1.2.1 and OIDC Core §3.1.2.6 return a refusal to the request's own
  redirect URI, and Google and LinkedIn document that form.
- The app the box shares with localhost (m-4) listed three redirect URIs, each ending in
  `/api/auth/oauth/github/callback`: `http://localhost:3000`, `https://dev.jobbliggaren.se` and
  `https://jobbliggaren.se`, in that order. "Allow wildcard matching" was unchecked on all three, and "Enable Device
  Flow" was unchecked (Klas's screenshot of the app page, 2026-10-03, kept local). #1732 holds no earlier reading of
  the wildcard setting. The one measured Cancel, on dev on 2026-09-29, landed on the first of them. That order was
  also the alphabetical one, so the page could not show whether GitHub's first follows the order of entry; a Cancel
  measures it.
- The first redirect URI is where every refusal lands, from every host, and where GitHub sends a code for a request
  that names no redirect. `security-auditor` asked for a host this box serves.
- Klas's answer, put through AskUserQuestion on 2026-10-03 ("Hur ska GitHub-appen se ut fram till lanseringen?"):
  **"(d) jobbliggaren.se först nu"**, whose text was "Avbryt på dev hamnar på en adress som inte svarar förrän
  jobbliggaren.se pekar på servern, och fram till dess skickar GitHub en kod utan adress till en server vi inte
  kör." He removed the localhost and dev redirect URIs and added them again, so the apex has been on the app
  longest. A Cancel on GitHub's authorization page, in a login started on dev on 2026-10-03, then landed on
  `https://jobbliggaren.se/api/auth/oauth/github/callback` with the query keys `error` (`access_denied`),
  `error_description`, `error_uri`, `iss` and `state`, no other value recorded. GitHub's first is therefore the
  first by order of entry. Read from the app page after the change, ‹date›: "Allow wildcard matching" unchecked on
  all three.
- **What (d) leaves until the apex is the box,** declared, not measured. The apex is not the box. It completed no
  TLS handshake over IPv4 on 2026-10-02T23:04:44Z, ‹IPv6 sentence›
  - A GitHub Cancel on dev or on localhost lands on a host that does not answer. #1926's Cancel criterion is
    therefore read for GitHub on the production host (#734 row 12), not on dev.
  - Every GitHub refusal carries its flow's `state` to that host, and the flow cookie stays set in that browser for
    up to ten minutes, since our callback never ran. A request that names no redirect sends a code there, after a
    click.
  - Both are inert while no TLS handshake completes there: a browser sends no request line, and so no query, over a
    handshake that fails.
  - **Lapse:** the apex completes a TLS handshake while it is not this box. The apex then stops being the app's
    first redirect URI that day (Klas, in the app's settings), and `security-auditor` reads Minor 1 (2026-09-29,
    extended below) again. **Home:** this amendment. **Reader:** Klas Olsson. Nothing detects it automatically.
- GitHub's page says wildcard matching "allows tokens to be sent to all subdomains and additional paths of the
  redirect URI", and its documentation that "Apps that had a single callback URL enabled prior to August 3, 2026
  have wildcard matching enabled for that callback URL". §3d point 4 keeps it off on every redirect URI, read on
  the day, and reads a redirect URI added again as a new one.
- At the flip: #734 row 12 reads it on the production host.
```
⚠ **The ‹date› slot in the bullet on Klas's answer accepts only "unchecked on all three".** If Klas finds a box checked, he unchecks it and the page is read again. If he wants one to stay checked, the text comes back to me.

**‹IPv6 sentence›** is exactly one of three sentences:
- "and none over IPv6 on ‹date›."
- "and accepted no IPv6 connection on ‹date›."
- "and had no AAAA record on ‹date›."

**The IPv6 instrument**, read-only, from a host with IPv6 (the box, if it has IPv6):
```
dig +short AAAA jobbliggaren.se
curl -6 -sS -o /dev/null --max-time 10 https://www.google.com/; echo "control=$?"
curl -6 -sS -o /dev/null --max-time 10 https://jobbliggaren.se/; echo "apex=$?"
```
**How the instrument is read:**
- No AAAA record: the third sentence.
- `control=0` and `apex=35`: the first sentence.
- `control=0` and `apex=7` or `28`: the second sentence.
- `control` is not 0: that host has no working IPv6, so take the reading from another host.
- `apex` is anything else (0, 60, or any other value): a handshake got further than over IPv4, and the text comes back to me before C4.

**2. The apex paragraph.** It replaces mine as a whole:
```
**The apex redirect.** All three clients the box uses register the apex's callback (Google and LinkedIn by probe on
2026-10-02, GitHub from the app page on 2026-10-03), and the box does not serve the apex. At Google and LinkedIn a
crafted request can send a code there with no click, at GitHub after one, and under Klas's (d) above every GitHub
refusal goes there too. It is inert while the apex completes no TLS handshake (above). `security-auditor`'s Minor 1
(2026-09-29, LinkedIn) extends by name to Google and GitHub and stands as graded: remove the apex redirect from each
client until the apex is the box. For GitHub, Klas's (d) keeps the apex as the first redirect URI instead. For Google
and LinkedIn the recommendation was put to him on 2026-10-03 as information, with no decision asked. Klas's portal
settings.
```

**3. Marker (iii) 3 in its (d) form.** The earlier marker encoded an order that has since moved, so it is replaced:
`*(corrected in Amendment ‹date› (22): the app lists three redirect URIs, localhost's, dev's and the apex's, not the box's alone, and GitHub sends every refusal to the first by order of entry; (22) holds the order and the wildcard readings)*`

**4. #734 row 12 in its (d) form.** It replaces my (iv). Row 12 is free (read 2026-10-03).
```
| 12 | **The production host's provider settings, read before the flip** — GitHub sends every refusal, a Cancel included, to its app's first redirect URI by order of entry, whatever the request named, and sends a request that names none there too (ADR 0142 Amendment (22)). On the production host, before the flip, measured there: a Cancel on Google's permission page, on GitHub's authorization page and on LinkedIn's sign-in page (in a browser signed out of LinkedIn, since a member with a grant sees no LinkedIn page) each lands on that host's `/logga-in` with the provider's notice, and so the GitHub Cancel shows that host's callback is the app's first; and "Allow wildcard matching" is unchecked on every redirect URI of the GitHub app, read from its page that day | **Open — Klas's.** On 2026-10-03 he put the apex first in the app the box shares with localhost (his (d), Amendment (22)), which meets the first-URI half once the apex is the production host; it is read then. The mechanism stays his, under m-4 (Amendments (17)–(20)). Bound by Amendment (22) (`security-auditor`, #1926 form round). Klas reads this row before the flip; nothing detects it automatically |
```

**5. My Q3, steps 4 and 6, in their (d) form.** The CTO's closing rule (decision 1) points at step 6.
```
4. A Cancel on Google's permission page and on GitHub's authorization page, each in a login started on dev, and on
   LinkedIn's sign-in page in a browser signed out of LinkedIn (a member with a grant sees no LinkedIn page). Record
   the host and path each lands on, and the notice. Query keys may be recorded, never the values of `state`, `code`
   or `error_description`.
6. **Done when:** Google and GitHub show their pages at every login; a Cancel at Google and at LinkedIn lands on
   dev's `/logga-in` with its notice; and a Cancel at GitHub lands on the apex, as Klas's (d) decided, until the apex
   is the box. GitHub's landing on the host its flow started on is read on the production host under #734 row 12,
   not on dev. Any other landing keeps #1926 open.
```

**(iii) GitHub's "Expected on the first login": no change.**
- "A Cancel on the authorization page lands where point 4 says" keeps one home for the landing.
- Point 4 now states that the Cancel lands on the apex.
- A second statement of the landing would be a second home that can drift.

### (B) LinkedIn

**Use this block for ‹LinkedIn›, in place of variant (a) and its addition.** The probe's counts belong in (22): the decision and its measurement share one home. #1926's dated comment is the source, and (22) transcribes it, as earlier amendments transcribe #1732's comments. Under the CTO's decision 2, 1021 is the count that shows no probe reached the api.
```
**LinkedIn has no documented lever and honoured no undocumented one, so its behaviour is recorded as LinkedIn's
limitation.**
- Klas's answer, put through AskUserQuestion on 2026-10-03 ("Hur ska LinkedIn-inloggningen fungera för den som har
  loggat in förut?"): **"(b) Prova först (rekommenderas)"**, whose text was "Du öppnar sessionens fyra länkar, en
  utan inställning och en för varje kandidat. Visar LinkedIn en sida för någon av dem byggs just den, och den tas
  bort igen om LinkedIn senare slutar visa sidan. Visar LinkedIn ingen sida gäller (a), utan att du behöver svara
  igen." Option (a), which therefore applies, was "(a) Som i dag", whose text was "LinkedIn frågar bara första
  gången. Sitter två personer vid samma dator loggas den som klickar på LinkedIn in direkt i kontot för den som
  fortfarande är inloggad på LinkedIn i den webbläsaren, utan någon sida emellan, och ser det kontots cv och
  ansökningar. Den som vill använda ett annat LinkedIn-konto loggar först ut från LinkedIn. Det skrivs in som en
  begränsning hos LinkedIn."
- LinkedIn's authorization request takes five parameters and no `prompt`, and a member with an existing grant is
  sent on without a screen ("3-Legged OAuth Flow", updated 2026-05-15, read 2026-10-03). Measured 2026-10-02: a
  repeat login on the box asked for neither the account nor permission (#1926, comment 5962296021).
- **The probe, 2026-10-03** (#1926, comment ‹id›). Klas, in Brave and signed in to LinkedIn, where the app was
  granted on 2026-10-02, opened four authorization URLs the session built by hand: our client, the box's callback,
  `response_type=code`, `scope=openid email` and a fresh random 43-character state that no flow held. The control,
  without `prompt`, came first, then `prompt=login`, `prompt=consent` and `prompt=select_account`. None of the four
  showed a LinkedIn page, and each landed on dev's `/logga-in` with "Inloggningen med LinkedIn slutfördes inte". So
  the control is valid and no value was honoured. RFC 6749 §3.1 has an unrecognised parameter ignored, so the probe
  could only show a page or not.
- **Read back on the box** at 2026-10-03T03:33:15Z, over the api's log from its recreate at 2026-10-02T23:48:42Z
  (onto `sha-0d1b94a`), which the earlier container's lines do not reach. The sitting followed his renewed request
  for the links at ‹hh:mm›Z; the web keeps no request log, so its own time is not on the server.
  - 0 × EventId 1021: no probe reached the api, where a state that names no flow can only be answered with 1021.
  - 0 × 1022 and 0 × 1023; no `User.*` or `Account.*` event; `AspNetUserLogins` unchanged at
    `github:1,google:1,linkedin:1`.
  - One `login_succeeded … Method=LinkedIn`, at 03:30:41Z, was Klas's own login with the button (his
    confirmation, 2026-10-03). A probe cannot produce one, since its state names no flow.
- Declared, not measured: at a shared computer, a person who chooses LinkedIn is signed into the account of whoever
  is still signed in to LinkedIn in that browser, with no page in between, and sees that account's CVs and
  applications. Choosing another LinkedIn account means signing out of LinkedIn first. It reaches someone other than
  the controller at trigger 1 or 2.
- The question returns if LinkedIn documents a parameter that asks again. **Home:** this amendment. **Reader:** Klas
  Olsson. Nothing detects it automatically.
```
⚠ **‹hh:mm› must come from the session's transcript and be later than 23:48:42Z.** If it cannot be shown to be later, the read window may not cover the sitting, and the block comes back to me.

**§3d LinkedIn.** This replaces my variant (a) paragraph:
```
**A repeat login asks nothing.** A member with a grant is sent on without any LinkedIn page (documented, and
measured on the box on 2026-10-02). LinkedIn documents no parameter that asks again, and honoured none of
`prompt=login`, `prompt=consent` and `prompt=select_account` when probed on 2026-10-03 (ADR 0142 Amendment (22),
Klas's decision). Someone at a shared computer who wants another LinkedIn account signs out of LinkedIn first.
```
The two Cancel facts the CTO assigned to the session (decision 6.7) fit beside this paragraph, provided they say "the request's `redirect_uri`".

### (C) Google

**What fills ‹words›: the label together with its description.**
- The description is written as the text of the option he chose, in the house form "whose text was" (Amendments (18) and (19)), with the question beside it.
- It is never written as words he typed. "Jag accepterar risken." is my option text, which he chose.
- The label alone would drop the consequence he was shown.

**(22).** The bullet below replaces both my ‹(a)› and ‹(b)› bullets:
```
- Klas's answer, put through AskUserQuestion on 2026-10-03 ("Ska servern dela Google-nyckel med din dator?"):
  **"(a) Ja, som GitHub och LinkedIn"**, whose text was "Jag accepterar risken. Kommer någon åt din
  appsettings.Local.json får hen också serverns Google-hemlighet, och byter du den lokala hemligheten byts serverns
  också. Det gäller också riktiga användares inloggningar efter lanseringen, om du inte ändrar det då. Dokumenten
  skrivs om med dina ord." The box shares the Google client with the developer machine, as it shares GitHub's and
  LinkedIn's (m-4); that covers real users' logins after the flip unless it is changed then. If the local secret is
  rotated, the box's secret file is replaced too. m-8 stands as graded, and the decision is Klas's.
```
**§3d Google point 3:**
```
3. **The Google client** (security-auditor m-8). The box shares the Google client used on localhost, by
   Klas's answer of 2026-10-03, "(a) Ja, som GitHub och LinkedIn" (ADR 0142 Amendment (22), where the hash
   reading and the option's whole text are). If the local secret is rotated, the box's secret file is
   replaced too. Its authorized redirect URIs include exactly
   `https://${SITE_HOST}/api/auth/oauth/google/callback`. Probe every registered one on the day, with an
   unregistered control, and record by host whether this box serves it.
```
**Marker (iii) 1:** `*(corrected in Amendment ‹date› (22): measured 2026-10-02 not to hold, since the box's Google client is the developer machine's; Klas decided on 2026-10-03 that the box shares it)*`

**(22) DoD 8, last sentence:** "The register does not change." My slot on the deactivation is dropped, since (c) was not chosen.

### (D) Verdict, the Minors, and `iss`

- **Verdict:** unchanged.
  - No answer touches code. LinkedIn keeps its five keys.
  - Q4 stands: the PR rides `agents-done`.
- **m-8:** decided by Klas, under (a). Its grade stands, and the docs land in-block through the texts in (C).
- **Minor 1, extended to Google and GitHub (the apex): it stands as graded.**
  - Under (d) more reaches the apex: every GitHub refusal from dev or localhost, carrying its flow's `state`, besides a code sent without a redirect.
  - Klas's option text named the code half. The `state` half is declared in my (22) text.
  - The grade rests on TLS failing. That is why the IPv6 reading is owed before C4, and why the lapse is added.
  - The CTO's named-skip line stays true, once the IPv6 reading fills one of my three sentences.
- **Minor 3:** in-block, with the (d) marker and the (d) point 4.
- **`iss` needs no text.**
  - The edge-log verdict is keyed on the query key, and already keeps `iss` as one published URL.
  - The file's comment cites only Google for `iss`, but does not say GitHub never sends it, so it is not false (AGENTS.md §5).
  - Our callback reads neither the key nor the value. Our mix-up defence is the distinct redirect URI per provider, which RFC 9700 §4.4.2.2 names.
  - (22)'s key list records what GitHub sent.
- **Two readings are owed before C4:**
  - the apex over IPv6, with the instrument above;
  - the wildcard setting on the two re-added redirect URIs. Only Klas can take this one, and the wording is below.

### Praise
- The probe ran with a control and three counts, so LinkedIn's (b) was settled by measurement. GitHub's "first" was settled by a Cancel, not by the page. ✓

### Summary
- 0 Blocker and 0 Major. The verdict and every grade are unchanged.
- The texts for (d), for LinkedIn's (b)→(a) and for Google's (a) are given verbatim above.
- Two readings are owed before C4.
- Re-review after a fix: the same agent, report-only, scoped to the fix delta (CLAUDE.md §9.6).

Verdict-table line: "security-auditor, #1926 batch after Klas's answers (report-only): verdict unchanged (form signed; 0 Blocker / 0 Major); texts for GitHub (d), LinkedIn (b)→(a) and Google (a); owed before C4: the apex over IPv6, and the wildcard re-read after the re-add; Minor 1 (apex) stands, with what reaches it widened by (d) and a lapse added; `iss` needs no text."

**Eskalering till Klas: nej. Inget nytt beslut.**

En avläsning som bara Klas kan ta. Sessionen ställer frågan ordagrant:
> Du tog bort och lade till localhost- och dev-adresserna i GitHub-appen, så två av raderna är nya, och deras inställning läses om innan dokumenten skrivs. Öppna appens sida: är "Allow wildcard matching" urkryssad på alla tre adresserna?

Files:
- `c:\tmp\jbl-1926\docs\reviews\2026-10-03-1926-form-security-auditor.md`
- `c:\tmp\jbl-1926\docs\reviews\2026-10-03-1926-form-cto.md`
- `c:\tmp\jbl-1926\docs\decisions\0142-passwordless-auth-one-page-code-or-link-oauth-ready.md`
- `c:\tmp\jbl-1926\docs\runbooks\vps-deploy-stack.md`
- `c:\tmp\jbl-1926\web\jobbliggaren-web\src\lib\auth\oauth-callback-edge-log-verdicts.ts`
