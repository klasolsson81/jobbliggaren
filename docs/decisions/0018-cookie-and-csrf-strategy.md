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
   session cookie on any cross-site request, including top-level navigation.
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
silent 14-day cookie, with no user choice. Per **ADR 0093** (persistent-login opt-in activation —
full GDPR/ePrivacy legal-basis analysis there; local-only file per the ADR 0072 docs-privacy
convention), the cookie now branches on an explicit "Håll mig inloggad" checkbox at login/register:

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
this appended block was edited. See ADR 0093 for the legal-basis rationale (why opt-in rather than
silent-default, why 180 days, why mandatory rotation above 30 days).
