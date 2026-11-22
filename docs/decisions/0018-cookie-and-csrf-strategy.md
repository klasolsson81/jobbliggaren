# ADR 0018: Cookie and CSRF Strategy for Frontend Auth

- **Status:** Accepted
- **Date:** 2026-05-06
- **Deciders:** Klas Olsson
- **Related:** ADR 0017 (frontend-auth-pattern)

## Context

ADR 0017 establishes that JobbPilot uses HTTP-only cookies for session
transport and that frontend and backend communicate via a same-origin
Next.js proxy. This ADR specifies the concrete cookie attributes and
the CSRF-mitigation strategy.

## Decision

### Cookie attributes (session cookie)

| Attribute    | Value                                |
|--------------|--------------------------------------|
| Name         | `__Host-jobbpilot_session`           |
| Value        | Opaque session-id (32 bytes, base64url) |
| HttpOnly     | true                                 |
| Secure       | true (production); true on localhost via dev cert |
| SameSite     | Strict                               |
| Path         | /                                    |
| Domain       | (omitted; `__Host-` prefix forbids it) |
| Max-Age      | 14 days (sliding, refreshed on use)  |

`__Host-` prefix enforces that the cookie is set with `Secure`, `Path=/`, and
no `Domain` attribute — strongest browser-enforced cookie integrity guarantee.

### Architecture

- `dev.jobbpilot.se/*` → Next.js (App Router)
- `dev.jobbpilot.se/api/*` → Next.js Route Handler → forwards to .NET backend
  (internal host, not browser-reachable)
- Cookie is set by the Next.js Route Handler on `dev.jobbpilot.se` after the
  backend confirms credentials. Backend never sets cookies on responses that
  reach the browser.
- Next.js translates incoming cookie to `Authorization: Bearer <session-id>`
  before forwarding to backend (per ADR 0017).

### Backend trust model

The .NET backend is **not browser-reachable** — it operates as an internal
service behind the Next.js same-origin proxy. This topology is load-bearing
for the CSRF strategy:

- The browser never issues a request directly to the backend origin.
- All cookie management (set, read, delete) is handled exclusively by Next.js
  Route Handlers. The backend has no cookie logic whatsoever.
- The backend receives `Authorization: Bearer <session-id>` synthesized
  server-side by the Next.js proxy. A browser-based CSRF attack cannot forge
  this header because the browser cannot read the `HttpOnly` cookie value.
- The backend validates the bearer token against the Redis session store. It
  does **not** validate `Origin` or `Referer` headers — that responsibility
  belongs to the Next.js layer (`assertSameOrigin` helper, Server Actions
  built-in Origin check).

**Invariant:** any relaxation of this topology (e.g., allowing browser-direct
requests to the backend API) invalidates this CSRF analysis and requires
explicit CSRF token machinery on the backend before that change ships.

### CSRF mitigation

Three layers, in order:

1. **`SameSite=Strict`** — primary defense. Browsers will not send the
   session cookie on any cross-site request, including top-level navigation
   (for a GET to a protected or admin page, narrowed by Amendment 2026-10-10).
2. **Origin/Host header validation** — Server Actions validate this
   automatically. Route Handlers performing state-changing operations
   (POST/PUT/PATCH/DELETE) must call `assertSameOrigin(request)` helper.
3. **No state changes on GET** — invariant enforced by code review and
   future linting rule.

Double-submit cookie / synchronizer-token pattern is **not** added at this
stage. SameSite=Strict + same-origin proxy + Server Actions Origin-check is
considered sufficient by current Next.js authentication guidance, and the
deployment topology eliminates the cross-origin attack surface that
double-submit primarily mitigates.

### Logout

- Client triggers Server Action `logout()`.
- Server Action: deletes Redis session record, instructs response to
  `set-cookie: __Host-jobbpilot_session=; Max-Age=0; Path=/; Secure; HttpOnly; SameSite=Strict`.
- Redirect to `(auth)/login`.

## Considered Alternatives

### Cross-origin cookie (api.jobbpilot.se issuing Domain=.jobbpilot.se)
**Rejected** because requires `SameSite=Lax` (Strict breaks redirects),
`AllowCredentials` CORS config, explicit origin allowlist, and additional
CSRF token. Higher complexity for marginal benefit.

### SameSite=Lax with double-submit CSRF token
**Rejected** as primary defense. Acceptable as fallback if Strict-mode
proves to break legitimate flows in practice; revisit then.

### Stateless JWT in cookie (no session-id)
See ADR 0017.

## Consequences

### Positive
- Strongest browser-side cookie protection available (`__Host-` + Strict).
- No CSRF token machinery to maintain.
- Single cookie, single domain, single trust boundary.

### Negative
- First page-load after login from external link cannot show authenticated
  state in SSR (cookie not sent on cross-origin top-level navigation in Strict
  mode). Mitigation: render skeleton, hydrate, client-side fetch of
  `/api/me`. Documented behavior, not bug.
- All state-changing endpoints must use POST/Server Actions (this is already
  best practice).

## Open Questions (TODO Fas 1)

- Token rotation policy on session-id (currently: never; consider rotating
  on privilege change or after N days).
- CSP `frame-ancestors` posture (relevant for embed/preview scenarios).

## References

- MDN: Set-Cookie `__Host-` prefix
- Next.js Server Actions security model
- ADR 0017

---

## Amendment 2026-07-05 (#481 2b-3b) — persistent-login activation: branch-dependent cookie lifetime + cookie-name correction

**Datum:** 2026-07-05
**Källa/Trigger:** #481 epic, PR2b-3b (persistent-login frontend activation) — corrects two claims
in the original 2026-05-06 decision above that this session's work made stale.

*Affects the "Cookie attributes (session cookie)" table and the "Logout" section above. The
original decision — topology, CSRF strategy, `__Host-` attribute discipline, the three-layer CSRF
mitigation — is unchanged and remains Accepted.*

### Correction 1 — cookie name

The table above lists `Name: __Host-jobbpilot_session` — a pre-rename artifact of the product's
original name (see ADR 0069, JobbPilot → Jobbliggaren). The cookie has always actually shipped, and
is read/written throughout the codebase, as **`__Host-jobbliggaren_session`**
(`web/jobbliggaren-web/src/lib/auth/session.ts`, `SESSION_COOKIE_NAME`; also the presence check in
`proxy.ts`). This is a documentation correction, not a behaviour change.

### Correction 2 — `Max-Age` is now branch-dependent, not a flat 14 days

The original "Max-Age: 14 days (sliding, refreshed on use)" row described the single,
always-persistent session profile that existed until 2026-07-05 — every login received the same
silent 14-day cookie, with no user choice. Per **ADR 0142** (which carries the persistent-login legal-basis analysis; until 2026-09-17 this
pointer read "ADR 0093", a number that holds a different ADR — the analysis was never written,
#1494), the cookie now branches on an explicit "Håll mig inloggad" checkbox at login/register:

| Choice | Cookie `Max-Age` | Server-side lifetime |
|---|---|---|
| Unticked (default) | none — true session cookie, dies on browser close | 24h sliding / 24h absolute cap |
| Ticked | 180 days (finite ceiling) | 30d sliding / 180d absolute cap, anchored to `SessionPayload.CreatedAt`; session-id rotated every 24h |

The server remains the source of truth for actual expiry in both branches — the cookie's `Max-Age`
never grants more reach than the server will still honour. Rotation (Persistent branch only)
re-sets `__Host-jobbliggaren_session` via a Next.js proxy refresh seam (`proxy.ts`, nodejs runtime
per the Next 16 `middleware`→`proxy` codemod), throttled by a companion `HttpOnly`
`__Host-jobbliggaren_refresh_after` cookie (a non-secret timestamp only, read server-side by the
proxy); the `HttpOnly`/`Secure`/`SameSite=Strict`/`Path=/`/no-`Domain` attribute discipline from
the original decision is unchanged on every rotation.

Sessions created before this activation (the prior always-14-day default) are untouched — ship
silently, no forced re-login — and continue to present their original `Max-Age` until natural
expiry.

### Disciplin

Additive amendment — the original 2026-05-06 decision text above is unchanged; no content beyond
this appended block was edited. See ADR 0142 for the legal-basis rationale (this pointer read "ADR 0093" until 2026-09-17, #1494;
why 180 days and why mandatory rotation above 30 days are carried there, and the opt-in itself is
reversed there on 2026-09-17).

---

## Amendment 2026-09-17 (ADR 0142, epic #1732) — persistent by default; the cookie table's `Max-Age` row; the dead `ADR 0093` pointers

*Affects the Amendment of 2026-07-05 above (the branch table and its two legal-basis pointers) and*
*the "Cookie attributes" table's `Max-Age` row. The topology, the `__Host-` discipline and the*
*three-layer CSRF mitigation are unchanged.*

1. **Persistent by default.** ADR 0142 D4 reverses the 2026-07-05 opt-in on a controller decision
   (Klas 2026-09-16): every passwordless login issues the `Persistent` profile (30 d sliding /
   180 d absolute cap / 24 h session-id rotation) and the cookie always carries `Max-Age` 180 d.
   The "Håll mig inloggad" checkbox is removed; the `Session` profile becomes dead and is retired
   in a later PR; `Legacy` is untouched. The branch table above therefore describes the state
   between 2026-07-05 and the merge of #1732 part 2, and the `Max-Age` row reads **180 days** from
   that merge. The disclosure moves to the steps that create a session, and the cookie-policy copy lands in the same PR as
   `setSessionCookie(id, true)`.
2. **The legal basis is written.** The two pointers in the 2026-07-05 amendment cited "ADR 0093"
   for the GDPR/ePrivacy analysis; 0093 is the CV-motor v2 ADR and the analysis existed nowhere
   (#1494, a same-day number collision). Both pointers now read ADR 0142, which carries the analysis
   verbatim (security-auditor, 2026-09-17): the session cookie is strictly necessary under ePrivacy
   Art. 5(3) / WP194 criterion B; the duration is disclosed where the per-device action is taken;
   Art. 25(2) is answered because under passwordless every re-login is itself processing (a mail
   through the processor, a PII record in Redis), so persistence is the minimising choice under the
   new purpose; the effective inactive-device default is 30 days; the shared-device residual is
   named and remedied by copy plus Logout on every page. **#1494 closes on this amendment.**
3. **Login-side cookies added by ADR 0142.** `__Host-jobbliggaren_login` (challenge id + submitted
   address as a display echo, httpOnly, `SameSite=Strict`, 15 min) and, in part 6a, the OAuth state
   cookie (`__Host-`, `SameSite=Lax` ≤ 10 min — Lax because the IdP callback arrives as a
   cross-site-initiated top-level navigation; **mandatory**, the callback refuses without it). The
   OAuth callback answers 200 + a document, never a 302, because the Strict session cookie does not
   ride a cross-site-initiated redirect chain — the same property the "Negative" consequence above
   already names for external links.

Additive amendment apart from the two pointer corrections in point 2, which are marked in place.

## Amendment 2026-09-26 (ADR 0142, #1744) — rule 3's two named exceptions

The OAuth start and callback (`GET /api/auth/oauth/{provider}/start` and `…/callback`) are rule 3's only
exceptions. The start writes a self-expiring anonymous record and a cookie, and reads no account. The callback
writes durably, and it cannot be a POST: a provider's `form_post` is a cross-site POST, which a Lax cookie does not
ride. Its CSRF protection is the protocol's own, the state bound to the Lax `__Host-jobbliggaren_oauth` cookie
(RFC 6749 §10.12). The binding holds on four conditions (security-auditor, 2026-09-25):

1. the cookie is `__Host-`, so no subdomain can toss one;
2. the state is at least 128 bits from a CSPRNG, and single-use (`GETDEL`);
3. the whole string is compared in constant time;
4. the record binds the provider, the path's provider must match it, and the code is exchanged only with the
   record's provider (mix-up, RFC 9700; reachable from 6b).

Additive amendment.

## Amendment 2026-10-03 (ADR 0148, #1956) — logout is a route handler

The Logout section above names a Server Action. Since #1956 every "Logga ut" form is a native
`<form method="post">` to `POST /api/auth/logout` (`app/api/auth/logout/route.ts`), rendered by
`LogoutForm`. A Server Action's id belongs to the build that rendered the page, so after a deploy a
stale page's logout never ran and the session lived on (ADR 0148, Context). A route handler runs on
the build that answers.

The handler keeps rule 2 with its helper, `isSameOriginRequest`: a request whose `Origin` does not
match its host gets 403 before the cookie is read. It answers POST alone (rule 3), revokes the backend
session best-effort, deletes the cookie through `deleteSessionCookie` as before, and answers 303 to
`/logga-in`, so the browser follows with a GET.

Additive amendment.

## Amendment 2026-10-10 (#2094) — a link another site opened reaches the page it names

The "Negative" consequence above says a page opened from an external link cannot see the session, and names a
skeleton-and-`/api/me` mitigation that was never built. What ran instead: `proxy.ts` saw no cookie and sent the
user to `/logga-in`, so every link in a notification mail, clicked in webmail, put a signed-in user on the login
page.

**The bounce.** Its one home is `web/jobbliggaren-web/src/lib/auth/strict-cookie-bounce.ts`, called first in
`proxy.ts`. It answers a request with **all** of the following with `200` and the continuation document the OAuth
callback already serves (`continuation-document.ts`):

1. the path has a return path (`return-path.ts`): a protected `(app)` prefix, or under `/admin`;
2. no session cookie arrived;
3. the method is `GET`;
4. `Sec-Fetch-Site: cross-site`, `Sec-Fetch-Mode: navigate`, `Sec-Fetch-Dest: document`, and no prefetch purpose.

The document's refresh starts on this origin, so the next request carries the cookie if the browser holds one; one
that holds none meets the ordinary login redirect, `next` intact. Its target is the return path the login `next`
would carry (the pathname, and on `/admin/feedback` its one validated id), never the request's own query, plus the
marker `vidare=1`. A request that carries the marker is never bounced again, which stops the loop a sandboxed popup
with an opaque origin would otherwise run in a browser that does not apply COOP; with the cookie it is sent on to the
clean URL with a same-origin `307`.
The marker is never written into `next`. Its edge-log verdict is `kept`: a constant `1`, written only by the proxy
and read only for presence, carrying no input and naming no person
(`strict-cookie-bounce-edge-log-inventory.test.ts`). The document carries `no-store`, exactly one
`Referrer-Policy: no-referrer`, `noindex`, `Cross-Origin-Opener-Policy: same-origin`, no `Set-Cookie` and no
subresource, and the global CSP with `frame-ancestors 'none'` still applies. The bounce calls no backend.

**What it changes.** The cookie stays `SameSite=Strict`, so a subresource, a frame, a fetch and a POST still never
carry it. But a top-level GET navigation that another site starts to one of those pages now reaches it signed in,
as `Lax` would allow:

- Layer 1's "including top-level navigation" no longer holds for a GET navigation to a protected or admin page.
- For such a GET, layer 3 ("no state changes on GET") now carries the CSRF protection that layer 1 carried. **Any
  new write at render under `(app)` or `(admin)` reopens this amendment.**
- Public pages, `/api` (outside the proxy's matcher) and a browser that sends no Fetch Metadata keep the old
  behaviour (superseded for `/junior` by the Amendment 2026-11-21, #2284). Rule 3 itself is unchanged: the bounce
  writes nothing.

**Accepted residuals.** None of these can be closed without also closing the mail click, which is itself a
cross-site navigation.

- **Writes at render** (measured 2026-10-10, `grep -rn "after(" "src/app/(app)" "src/app/(admin)" src/components`
  in `web/jobbliggaren-web`; re-measured 2026-10-19 on `e9579a640` for the amendment below). Another site that opens these pages in a top-level window can make the signed-in
  user's own page do what opening it does:
  - `/matchningar` marks the matches seen;
  - `/jobb` marks the listed ads seen;
  - `/jobb/{id}` marks a followed company's hit for that ad seen, and the other site chooses the ad by its id
    (the modal route does the same, but is reached only from inside the app);
  - `/foretag/bevakade/nya` consumes the set of new ads;
  - `/foretag/branschbevakningar/{id}/nya` marks that Branschbevakning's shown window seen, and the other site
    chooses the criterion by its id (Amendment 2026-10-18).

  Each is an integrity effect on the user's own notices, not a disclosure. `(admin)` writes nothing at render. The
  target drops the query, so a planted `/jobb?q=…&commit=true` recent search does not reach the page. (The Amendment
  2026-11-21, #2284, marks the reach of this sentence: it holds for every key with free text, and for `/junior` two
  validated keys travel on to a signed-in `/jobb`.)
- **Failed-access log lines.** Another site can open `/cv/{id}`, `/ansokningar/{id}` or a Branschbevakning page
  (`/foretag/branschbevakningar/{id}`, its `/annonser` and `/nya`; Amendment 2026-10-18) with an id the signed-in user
  does not own, and the cross-user attempt is logged against that user (`IFailedAccessLogger`). Such a line is no
  longer evidence of what the logged user meant to do.
- **Whether the user is signed in.** The page the hop leads to now depends on the session. COOP `same-origin` on the
  bounce, the window's first document, severs the opener's handle before that hop, so `window.length` and
  navigation through the handle cannot read it. A browser without COOP, and timing, remain.
- **A sandboxed popup gets a browser error.** A COOP other than `unsafe-none` turns the response into a network
  error in a navigable with sandbox flags (HTML, "create navigation params by fetching"; Chrome shows
  `ERR_BLOCKED_BY_RESPONSE`). A webmail that opens links in a sandboxed popup without
  `allow-popups-to-escape-sandbox` therefore shows an error page where it showed the login page before. Gmail does
  not sandbox the message body; no webmail that does was measured.
- **Not measured on WebKit.** Safari's handling of the refresh hop is the same open residual as the OAuth callback's.
- **Mail apps with a cookie jar of their own** (an in-app browser) never hold the session, so their users meet the
  login page; the target survives it.

**#1893, in the same change.** When `/auth/refresh` answers exactly `401` to a GET on a protected path, the proxy
answers with the login redirect and its `next`, and clears the session and refresh-throttle cookies with their
`__Host-` attributes. A `429`, a `503`, any other failure and every non-GET pass through as before. The threat
model records the premise this rests on.

Additive amendment, apart from layer 1's sentence, which is marked in place.

## Amendment 2026-10-18 (#1663 PR 2, ADR 0175 D7) — a new write at render, `/foretag/branschbevakningar/{id}/nya`

The amendment of 2026-10-10 says any new write at render under `(app)` or `(admin)` reopens it. This is that
reopening, for one write.

- **The write.** `/foretag/branschbevakningar/{id}/nya` acknowledges the window it rendered: an `after()` callback
  posts the server's token to `POST /api/v1/me/company-watch-criteria/{id}/new-ads/seen`, and only when the read
  succeeded, returned a token, and the criterion is neither refused nor unscanned. Nothing is written for an error,
  a 404, a closed state or an empty page.
- **The residual it adds**, written into "Writes at render" above: another site that opens the page in a top-level
  window can make the signed-in user's page acknowledge that Branschbevakning's new ads, and it chooses the
  criterion by its id. A foreign or unknown id is the same 404 and writes nothing. The write only moves the mark forward, clamped to now, and never past the window the page showed. It is
  an integrity effect on the user's own notices, as for the other pages, not a disclosure.
- **Failed-access log lines** now include the Branschbevakning pages, which log a cross-user attempt through
  `CriterionOwnerScopedLoader` (`security-auditor` Minor 2); written into the bullet above.
- **No `/jobb/{id}` stamp for criterion hits.** Opening an ad marks no Branschbevakning hit seen; the `/jobb/{id}`
  write above stays the follow hit's alone (`security-auditor` S-6).
- `strict-cookie-bounce-render-writes.test.ts` accepts the new page, and a further write still reopens this.

Additive amendment, apart from the two bullets above that it extends in place.

## Amendment 2026-11-21 (#2284) — the bounce answers one public path, `/junior`

> **Provenance.** Written by `adr-keeper` for the driving session from its brief; it adds no decision of its own. It records
> `senior-cto-advisor`'s ruling D-A to D-E and `security-auditor`'s plan read, both of 2026-11-21
> (`docs/reviews/2026-11-21-2284-plan-senior-cto-advisor.md` and `docs/reviews/2026-11-21-2284-plan-security-auditor.md`,
> local, transcribed by the driving session). Additive: nothing above this heading is rewritten except two sentences
> marked in place, one in "What it changes" and one in "Writes at render" of the Amendment 2026-10-10.

**Why.** Klas-direktiv 2026-11-21: "inloggad besökare på /junior → /jobb?junior=on, också när länken öppnas från
LinkedIn/Discord/FB". `/junior` is a public page that reads no session (ADR 0186 D8), and the session cookie is
`SameSite=Strict`, so a link from LinkedIn, Discord or Facebook arrives without it. The cookie is the only signal that the
visitor is signed in, and a browser withholds it on a navigation another site starts. The Amendment 2026-10-10 left every
public path outside the bounce; this one adds one. The legal outcome has one home, ADR 0166 (Amendment 2026-11-21), and is
not restated here. The product ruling is ADR 0186's Amendment 2026-11-21.

**Where it lives.** A sibling branch beside `publicJobAdRoute`, not a case inside the bounce (CTO D-A):
- `web/jobbliggaren-web/src/lib/auth/junior-it-signed-in-route.ts`, called in `proxy.ts` directly after
  `strictCookieBounce`. `strictCookieBounce`'s decisions, `returnPathFor`, `PROTECTED_PREFIXES` and the cookie's
  `SameSite=Strict` are unchanged, so `/junior` never becomes a login `next`. The branch reads no cookie itself: the proxy
  binds the refresh driver to the cookie, and a test reads the module's source for `.cookies`.
- `lib/job-ads/junior-it-handover.ts`, pure: both addresses the branch sends a request on to, from the one parse the page
  itself uses (below).
- `lib/auth/continuation-answer.ts`: the continuation document's answer and `hopTarget`, moved out of the bounce so that
  both branches import them, and the junior branch never imports `strict-cookie-bounce.ts`. It takes a target, refuses one
  that is not a path on this site, and builds the hop itself, so no caller can turn the document's refresh into a redirect
  to another host (`security-auditor` Minor 1).
- `lib/auth/session-refresh.ts`: the refresh driver, moved out of `route()` with a typed outcome (`notDue`, `failed`,
  `unknownSession`, `slid`, `rotated`). The protected tail and the junior branch both use it.

Both moves are pure, and are proven so, not assumed: `proxy.test.ts` passes with `git diff -U0` removing no assertion line,
and its lines 299–306, the test that a public path never touches the backend, are byte-unchanged.

**The condition, and the hop.** The branch acts only on exactly `/junior` with a query the page accepts. That is one parse,
`parseJuniorItParams` (the page's own, over Next's array shape for a repeated key), so the proxy never sends on a request
the page would answer with its 404, and only validated values travel. Without the session cookie, a request with **all** of
the following gets `200` and the continuation document, the one the bounce serves (`no-store`, `noindex`, COOP
`same-origin`, no `Set-Cookie`, no subresource):

1. the path is exactly `/junior`, and the query is one the page accepts;
2. no session cookie arrived;
3. the method is `GET`;
4. `Sec-Fetch-Site: cross-site`, `Sec-Fetch-Mode: navigate`, `Sec-Fetch-Dest: document`, and no prefetch purpose;
5. the request does not carry the marker `vidare=1`.

Its refresh goes to `hopTarget(juniorItHref(parsed))`: the list's canonical address for the validated `municipality`,
`sortBy` and `page` (a default left out), plus the marker. "The target drops the query" (Amendment 2026-10-10) still holds
for the gated pages; the hop carries three validated keys because the page it leads to is the public list, which reads no
session and takes no free text. A request without Fetch Metadata, and a link previewer that sends none, is served the page as
before.

The marker's edge-log verdict stays `kept`. The hop's three added keys carry the values `/jobb` carries under the same
names, so they take `/jobb`'s verdicts (`strict-cookie-bounce-edge-log-verdicts.ts`, ADR 0050 N-1). The inventory test now
reads the hop from its real producer, `hopTarget` over `juniorItHandover`, with all three keys off their default, and its
first assertion's expected set grows, declared, to `id`, `municipality`, `page`, `sortBy` and `vidare`.

**The signed-in 307.** With the session cookie, a `GET` or `HEAD` is answered `307`, `Cache-Control: no-store`, to
`buildJobbHref`: `/jobb?[municipality=…&]junior=on[&sortBy=…]`, exactly those three keys. `page` is dropped, because the two
lists are different sets (ADR 0186 A4), and so is every other key, the marker included. HEAD is answered as GET because
RFC 9110 §9.3.2 has HEAD identical to GET except that no content is sent; a GET that answers `307` beside a HEAD that
answers `200` would break that (CTO D-C). The continuation document and the strip stay GET-only: they answer navigations, and
a navigation is always a GET. Any other method falls through as before.

The shared refresh driver runs first, when it is due, and the 307 carries what its outcome asks of a response (CTO D-E):
- **Not due, or a prefetch:** the 307, with no backend call.
- **The session slid or rotated:** the 307 carrying the companion cookie, and after a rotation the new id as the session
  cookie. The id is in a `Set-Cookie` and nowhere else: never in a Location, a query, the document or a log.
- **The refresh failed** (a timeout, a network error, a status other than 401, a malformed body): the 307 with the
  30-second backoff. A failure says nothing about the session (#1893).
- **Exactly 401:** both cookies are cleared, with their `__Host-` attributes, and the request is answered as one with no
  session: the strip below if its condition holds, otherwise the public page, rendered with the session cookie taken out of
  the forwarded `cookie` header (`forwardedWithoutSession`, the nonce pair kept), so the render never carries the id the
  driver has just found dead.

Every response that carries a `Set-Cookie` is `no-store`. The refresh call carries only `Authorization` and the headers
`pickForwardedHeaders` picks (the two forwarding headers), never the path, the query or `Referer`.

**Why the session is validated when due, not only looked for (CTO D-E).** Sessions are created Persistent
(`PasswordlessSessionGrant.cs`, both creation paths; the OAuth callback, ADR 0142 D4). The server slides them for 30 days
under a cookie that lives 180 (`SessionStoreOptions.cs:38-43`, `cookie-names.ts:23-28`), so anyone idle for more than 30 days
whose browser kept the cookie holds one the server no longer knows, and that group only grows. Under a presence check each of
them would open a link to an open list and be sent to `/logga-in?next=/jobb`, and land on `/jobb` without `junior=on` after
signing in: the visitors the directive does not cover, sent the wrong way. A presence check is sound only where a false
positive still ends at the right answer. On a protected page it does, since a stale cookie ends at login, which is right
there; on a public page ADR 0017's presence gate ends at the wrong answer. Validating adds no backend call on any path: the
refresh it runs is the one the 307's `/jobb` request would have run one hop later, the companion cookie on the 307 means
`/jobb` then finds none due, and a dead cookie costs one call either way.

**The edge strip.** A request that carries the marker and has no live session is sent on to the list's clean address with a
`307`, `no-store`, to `juniorItHref(parsed)` (never "the query minus `vidare`"), carrying no `Set-Cookie` except the 401
clear above. It fires only when **all** of these hold (CTO D-B):
- the method is `GET` and the request carries the marker;
- there is no live session: no cookie, or one the driver has just cleared;
- it is a browser top-level document navigation: `Sec-Fetch-Mode: navigate`, `Sec-Fetch-Dest: document`, no prefetch;
- `Sec-Fetch-Site` is `same-origin`, `same-site` or `none`.

That is the exact complement of the bounce's own condition 4. A request without Fetch Metadata is not stripped, and a
cross-site arrival that carries the marker is never stripped: it is served the page where it is.

*Why it cannot loop (CTO D-B).*
- Only a cross-site document navigation is ever bounced.
- The Fetch Metadata specification (W3C Working Draft 2026-09-21, §2.3) computes `Sec-Fetch-Site` over a redirect's whole URL
  list against the request's origin. `none` skips that step, and §4.1 keeps it across redirects (non-normative).
- So a 307 from a request that is not cross-site has a follow-up that is not cross-site, and that follow-up cannot be
  bounced. If a browser recomputed `none`, the cost would be one extra bounce, which ends on the same-origin hop.
- A cross-site marked arrival is never stripped, so ADR 0165 D5's loop cannot form. That covers the opaque-origin hop of a
  sandboxed popup and a shared marked link alike. `security-auditor` reads it the same way: the redirected request keeps its
  initiator and is given only URLs on the same origin, so it is never cross-site.

*Why ADR 0165 D5 does not bind it.* D5 rejected "a server redirect to strip the marker (loops in the sandbox case)". That is
an *unconditional* strip. The loop needs a cross-site marked request to be stripped, and this strip never does that. The
marker now has two removers, this edge strip for `/junior` and D5's client island `StripBounceMarker` for `/jobb/<uuid>`;
the conditions explain the split, aligning them is not owed because there is no defect, and it is not filed.

**Two stated rules each gain one named, pinned exception.**
1. *"The target drops the query"* (Writes at render, above). For `/junior`, two validated keys, `municipality` (a kommun
   concept id) and `sortBy` (one of two orders), travel on the 307 to a signed-in `/jobb`, with the constant `junior=on`.
   `q`, `commit`, `employer` and every other key never reach a Location; a test names them. The rule holds for every key with
   free text.
2. *"Public paths never touch the backend"* (the proxy's own comment, pinned by `proxy.test.ts` lines 299–306). `/junior`
   with the session cookie is the one named exception, when the refresh is due and the request is not a prefetch. The
   comment in `proxy.ts` names `/junior` as that exception, the `/cv-granskning` test stays byte-unchanged, and a test for
   `/junior` is added.

**What it changes.** The cookie stays `SameSite=Strict`. A top-level GET navigation that another site starts to `/junior` now
ends signed in at `/jobb`, as a navigation to a protected page does since the Amendment 2026-10-10, because the document's
refresh starts on this origin. Nothing else gains the cookie. For that GET, "no state changes on GET" carries the CSRF
protection that layer 1 carried, as for the protected pages.

**Accepted residuals.** They add to those of the Amendment 2026-10-10.
- **R-a, a stale cookie.** A session revoked on the server is still met with the 307 for at most 15 minutes, until the next
  refresh falls due, and `/jobb`'s layout then re-verifies and sends the visitor to login (`(app)/layout.tsx:50`). Every
  protected link has the same window. After that the driver's 401 clears both cookies and the public list is served.
- **R-b, a kommun and an order chosen by another site.** Another site can choose the kommun and the order of a signed-in
  `/jobb`. A well-formed kommun id that no ad has gives an empty page, whose watermark write sends `undefined`, which the
  backend reads as now (`jobb-results.tsx:276-291`), so the user's own "new" markers move. That is an integrity effect on the
  user's own notices, not a disclosure, in the class "Writes at render" above; the bounce to an unfiltered `/jobb` already
  moves the mark about as far. Nothing can be planted: no `q` and no `commit` travel, and a junior search is never stored in
  recent searches (ADR 0186 A1). `security-auditor`: a remaining effect in that class, no accepted-risk ADR needed.
- **R-c, sandboxed popups.** No change. Caddy writes `Cross-Origin-Opener-Policy: same-origin` on every response
  (`deploy/caddy/Caddyfile:216`, `:234`), so what the browser does with it in a sandboxed navigable (see "A sandboxed popup
  gets a browser error" above) does not depend on this branch.
- **R-d, in-app browsers with a cookie jar of their own** never hold the session, so they see the public list.
- **R-e, the public list for a signed-in visitor.** Every request that carries the cookie is sent on, so a signed-in visitor
  sees the public list only in a private window. A recruiter complaint about the list (ADR 0166 D10 trigger 3) is read there.
- **R-f, link previewers** (`security-auditor`'s wording). A previewer that sends cross-site Fetch Metadata gets the
  continuation document, not the page. It is never a row or a count. Whether the platforms' previewers do this is not
  measured.
- **Another site can make the session slide or rotate on `/junior`** through the bounce, as it already can on every protected
  path.
- **The premise of #1893 and its race now reach `/junior`,** for GET and HEAD: only a rejected session answers 401, and the
  proxy's delete can land after a fresh login in another tab (`docs/threat-model.md`).
- **Whether the user is signed in** is now also told by where `/junior` ends, the list or `/jobb`. `security-auditor`: no new
  capability, since the bounce on a protected path already gives the same oracle and COOP severs the window handle.

Measured in Chromium (job-modal harness, `tests/job-modal/junior-signed-in-redirect.spec.ts`), 2026-11-21, 6 of 6 on a
production build, reading from the front proxy whether each document request carried the session cookie:
- a click on a page on 127.0.0.1 reached `/junior` as `cross-site` without the cookie and got the document;
- the document's hop arrived `same-origin` with the cookie and was answered `307` to `/jobb?junior=on`, which rendered with
  the junior filter on;
- signed out, the hop was stripped by a `307` to the clean `/junior`;
- an address-bar navigation (`none`) carried the cookie and went straight on, `none` kept across the redirect;
- a request with a LinkedInBot user agent and no Fetch Metadata got the page and its `og:title`;
- with the refresh blocked, the document's link showed after the hold.

Firefox and WebKit are not measured. The 2026-10-10 residual "Not measured on WebKit" stands for this hop.

Additive amendment, apart from the two sentences marked in place.
