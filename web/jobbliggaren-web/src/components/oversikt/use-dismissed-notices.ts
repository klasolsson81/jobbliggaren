"use client";

import { useCallback, useMemo, useSyncExternalStore } from "react";

/**
 * Delad dismiss-store för Översikt-notiscentret (#726). Lyft ur notice-list.tsx
 * (F4-12) så kunskapen om "vilka notiser är lästa" lever på EN plats och delas av
 * notice-section, notice-toolbar och den kvarvarande guest-only notice-list.
 *
 * Persistens: localStorage `jp-oversikt-dismissed-notices` (samma nyckel som förr
 * — befintliga lästa notiser överlever refaktoreringen). Ingen server-action finns
 * ännu (BE notification-port saknas); state är optimistiskt klient-lokalt.
 *
 * SSR-säker via `useSyncExternalStore`: server-snapshot = tom lista, klient-snapshot
 * = faktiska localStorage-värden post-hydration. En modul-lokal lyssnar-registry
 * gör att en SKRIVNING i denna flik notifierar prenumeranterna synkront — det
 * inbyggda "storage"-eventet fyras bara i ANDRA flikar, aldrig i den som skrev.
 */
const LS_KEY = "jp-oversikt-dismissed-notices";

const listeners = new Set<() => void>();

function readRaw(): string {
  if (typeof window === "undefined") return "[]";
  try {
    return window.localStorage.getItem(LS_KEY) ?? "[]";
  } catch {
    return "[]";
  }
}

function parseIds(raw: string): ReadonlySet<string> {
  try {
    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) return new Set();
    return new Set(parsed.filter((v): v is string => typeof v === "string"));
  } catch {
    return new Set();
  }
}

function writeIds(next: ReadonlySet<string>): void {
  if (typeof window === "undefined") return;
  try {
    window.localStorage.setItem(LS_KEY, JSON.stringify([...next]));
  } catch {
    // localStorage kan vara blockerad (private-mode/Safari ITP) — degradera tyst.
  }
  for (const l of listeners) l();
}

function subscribe(callback: () => void): () => void {
  listeners.add(callback);
  if (typeof window !== "undefined") {
    window.addEventListener("storage", callback);
  }
  return () => {
    listeners.delete(callback);
    if (typeof window !== "undefined") {
      window.removeEventListener("storage", callback);
    }
  };
}

function getServerSnapshot(): string {
  return "[]";
}

export interface DismissedNoticesStore {
  /** Id:n för notiser som markerats som lästa (dismissade). */
  readonly dismissed: ReadonlySet<string>;
  /** Markera en notis som läst. */
  readonly dismiss: (id: string) => void;
  /** Markera flera notiser som lästa i en skrivning (t.ex. "Markera alla"). */
  readonly dismissMany: (ids: ReadonlyArray<string>) => void;
  /** Återställ (avmarkera) en läst notis — tar bort id:t ur arrayen. */
  readonly restore: (id: string) => void;
  /** Återställ flera lästa notiser i EN skrivning (t.ex. "Återställ lästa"). */
  readonly restoreMany: (ids: ReadonlyArray<string>) => void;
}

/**
 * Alla mutationer läser FÄRSK localStorage (inte closure-state) innan de skriver,
 * så samtidiga/sekventiella anrop komponerar korrekt och callbacks:en är stabila
 * (tomma deps → referens-identiska mellan renders).
 */
export function useDismissedNotices(): DismissedNoticesStore {
  const raw = useSyncExternalStore(subscribe, readRaw, getServerSnapshot);
  const dismissed = useMemo(() => parseIds(raw), [raw]);

  const dismiss = useCallback((id: string) => {
    const next = new Set(parseIds(readRaw()));
    next.add(id);
    writeIds(next);
  }, []);

  const dismissMany = useCallback((ids: ReadonlyArray<string>) => {
    if (ids.length === 0) return;
    const next = new Set(parseIds(readRaw()));
    for (const id of ids) next.add(id);
    writeIds(next);
  }, []);

  const restore = useCallback((id: string) => {
    const next = new Set(parseIds(readRaw()));
    if (!next.delete(id)) return;
    writeIds(next);
  }, []);

  const restoreMany = useCallback((ids: ReadonlyArray<string>) => {
    const next = new Set(parseIds(readRaw()));
    let changed = false;
    for (const id of ids) changed = next.delete(id) || changed;
    if (!changed) return;
    writeIds(next);
  }, []);

  return { dismissed, dismiss, dismissMany, restore, restoreMany };
}
