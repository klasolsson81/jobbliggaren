import type { AdminOverviewSnapshot, RegistrationPeriod } from "@/lib/dto/admin-overview";

export type { AdminOverviewSnapshot } from "@/lib/dto/admin-overview";
export type OverviewObservation<T> =
  | { readonly kind: "loaded" | "empty"; readonly data: T; readonly sampledAt: string; readonly refreshFailed: boolean }
  | { readonly kind: "loading" | "failed" };

export const OVERVIEW_REFRESH_MS = 60_000;
export const OVERVIEW_STALE_MS = 5 * 60_000;

export function retainObservation<T>(
  previous: OverviewObservation<T>,
  next: OverviewObservation<T>,
): OverviewObservation<T> {
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