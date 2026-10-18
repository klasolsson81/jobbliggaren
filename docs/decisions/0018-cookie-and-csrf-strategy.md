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
  behaviour. Rule 3 itself is unchanged: the bounce writes nothing.

**Accepted residuals.** None of these can be closed without also closing the mail click, which is itself a
cross-site navigation.

- **Writes at render** (measured 2026-10-10, `grep -rn "after(" "src/app/(app)" "src/app/(admin)" src/components`
  in `web/jobbliggaren-web`; re-measured 2026-10-18 on `cf1afb2e0` for the amendment below). Another site that opens these pages in a top-level window can make the signed-in
  user's own page do what opening it does:
  - `/matchningar` marks the matches seen;
  - `/jobb` marks the listed ads seen;
  - `/jobb/{id}` marks a followed company's hit for that ad seen, and the other site chooses the ad by its id
    (the modal route does the same, but is reached only from inside the app);
  - `/foretag/bevakade/nya` consumes the set of new ads;
  - `/foretag/branschbevakningar/{id}/nya` marks that Branschbevakning's shown window seen, and the other site
    chooses the criterion by its id (Amendment 2026-10-18).

  Each is an integrity effect on the user's own notices, not a disclosure. `(admin)` writes nothing at render. The
  target drops the query, so a planted `/jobb?q=…&commit=true` recent search does not reach the page.
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
  criterion by its id. A foreign or unknown id is the same 404, writes nothing and is logged as a cross-user
  attempt. The write only moves the mark forward, clamped to now, and never past the window the page showed. It is
  an integrity effect on the user's own notices, as for the other pages, not a disclosure.
- **Failed-access log lines** now include the Branschbevakning pages, which log a cross-user attempt through
  `CriterionOwnerScopedLoader` (`security-auditor` Minor 2); written into the bullet above.
- **No `/jobb/{id}` stamp for criterion hits.** Opening an ad marks no Branschbevakning hit seen; the `/jobb/{id}`
  write above stays the follow hit's alone (`security-auditor` S-6).
- `strict-cookie-bounce-render-writes.test.ts` accepts the new page, and a further write still reopens this.

Additive amendment, apart from the two bullets above that it extends in place.
