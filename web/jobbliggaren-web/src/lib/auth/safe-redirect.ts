// A plain module, not part of `actions.ts`: a "use server" file may export only async functions
// (`_action-result.ts`, #1059), and the login flow's actions need this guard as well.

// F6 P5 Punkt 4 svans-PR3 (2026-05-24, Klas-feedback "kom direkt till jobb"):
// /jobb och rot / hoppar över next-param och defaultar till /oversikt.
// Skäl: proxy-flödet redirektar unauth user från /jobb → /logga-in?next=/jobb,
// vilket bevarade /jobb som login-target trots Klas-intent "/oversikt är start-
// sidan". Andra deep links (/ansokningar/abc-123, /cv/xyz) respekteras fortfarande
// — användare som faktiskt klickat en deep link ska komma dit, men "passiv"
// landning på jobb-listan ska gå till /oversikt.
const HOME_REDIRECT_PATHS = new Set<string>(["/", "/jobb"]);

export const DEFAULT_REDIRECT_PATH = "/oversikt";

/**
 * The post-login target. Only a same-site relative path passes: `//host` and `/\host` are
 * protocol-relative URLs a browser resolves off-site, so both fall back to the default.
 */
export function safeRedirectPath(raw: string | null | undefined): string {
  if (
    raw &&
    raw.startsWith("/") &&
    !raw.startsWith("//") &&
    !raw.startsWith("/\\") &&
    !HOME_REDIRECT_PATHS.has(raw)
  ) {
    return raw;
  }
  return DEFAULT_REDIRECT_PATH;
}
