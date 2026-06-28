// Shared, locale-aware relative-time helpers. Extracted from
// `lib/oversikt/aggregations.ts` (#336 CTO bind 2026-06-28) so feature modules
// (oversikt, applications, …) reuse ONE day-diff + relative-phrase knowledge
// piece instead of importing across feature boundaries or copying it (DRY). Lives
// under the `lib/i18n` concern next to `format.ts`. `aggregations.ts` re-exports
// these for its existing consumers; new code imports from here directly.

/**
 * Heltal kalenderdagar mellan `isoString` och `now` (default: Date.now()).
 * Negativ siffra om datumet ligger i framtiden. Använder UTC-trunkering
 * för stabilitet över DST-gränser.
 */
export function daysSince(isoString: string, now: Date = new Date()): number {
  const start = new Date(isoString);
  if (Number.isNaN(start.getTime())) return 0;
  const msPerDay = 86_400_000;
  const startUtc = Date.UTC(
    start.getUTCFullYear(),
    start.getUTCMonth(),
    start.getUTCDate()
  );
  const nowUtc = Date.UTC(
    now.getUTCFullYear(),
    now.getUTCMonth(),
    now.getUTCDate()
  );
  return Math.floor((nowUtc - startUtc) / msPerDay);
}

/**
 * Pure helper-translator scoped to a `*.relativeTime` namespace. The caller passes
 * `t` from `useTranslations("<ns>.relativeTime")` / `getTranslations`, so this
 * helper stays request-context-free (testable without next-intl plumbing).
 */
export type RelativeTimeTranslator = (
  key: "today" | "yesterday" | "daysAgo",
  values?: Record<string, number>,
) => string;

/**
 * Härleder en svensk relativ tids-sträng från ett ISO-datum jämfört med `now`.
 * "i dag" (0 dagar), "i går" (1 dag), "{N} dagar sedan" (>=2 dagar). Negativa
 * dagar (framtid) faller på "i dag" — bör inte uppstå vid normala BE-svar.
 * Den svenska copyn resolvas via next-intl (`<ns>.relativeTime.*`).
 */
export function formatDaysAgo(
  t: RelativeTimeTranslator,
  isoString: string,
  now: Date = new Date(),
): string {
  const days = daysSince(isoString, now);
  if (days <= 0) return t("today");
  if (days === 1) return t("yesterday");
  return t("daysAgo", { count: days });
}
