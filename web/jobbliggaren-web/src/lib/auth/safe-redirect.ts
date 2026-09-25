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

// Only ever used to parse against; the guard compares origins and emits path, query and fragment.
const PARSE_BASE = new URL("https://jobbliggaren.invalid");

const BACKSLASH = String.fromCharCode(92);

function hasControlCharacterOrBackslash(raw: string): boolean {
  for (const ch of raw) {
    const code = ch.charCodeAt(0);
    if (code < 0x20 || code === 0x7f || ch === BACKSLASH) return true;
  }
  return false;
}

/**
 * The post-login target: a path on this site, or the default. The value is parsed as a browser would
 * parse it, and only a result on the site's own origin passes. A raw control character or backslash is
 * refused before parsing, because the parser deletes tabs and newlines and reads a backslash as a slash,
 * which is how a string beginning with one slash turns into another host.
 */
export function safeRedirectPath(raw: string | null | undefined): string {
  if (!raw || !raw.startsWith("/") || hasControlCharacterOrBackslash(raw)) {
    return DEFAULT_REDIRECT_PATH;
  }

  let parsed: URL;
  try {
    parsed = new URL(raw, PARSE_BASE);
  } catch {
    return DEFAULT_REDIRECT_PATH;
  }

  if (parsed.origin !== PARSE_BASE.origin) return DEFAULT_REDIRECT_PATH;

  // Checked on the OUTPUT too: removing dot segments can leave a path that begins with two slashes, and that
  // string, handed back to a browser, names another host.
  const target = `${parsed.pathname}${parsed.search}${parsed.hash}`;
  if (target.startsWith("//") || HOME_REDIRECT_PATHS.has(target)) return DEFAULT_REDIRECT_PATH;
  return target;
}
