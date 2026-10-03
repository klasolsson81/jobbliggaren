# #1926 form round — security-auditor follow-up batch after Klas's answers (report-only)

- **Date:** 2026-10-03 (report returned ~2026-10-03T03:5xZ)
- **Agent:** `security-auditor`, report-only, the same agent as `2026-10-03-1926-form-security-auditor.md`, resumed
  with the session's batch (Klas's three answers, the LinkedIn probe, GitHub's reorder and Cancel, #734's free row)
- **Transcription:** the agent's last SubagentHandback message, byte-extracted from its transcript; nothing below the
  rule is the session's.

---

## Security audit: #1926, follow-up batch after Klas's answers (report-only)

**Status:** ✓ My verdict is unchanged: the form is signed, and there are 0 Blocker and 0 Major.
- The texts below replace my signed variants, fitted to Klas's three answers.
- The session fills only the named slots, each with one of the readings listed under its block. Any other reading comes back to me.
- Two readings are owed before C4.

**Authority:**
- GDPR Art. 5(1)(f), 25(1) and 32(1)(b).
- RFC 6749 §3.1 and §4.1.2.1; RFC 9700 §4.4.2 and §4.5.
- CLAUDE.md §9.6; AGENTS.md §1.6 and §5.
- ADR 0142 Amendments (14)–(21).

**What I read and measured, 2026-10-03.**
- The coordinator's batch, and `senior-cto-advisor`'s routing (`c:\tmp\jbl-1926\docs\reviews\2026-10-03-1926-form-cto.md`, decisions 1–6).
- My transcribed report. Its md5 is `ad643b33e390f5415af307c5741c5455`, which matches the one given.
- `grep -c -F` on that report: each option text Klas chose, and option (a), which his (b) brings into force, occurs exactly once.
- RFC 9700 §4.4.2, which has two subsections: 4.4.2.1 is the defence by issuer identification, and 4.4.2.2 the defence by distinct redirect URIs.

I did not read the box, and I wrote, committed, labelled and posted nothing.

Klas's words are wrapped inside the blocks below. Measure them as substrings with whitespace normalised.

### (A) GitHub under Klas's (d)

**(A)(ii) Amendment (22): my GitHub section is replaced as a whole.** That is everything from its bold heading through "At the flip: #734 row 12 …".
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
  `error_description`, `error_uri`, `iss` and `state`, and no other value recorded. GitHub's first is therefore the
  first by order of entry. Read from the app page after the change, ‹date›: "Allow wildcard matching" unchecked on
  all three.
- **What (d) leaves until the apex is the box.** The apex is not the box. It completed no TLS handshake over IPv4
  on 2026-10-02T23:04:44Z, ‹IPv6 sentence›
  - A GitHub Cancel on dev lands on a host that does not answer, as measured above, and by the same rule so does
    one on localhost. #1926's Cancel criterion is therefore read for GitHub on the production host (#734 row 12),
    not on dev.
  - Declared, not measured: every GitHub refusal carries its flow's `state` to that host, and the flow cookie stays
    set in that browser for up to ten minutes, since our callback never ran; and a request that names no redirect
    sends a code there, after a click.
  - Both are inert while no TLS handshake completes there: a browser sends no request line, and so no query, over a
    handshake that fails.
  - **Lapse:** the apex completes a TLS handshake while it is not this box. The apex then stops being the app's first
    redirect URI that day (Klas, in the app's settings), and `security-auditor` reads Minor 1 (2026-09-29, extended
    below) again. **Home:** this amendment. **Reader:** Klas Olsson. Nothing detects it automatically.
- GitHub's page says wildcard matching "allows tokens to be sent to all subdomains and additional paths of the
  redirect URI", and its documentation that "Apps that had a single callback URL enabled prior to August 3, 2026
  have wildcard matching enabled for that callback URL". §3d point 4 keeps it off on every redirect URI, read on
  the day, and reads a redirect URI added again as a new one.
- At the flip: #734 row 12 reads it on the production host.
```
**‹IPv6 sentence›** takes exactly one of these:
- `and none over IPv6 on ‹date›.`
- `and accepted no IPv6 connection on ‹date›.`
- `and had no AAAA record on ‹date›.`

**The IPv6 instrument.** Run it from a host with working IPv6. On the box it is read-only, if the box has IPv6.
```
dig +short AAAA jobbliggaren.se
curl -6 -sS -o /dev/null --max-time 10 https://www.google.com/; echo "control=$?"
curl -6 -sS -o /dev/null --max-time 10 https://jobbliggaren.se/; echo "apex=$?"
```
- No AAAA record → the third sentence.
- `control=0` and `apex=35` → the first sentence.
- `control=0` and `apex` 7 or 28 → the second sentence.
- `control` not 0 → that host cannot read IPv6; take the reading from another host.
- Any other `apex`, for example 0, or 60 (a handshake that reached the certificate) → the text comes back to me before C4.

**The wildcard slot** takes only "unchecked on all three". Any other reading comes back to me before C4.

**(22): my "The apex redirect." paragraph is replaced:**
```
**The apex redirect.** All three clients the box uses register the apex's callback (Google and LinkedIn by probe on
2026-10-02, GitHub from the app page on 2026-10-03), and the box does not serve the apex. At Google and LinkedIn a
crafted request can send a code there with no click, at GitHub after one, and under Klas's (d) above every GitHub
refusal goes there too. It is inert while the apex completes no TLS handshake (above). `security-auditor`'s Minor 1
(2026-09-29, LinkedIn) extends by name to Google and GitHub and stands as graded: remove the apex redirect from each
client until the apex is the box. For GitHub, Klas's (d) keeps the apex as the first redirect URI instead. For
Google and LinkedIn the recommendation was put to him on 2026-10-03 as information, with no decision asked. Klas's
portal settings.
```

**(A)(i) §3d GitHub point 4, replaced as a whole:**
```
4. **The GitHub OAuth App** (security-auditor m-4). The box shares the OAuth App used on localhost, by
   Klas's acceptance on 2026-09-27: "varför kan jag inte använda samma nyckel som i appsettings ? Jag
   accepterar risken" (ADR 0142 Amendment (19), where the hash reading is). If the local secret is
   rotated, the box's secret file is replaced too. Read on the day:
   - **Where a refusal lands.** GitHub sends every refusal, a Cancel included, to the app's first redirect URI
     by order of entry, whatever the request named, and sends a request that names no redirect there too (ADR
     0142 Amendment (22)). By Klas's decision of 2026-10-03 the first is
     `https://jobbliggaren.se/api/auth/oauth/github/callback`, the host real users will register on. Until the
     apex is this box, a Cancel on this box lands on a host that does not answer, so read that the apex
     completes no TLS handshake over IPv4 or IPv6; if it does, the apex stops being the first that day
     (Amendment (22)). Take one Cancel on GitHub's authorization page from this box and record the host it lands
     on: the order of entry is read from a Cancel, not from the page's list.
   - **"Allow wildcard matching" is unchecked on every redirect URI.** It would let a code be sent to any
     subdomain and any further path, and GitHub enables it for an app that had a single callback URL before
     2026-08-03. A redirect URI removed and added again is read again.
   - **"Enable Device Flow" is unchecked.** Nothing here uses it.
   - Whether the app issues expiring user tokens: GitHub does not document whether revoking a token also
     revokes its refresh token.
```

**(A)(iii) GitHub's "Expected on the first login" needs no change.** Its last sentence points to point 4, which now carries the landing under (d), so the landing has one home.

**(A)(iv) Three more texts that (d) moves.**

Marker (iii) 3. The signed form encodes the screenshot's order, which (d) has since moved. This form replaces it:
`*(corrected in Amendment ‹date› (22): the app lists three redirect URIs, localhost's, dev's and the apex's, not the box's alone, and GitHub sends every refusal to the first by order of entry; (22) holds the order and the wildcard readings)*`

#734 row 12, replaced. Row 12 is free (read 2026-10-03).
```
| 12 | **The production host's provider settings, read before the flip** — GitHub sends every refusal, a Cancel included, to its app's first redirect URI by order of entry, whatever the request named, and sends a request that names none there too (ADR 0142 Amendment (22)). On the production host, before the flip, measured there: a Cancel on Google's permission page, on GitHub's authorization page and on LinkedIn's sign-in page (in a browser signed out of LinkedIn, since a member with a grant sees no LinkedIn page) each lands on that host's `/logga-in` with the provider's notice, and so the GitHub Cancel shows that host's callback is the app's first; and "Allow wildcard matching" is unchecked on every redirect URI of the GitHub app, read from its page that day | **Open — Klas's.** On 2026-10-03 he put the apex first in the app the box shares with localhost (his (d), Amendment (22)), which meets the first-URI half once the apex is the production host; it is read then. The mechanism stays his, under m-4 (Amendments (17)–(20)). Bound by Amendment (22) (`security-auditor`, #1926 form round). Klas reads this row before the flip; nothing detects it automatically |
```

My Q3 steps 4 and 6, replaced. The CTO's closing rule rests on step 6.
```
4. A Cancel on Google's permission page and on GitHub's authorization page, each in a login started on dev, and on
   LinkedIn's sign-in page in a browser signed out of LinkedIn, since a member with a grant sees no LinkedIn page.
   Record the host and path each lands on, and the notice. Query keys may be recorded, never the values of `state`,
   `code` or `error_description`.
6. **Done when:** Google and GitHub show their pages at every login; a Cancel at Google and at LinkedIn lands on
   dev's `/logga-in` with its notice; and a Cancel at GitHub lands on the apex, as Klas's (d) decided. GitHub's
   Cancel landing on the host its flow started on is read on the production host under #734 row 12, not on dev.
   Any other landing keeps #1926 open.
```

### (B) LinkedIn: not my variant (a) with its addition. This block fills ‹LinkedIn› in (22) instead.
The probe's counts belong in (22).
- They are the measurement that decided between the two branches of Klas's (b), and a decision's measurement goes in the ADR that records the decision.
- The #1926 comment is the dated record that the amendment transcribes.
- The CTO is right that 1021 is the count that shows no probe reached the api, so the text names it.
```
**LinkedIn has no documented lever and honoured no undocumented one, so its behaviour is recorded as LinkedIn's
limitation.**
- Klas's answer, put through AskUserQuestion on 2026-10-03 ("Hur ska LinkedIn-inloggningen fungera för den som har
  loggat in förut?"): **"(b) Prova först (rekommenderas)"**, whose text was "Du öppnar sessionens fyra länkar, en
  utan inställning och en för varje kandidat. Visar LinkedIn en sida för någon av dem byggs just den, och den tas
  bort igen om LinkedIn senare slutar visa sidan. Visar LinkedIn ingen sida gäller (a), utan att du behöver svara
  igen." Its fallback, option **"(a) Som i dag"**, whose text was "LinkedIn frågar bara första gången. Sitter två
  personer vid samma dator loggas den som klickar på LinkedIn in direkt i kontot för den som fortfarande är inloggad
  på LinkedIn i den webbläsaren, utan någon sida emellan, och ser det kontots cv och ansökningar. Den som vill
  använda ett annat LinkedIn-konto loggar först ut från LinkedIn. Det skrivs in som en begränsning hos LinkedIn.",
  therefore applies.
- LinkedIn's authorization request takes five parameters and no `prompt`, and a member with an existing grant is
  sent on without a screen ("3-Legged OAuth Flow", updated 2026-05-15, read 2026-10-03). Measured 2026-10-02: a
  repeat login on the box asked for neither the account nor permission (#1926, comment 5962296021).
- **The probe, 2026-10-03** (#1926, comment ‹id›). Klas, in Brave, signed in to LinkedIn with the app already
  granted, opened four authorization URLs the session built by hand: our client, the box's callback,
  `response_type=code`, `scope=openid email` and a fresh random 43-character state that no flow held. The control,
  without `prompt`, came first, then `prompt=login`, `prompt=consent` and `prompt=select_account`. None of the four
  showed a LinkedIn page, and each landed on dev's `/logga-in` with "Inloggningen med LinkedIn slutfördes inte". The
  control is valid, and no value was honoured. RFC 6749 §3.1 has an unrecognised parameter ignored, so the probe
  could only show a page or not.
- **Read back on the box** at 2026-10-03T03:33:15Z, over the api's log from its recreate at 2026-10-02T23:48:42Z
  onto `sha-0d1b94a`. The lines from 23:38:44Z to the recreate are not readable. Klas opened the links after asking
  for them again at ‹hh:mm›Z, after the recreate, and the web keeps no request log, so the sitting's own time is not
  on the server. 0 × EventId 1021, so no probe reached the api; 0 × 1022 and 0 × 1023; no `User.*` or `Account.*`
  event; `identity."AspNetUserLogins"` unchanged, `github:1,google:1,linkedin:1`. The one `login_succeeded …
  Method=LinkedIn` in the window, at 03:30:41Z, was Klas's own login with the button, by his confirmation on
  2026-10-03; a probe's state names no flow, so it cannot produce one.
- Declared, not measured: at a shared computer, a person who chooses LinkedIn is signed into the account of whoever
  is still signed in to LinkedIn in that browser, with no page in between, and sees that account's CVs and
  applications. Choosing another LinkedIn account means signing out of LinkedIn first. It reaches someone other than
  the controller at trigger 1 or 2.
- The question returns if LinkedIn documents a parameter that asks again. **Home:** this amendment. **Reader:** Klas
  Olsson. Nothing detects it automatically.
```
- **‹id›** is the #1926 comment the CTO's decision 2 has posted.
- **‹hh:mm›** is the time of Klas's renewed request, from the session's transcript. If that time is not after 23:48:42Z, the text comes back to me.

**The §3d LinkedIn paragraph under "Expected on the first login"** replaces my variant (a):
```
**A repeat login asks nothing.** A member with a grant is sent on without any LinkedIn page (documented, and
measured on the box on 2026-10-02). LinkedIn documents no parameter that asks again, and honoured none of
`prompt=login`, `prompt=consent` and `prompt=select_account` when probed on 2026-10-03 (ADR 0142 Amendment (22),
Klas's decision). Someone at a shared computer who wants another LinkedIn account signs out of LinkedIn first.
```
dotnet-architect's two Cancel facts (CTO decision 6.7) sit beside it unchanged, and neither conflicts with it.

### (C) Google: the label plus its description, as the text of the option he chose
- Klas chose that text; he did not type it. So it is recorded as "whose text was", the form Amendments (18) and (19) use, and never as words he wrote.
- The description carries the consequence he was shown, which is what makes the record informed.

**In (22), my bullets ‹(a)› and ‹(b)› are replaced by this one:**
```
- Klas's answer, put through AskUserQuestion on 2026-10-03 ("Ska servern dela Google-nyckel med din dator?"):
  **"(a) Ja, som GitHub och LinkedIn"**, whose text was "Jag accepterar risken. Kommer någon åt din
  appsettings.Local.json får hen också serverns Google-hemlighet, och byter du den lokala hemligheten byts serverns
  också. Det gäller också riktiga användares inloggningar efter lanseringen, om du inte ändrar det då. Dokumenten
  skrivs om med dina ord." The box shares the Google client with the developer machine, as it shares GitHub's and
  LinkedIn's (m-4); that covers real users' logins after the flip unless it is changed then. If the local secret is
  rotated, the box's secret file is replaced too. m-8 stands as graded, and the decision is Klas's.
```

**The §3d Google point 3:**
```
3. **The Google client** (security-auditor m-8). The box shares the Google client used on localhost, by
   Klas's answer of 2026-10-03, "(a) Ja, som GitHub och LinkedIn" (ADR 0142 Amendment (22), where the hash
   reading and the option's whole text are). If the local secret is rotated, the box's secret file is
   replaced too. Its authorized redirect URIs include exactly
   `https://${SITE_HOST}/api/auth/oauth/google/callback`. Probe every registered one on the day, with an
   unregistered control, and record by host whether this box serves it.
```

**Marker (iii) 1:**
`*(corrected in Amendment ‹date› (22): measured 2026-10-02 not to hold, since the box's Google client is the developer machine's; Klas decided on 2026-10-03 that the box shares it)*`

**(22)'s DoD 8, last sentence:** "The register does not change."

**‹date›** in the three markers is the amendment's own date.

### (D) What the answers and readings change
- **The verdict is unchanged.**
  - No answer changes a line of C1.
  - LinkedIn's request keeps its five keys, so there is no C2 constant.
  - The PR still rides `agents-done` (Q4).
  - My verdict on the final diff is still owed.
- **Minor 1 (m-8): disposed of by Klas's (a), with its grade unchanged.** The docs land in-block, as (C) gives them. Verdict table: "m-8: Klas's decision (a) 2026-10-03, recorded in (22); grade unchanged".
- **Minor 2 (the apex redirect): still Minor, but its premise now carries more.**
  - Under (d), every GitHub refusal from dev and from localhost reaches the apex, not only a crafted request.
  - The grade still rests on the apex being inert, so the IPv6 half of that reading is now owed before C4, no longer optional.
  - (22) gains a lapse with its home and its reader.
  - For GitHub the remedy is declined by Klas's (d), and that decision is recorded.
  - The CTO's named-skip line stays true once ‹IPv6 sentence› is filled from the allowed set.
- **Minor 3 stays in-block,** with marker 3 in the form under (d) given above.
- **`iss` needs no text.**
  - The edge-log verdict is keyed on the query key, not on the provider. It judges `iss` "kept": a published issuer URL, not personal data.
  - The file's comment cites Google's documentation for `iss`, and does not say GitHub never sends it. So it is not false (AGENTS.md §5).
  - (22)'s list of query keys records GitHub's `iss`.
  - The callback reads `iss` neither now nor under this form. Mix-up defence rests on a distinct redirect URI per provider and a provider-bound state taken once (RFC 9700 §4.4.2.2). An issuer check under §4.4.2.1 would add nothing we lack, so not validating `iss` is no finding.
- **The probe changes nothing.** Any code it minted was refused at the web callback, never exchanged, and expires in 30 minutes, as documented.

### Findings
None new. The Minors stand as set out in (D).

### Praise
- The probe ran with a control in one sitting, and its read-back named 1021, the count that can show "no probe reached the api". ✓
- The reorder was measured by a Cancel instead of being read off the page, and that settled GitHub's order-of-entry rule. ✓

### Summary
- The verdict is unchanged: 0 Blocker, 0 Major.
- Replacement texts are given for (d), for LinkedIn's (b) falling back to (a), and for Google's (a).
- Owed before C4: the session's IPv6 reading of the apex, and Klas's re-read of the wildcard setting after the re-add.
- Re-review after a fix: the same agent, report-only, scoped to the fix delta (CLAUDE.md §9.6).

Verdict-table line: "security-auditor, #1926 follow-up batch: verdict unchanged (form signed; 0 Blocker / 0 Major); replacement texts for (d), LinkedIn (b)→(a) and Google (a); owed before C4: the IPv6 apex reading and the wildcard re-read after the re-add; Minor 1 (m-8) disposed of by Klas's (a); Minor 2 stands, with a lapse added under (d); Minor 3 in-block, marker 3 in its (d) form; `iss` needs no text."

**Eskalering till Klas: nej. Inget nytt beslut.**

En avläsning som bara Klas kan ta före C4. Sessionen ställer den ordagrant:
> Du tog bort localhost- och dev-adresserna i GitHub-appen och lade till dem igen. De två raderna är alltså nya, så inställningen för jokertecken läses om innan dokumenten skrivs.
>
> Frågan: Är rutan "Allow wildcard matching" tom, utan bock, på alla tre adresserna i GitHub-appen?
> - **(a) Ja, på alla tre:** "Ingen av de tre adresserna har en bock i rutan."
> - **(b) Nej, på någon:** "Minst en adress har en bock i rutan. Ta bort bocken och svara igen."

Files:
- `c:\tmp\jbl-1926\docs\reviews\2026-10-03-1926-form-security-auditor.md`
- `c:\tmp\jbl-1926\docs\reviews\2026-10-03-1926-form-cto.md`
- `c:\tmp\jbl-1926\docs\decisions\0142-passwordless-auth-one-page-code-or-link-oauth-ready.md`
- `c:\tmp\jbl-1926\docs\runbooks\vps-deploy-stack.md`
- `c:\tmp\jbl-1926\web\jobbliggaren-web\src\lib\auth\oauth-callback-edge-log-verdicts.ts`
- `c:\tmp\jbl-1926\web\jobbliggaren-web\src\app\api\auth\oauth\[provider]\callback\route.ts`
