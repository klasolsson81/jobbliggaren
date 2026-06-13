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
 * Anti-pattern: använd inte `await mapActionError(res, ...)` — funktionen
 * är sync och läser inte body. Body-läsning post-error är förbjudet i
 * action-layer per säkerhetsinvariant.
 */
const STATE_CONFLICT_MSG =
  "Resursen är i ett otillåtet tillstånd. Ladda om sidan och försök igen.";

export function mapActionError(res: Response, fallback: string): string {
  switch (res.status) {
    case 401:
      return "Du är inte inloggad.";
    case 403:
      return "Du saknar behörighet för åtgärden.";
    case 404:
      return "Resursen hittades inte.";
    case 409:
    case 422:
      return STATE_CONFLICT_MSG;
    case 429:
      return "För många försök. Vänta en stund och försök igen.";
    default:
      return fallback;
  }
}
