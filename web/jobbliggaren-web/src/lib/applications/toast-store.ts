import type { ApplicationStatus } from "@/lib/dto/applications";

/**
 * Ephemeral module-store for the /ansokningar action toast (#630 PR 7, design
 * handoff §10; CTO-bind 2). Client-only module-store idiom: module state that
 * crosses React trees WITHOUT living in any component — here it must cross the
 * pipeline island AND the intercepting-route modal (two sibling trees under
 * the (app) layout), so a single ToastHost mounted in the layout renders
 * whatever either tree publishes.
 *
 * Single-toast model (prototype-exact): publishing replaces the current toast.
 * `token` disambiguates timers/dismissal — a stale auto-close for a replaced
 * toast must never dismiss its successor.
 *
 * ADR 0092 D3: the undo payload carries the PREVIOUS status; undo is a second,
 * equally real, audited inverse transition — never a deferred/staged write.
 */
export type ApplicationToast =
  | {
      kind: "statusChange";
      token: number;
      applicationId: string;
      /** Display name for "{company}: {from} → {to}" (jobAd company or the row's fallback title). */
      company: string;
      from: ApplicationStatus;
      to: ApplicationStatus;
    }
  | {
      // Bulk-statusbyte (#630 PR 10, Tabell-vyns bulkrad). EN toast för hela
      // gruppen; grupp-ångran skickar ETT batch-anrop där varje app återförs
      // till SIN egen `from` (per-item previous — därav `items`, inte ett delat
      // `from`). ADR 0092 D3: kompenserande invers, aldrig tidslinjeradering.
      kind: "statusChangeBatch";
      token: number;
      /** Antal ansökningar (visas som "{count} ansökningar markerade som {to}"). */
      count: number;
      to: ApplicationStatus;
      items: { applicationId: string; from: ApplicationStatus }[];
    }
  | { kind: "followUpLogged"; token: number; company: string }
  | { kind: "error"; token: number; message: string };

export type ApplicationToastInput =
  | Omit<Extract<ApplicationToast, { kind: "statusChange" }>, "token">
  | Omit<Extract<ApplicationToast, { kind: "statusChangeBatch" }>, "token">
  | Omit<Extract<ApplicationToast, { kind: "followUpLogged" }>, "token">
  | Omit<Extract<ApplicationToast, { kind: "error" }>, "token">;

let current: ApplicationToast | null = null;
let nextToken = 1;
const listeners = new Set<() => void>();

function emit(): void {
  for (const listener of listeners) listener();
}

/** Publish a toast (replaces any current one). Returns its token. */
export function showApplicationToast(input: ApplicationToastInput): number {
  const token = nextToken++;
  current = { ...input, token } as ApplicationToast;
  emit();
  return token;
}

/**
 * Dismiss the current toast. With a token: only if it is still the current
 * one (a stale auto-close timer never kills a newer toast). Without: always.
 */
export function dismissApplicationToast(token?: number): void {
  if (current == null) return;
  if (token != null && current.token !== token) return;
  current = null;
  emit();
}

export function subscribeApplicationToast(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function getApplicationToastSnapshot(): ApplicationToast | null {
  return current;
}

/** SSR snapshot for useSyncExternalStore — there is never a toast server-side. */
export function getApplicationToastServerSnapshot(): null {
  return null;
}
