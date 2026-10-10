import type { AdminOverviewSnapshot, RegistrationPeriod } from "@/lib/dto/admin-overview";

export type { AdminOverviewSnapshot } from "@/lib/dto/admin-overview";
export type OverviewObservation<T> =
  | { readonly kind: "loaded" | "empty"; readonly data: T; readonly sampledAt: string; readonly refreshFailed: boolean }
  | { readonly kind: "loading" | "failed" };

/**
 * A source that can also say that its host has not reported yet (#1982): built, and not an error. Only the
 * Backup card has such a source today; the others read the database, which always answers.
 */
export type AwaitingObservation<T> = OverviewObservation<T> | { readonly kind: "awaiting" };

export const OVERVIEW_REFRESH_MS = 60_000;
export const OVERVIEW_STALE_MS = 5 * 60_000;

export function retainObservation<T>(previous: OverviewObservation<T>, next: OverviewObservation<T>): OverviewObservation<T>;
export function retainObservation<T>(previous: AwaitingObservation<T>, next: AwaitingObservation<T>): AwaitingObservation<T>;
export function retainObservation<T>(
  previous: AwaitingObservation<T>,
  next: AwaitingObservation<T>,
): AwaitingObservation<T> {
  return next.kind === "failed" && (previous.kind === "loaded" || previous.kind === "empty")
    ? { ...previous, refreshFailed: true }
    : next;
}

export function retainOverview(
  previous: AdminOverviewSnapshot,
  next: AdminOverviewSnapshot,
): AdminOverviewSnapshot {
  return {
    accounts: retainObservation(previous.accounts, next.accounts),
    audit: retainObservation(previous.audit, next.audit),
    jobs: retainObservation(previous.jobs, next.jobs),
    backup: retainObservation(previous.backup, next.backup),
  };
}

export function accountsHref(basePath: string, period?: RegistrationPeriod, status?: string): string {
  const params = new URLSearchParams();
  if (period) {
    params.set("registeredFrom", period.from);
    params.set("registeredBefore", period.before);
  }
  if (status) params.set("status", status);
  const query = params.toString();
  return `${basePath}/anvandare${query ? `?${query}` : ""}`;
}