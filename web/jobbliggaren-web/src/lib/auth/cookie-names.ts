// Shared cookie-name + cookie-lifetime constants for the JobbPilot session.
//
// This module deliberately has NO `import "server-only"` guard: the Next proxy
// (`src/proxy.ts`) runs on the nodejs runtime but is NOT a Server Component /
// Server Action, so it cannot import `session.ts` (which is `server-only`).
// Keeping the shared names here lets both the server-only session helpers and
// the proxy read one source of truth without duplicating string literals.

export const SESSION_COOKIE_NAME = "__Host-jobbliggaren_session";

// Non-secret companion cookie that throttles the proxy refresh driver (stores the epoch
// seconds after which the next /auth/refresh is due). Carries no credential.
export const REFRESH_AFTER_COOKIE_NAME = "__Host-jobbliggaren_refresh_after";

// Persistent ("Håll mig inloggad" ticked) cookie Max-Age = the 180d absolute cap. The
// server is the SSOT for expiry (30d sliding + 180d cap from CreatedAt); this is just the
// finite ceiling so the cookie survives browser restarts (never an infinite cookie).
// MIRROR of the backend `SessionStoreOptions.Persistent.AbsoluteTtl` (180d) — if the
// server cap ever changes, update both sides (the cookie must not outlive the server cap).
export const PERSISTENT_MAX_AGE_SECONDS = 180 * 24 * 60 * 60;
