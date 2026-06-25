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
