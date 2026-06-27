import type { useFormatter } from "next-intl";

// Shared, locale-aware date/number formatting (ADR 0078 follow-up; issues
// #189 DRY + #190 locale-aware). The active locale is resolved per request
// from the NEXT_LOCALE cookie, so formatting must route through next-intl's
// formatter rather than a hardcoded `sv-SE` literal — otherwise an `en`
// reader sees Swedish grouping/months.
//
// Architecture (CTO 2026-06-25, Variant B): pure functions that take the
// next-intl formatter as a parameter. A hook-only design is impossible because
// `formatDate` is consumed in an async Server Component
// (app/(app)/@modal/(.)ansokningar/[id]/page.tsx), where React hooks cannot
// run. Callers acquire the formatter for their context and pass it in:
//   - Client + synchronous Server Components: `const f = useFormatter()`
//   - Async Server Components:                `const f = await getFormatter()`
// One knowledge piece, one place (DRY); locale acquisition stays the caller's
// concern, which is the only thing that legitimately differs sync vs async.

/**
 * The subset of the next-intl `useFormatter()` / `getFormatter()` result these
 * utilities need (interface segregation). `Pick` keeps next-intl's exact,
 * locale-narrowed option types, so the real formatter is assignable as-is.
 */
export type JpFormatter = Pick<
  ReturnType<typeof useFormatter>,
  "number" | "dateTime"
>;

/**
 * Locale-aware grouped integer: "1 234" in sv (U+00A0 grouping per CLAUDE.md
 * §10), "1,234" in en. Replaces the duplicated `formatThousands` regex helpers
 * and the ad-hoc `toLocaleString("sv-SE")` call sites.
 */
export function formatNumber(format: JpFormatter, value: number): string {
  return format.number(value);
}

/**
 * Short, locale-aware date: "18 maj 2026" in sv, "May 18, 2026" in en. Returns
 * null for missing or unparseable input so callers can omit the row, preserving
 * the contract of the former `formatSvDate`. CLAUDE.md §10 (locale conventions).
 */
export function formatDate(
  format: JpFormatter,
  value: string | null | undefined,
): string | null {
  if (!value) return null;
  const date = new Date(value);
  if (isNaN(date.getTime())) return null;
  return format.dateTime(date, {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
}

/**
 * Ledger-style timestamp "YYYY-MM-DD HH:mm" (24h, Europe/Stockholm), for admin
 * operator tables where rows must align column-wise for skim comparison
 * (audit-log / background-jobs convention, CLAUDE.md §10). Returns null for
 * missing/unparseable input so callers can show an "Aldrig körd"-style label.
 *
 * Unlike `formatDate` this shape is intentionally locale-stable: the ISO-like
 * `YYYY-MM-DD HH:mm` ordering is a fixed operator convention, not a localized
 * presentation, so it reads identically in sv and en. The next-intl formatter
 * is still the timezone authority (resolves the configured Europe/Stockholm
 * zone deterministically across SSR and client). `hour12` is pinned false.
 */
export function formatDateTime(
  format: JpFormatter,
  value: string | null | undefined,
): string | null {
  if (!value) return null;
  const date = new Date(value);
  if (isNaN(date.getTime())) return null;
  // Compose the ledger shape from two timezone-resolved fragments so the row
  // stays column-aligned in both locales. sv already yields "YYYY-MM-DD" /
  // "HH:mm"; en yields "MM/DD/YYYY", which is reordered to ISO below. The
  // formatter (not a hardcoded locale literal) remains the timezone authority.
  const time = format.dateTime(date, {
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  });
  const day = format.dateTime(date, {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  });
  const iso = /^\d{4}-\d{2}-\d{2}$/.test(day)
    ? day
    : day.replace(/^(\d{2})\/(\d{2})\/(\d{4})$/, "$3-$1-$2");
  return `${iso} ${time}`;
}

/**
 * Locale-aware 24-hour clock time: "14:32" in both sv and en. A Swedish civic
 * utility uses the 24h clock per CLAUDE.md §10 (no AM/PM), so `hour12` is pinned
 * false — only the timezone-resolved value is locale/zone-aware, not the clock
 * style. Unlike `formatDate` this takes a known-valid `Date` (a save-confirmation
 * timestamp from `new Date()`), so it is non-nullable. Replaces the ad-hoc
 * `toLocaleTimeString("sv-SE", { hour, minute })` call sites embedded in `t()`
 * status messages.
 */
export function formatTime(format: JpFormatter, value: Date): string {
  return format.dateTime(value, {
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  });
}
