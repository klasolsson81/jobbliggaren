import type { useTranslations } from "next-intl";

/**
 * Mappar HTTP-statuskoder från Server Action fetch-svar till svenska
 * användartexter. Backend-`body` läses ALDRIG — `body?.detail` /
 * `body?.title` från ASP.NET ProblemDetails kan innehålla stacktrace,
 * SQL-fel eller annan intern info som bryter GDPR Art. 5(1)(f) om det
 * läcker till UI. Se TD-10 + OWASP ASVS V8.2.
 *
 * Status-koden är hela sanningen. Per-action `fallback`-text används
 * för statuskoder utanför whitelisten (inklusive 500). Frontend
 * Zod-validering körs före fetch — 422 från backend tolkas som
 * "Resursen är i ett otillåtet tillstånd" snarare än per-fält-fel
 * (CTO-beslut 2026-05-11, ADR 0030-symmetri för writes).
 *
 * Status-texterna resolvas via next-intl (`errors`-namespace). Anroparen
 * (en server-action) hämtar `t` via `await getTranslations("errors")` och
 * skickar in den — samma factory-mönster som validerings-schemana.
 *
 * Anti-pattern: använd inte `await mapActionError(res, ...)` — funktionen
 * är sync och läser inte body. Body-läsning post-error är förbjudet i
 * action-layer per säkerhetsinvariant.
 */
export type ErrorsTranslator = ReturnType<typeof useTranslations<"errors">>;

export function mapActionError(
  res: Response,
  fallback: string,
  t: ErrorsTranslator,
): string {
  switch (res.status) {
    case 401:
      return t("notLoggedIn");
    case 403:
      return t("forbidden");
    case 404:
      return t("notFound");
    case 409:
    case 422:
      return t("stateConflict");
    case 429:
      return t("tooManyAttempts");
    default:
      return fallback;
  }
}
