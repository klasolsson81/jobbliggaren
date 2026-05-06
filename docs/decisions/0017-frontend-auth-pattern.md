# ADR 0017: Frontend Authentication Pattern (Custom, Cookie-Based)

- **Status:** Accepted
- **Date:** 2026-05-06
- **Deciders:** Klas Olsson
- **Related:** ADR 0015 (frontend-stack), ADR 0018 (cookie/CSRF strategy)

## Context

Fas 0 closes with the milestone "register + log in on dev.jobbpilot.se".
Backend authentication (ASP.NET Core Identity, password hashing, JWT issuance)
is functional and tested end-to-end against Testcontainers since STEG 3.

This ADR scopes the **frontend** authentication pattern: how the Next.js 16
App Router application acquires, maintains, and uses an authenticated session.

OAuth (Google, LinkedIn, Facebook) is explicitly **out of scope** for STEG 4b
but the design must not preclude it.

## Decision

JobbPilot will implement custom authentication in Next.js 16, **without**
Auth.js (NextAuth) or Better Auth, using:

1. **HTTP-only, Secure cookies** for session transport (set by Next.js Route
   Handler acting as proxy to .NET backend; see ADR 0018 for cookie spec).
2. **Server-side stateful sessions** stored in Redis 8.6, keyed by an opaque
   session-id. JWT is **not** used for session transport.
3. **A thin client-side session helper** (`lib/auth/session.ts` +
   `SessionProvider`) that exposes `useSession()` to Client Components by
   fetching `/api/me` on mount. The helper does **not** store or read tokens
   client-side.
4. **Server Components** read session via `getServerSession()` which calls
   `/api/me` server-to-server, cached per request via `React.cache()`.
5. **Defense-in-depth route protection:** `middleware.ts` performs cookie
   presence check (cheap, blocks unauthenticated noise), AND each protected
   layout/page re-verifies session in its Server Component. CVE-2025-29927
   demonstrated that middleware-only protection is bypassable.
6. **Backend remains cookie-agnostic.** The Next.js proxy translates the
   session cookie into an `Authorization: Bearer <session-id>` header before
   forwarding to the backend. Backend validates the bearer token against the
   Redis session store. This keeps cookie semantics confined to the Next.js
   layer and allows future non-browser clients to authenticate against the
   same backend without cookie support.
7. **OAuth-readiness without OAuth-implementation:** route group
   `(auth)/oauth/[provider]/callback/route.ts` reserved as 501 stub. Backend
   `User` entity gets `AuthProvider` enum column (Local | Google | LinkedIn |
   Facebook) and unique `(Provider, ProviderUserId)` constraint via a
   migration in this STEG. No OAuth endpoints are wired.

## Considered Alternatives

### Auth.js v5 (NextAuth)
**Rejected** because: maintenance status (Better Auth team took over Sept 2025,
in security-patch mode), opinionated session-cookie format incompatible with
our existing .NET backend, and additional dependency surface.

### Better Auth
**Rejected** because: assumes its own user table schema, requires us to
rewrite or duplicate ASP.NET Core Identity which already works.

### Stateless JWT in cookie (no Redis lookup)
**Rejected** because: cannot revoke before expiry without building a denylist
(which is just stateful sessions with extra steps), and refresh-token rotation
introduces race-condition edge cases that solo-developer cannot reasonably
test exhaustively. GDPR "delete my account" is more naturally a SQL DELETE
on a session table than a denylist insert.

### Localstorage / sessionStorage for tokens
**Rejected** because: XSS-readable. Industry consensus and Next.js
documentation explicitly warn against this pattern.

## Log and Audit Policy

Session-id values are opaque high-entropy tokens equivalent in sensitivity to
passwords. The following rules are non-negotiable:

- **Session-id and JTI MUST NOT appear in logs, error messages, exception
  details, or response bodies.** Structural enforcement: `SessionId` is a
  `readonly record struct` whose `ToString()` returns a 6-character prefix
  followed by `…`. Raw value is only accessible via the explicit `Reveal()`
  method, which must only be called at Redis key derivation and cookie write
  sites.
- Redis keys store a SHA-256 hash of the raw session-id (base64url-encoded),
  not the raw token. This ensures a Redis dump cannot be used for direct
  impersonation.
- `userId` (Guid) may appear in logs as a correlation key. It is PII under
  GDPR but pseudonymous and already present in other audit traces.
- This policy applies to both `ISessionStore` implementations (InMemory and
  Redis) and all call sites in the auth pipeline.

## Consequences

### Positive
- One source of truth for session state (Redis), invalidatable on demand.
- No client-side token handling → smaller XSS attack surface.
- Backend stays bearer-token based; existing JWT validation is replaced by
  session-store lookup but the wire protocol on the backend boundary is
  unchanged from a client-API contract perspective.
- OAuth can be added in Fas 1 without schema migration (column already there).
- Backend is reusable for non-browser clients (mobile, integrations).

### Negative
- Per-request Redis lookup (~5-15ms). Acceptable for our scale.
- More moving parts than a managed library (Auth.js).
- We own all security-sensitive code: rate-limiting on `/auth/login`,
  password-reset flow, account-lockout. (Rate-limiting deferred to Fas 1;
  noted as TODO, not silently dropped.)

### Neutral
- Session helper is intentionally minimal. Future auth features (MFA,
  passkeys) will require revisiting this ADR.

## Out of Scope (Deferred)

- OAuth provider integration (Fas 1)
- MFA / passkeys (Fas 1+)
- Rate-limiting on auth endpoints (Fas 1, security-not-cost rationale,
  separate from ADR 0005 which covers cost protection)
- Password reset flow (Fas 1)
- Email verification (Fas 1)
- "Remember me" / persistent sessions (Fas 1)
- Secondary user-sessions index — efficient bulk invalidation for
  `InvalidateAllForUserAsync` (GDPR erasure via SCAN-based fallback until
  implemented). Required by account-deletion flow; must be implemented
  synchronously before SQL DELETE commits.
- JTI value-object migration — coherent refactor with `SessionId` pattern,
  deferred to Fas 1. JTI is a public JWT claim (not a bearer secret), so
  raw JTI in Redis keys is acceptable; the refactor is for architectural
  consistency, not security necessity.

## References

- Next.js 16 Authentication Guide: https://nextjs.org/docs/app/guides/authentication
- CVE-2025-29927 (middleware bypass)
- ADR 0015 (frontend stack)
- ADR 0018 (cookie/CSRF strategy)
