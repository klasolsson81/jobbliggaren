"use client";

import { useCallback, useMemo, useSyncExternalStore } from "react";

/**
 * Delad notis-inställnings-store för Översikt-notiscentret (#726) — vilka
 * notistyper per sektion användaren vill se. Samma store-mönster som
 * `use-dismissed-notices` (useSyncExternalStore + localStorage + modul-lokal
 * lyssnar-registry).
 *
 * Persistens: localStorage `jp-oversikt-notice-prefs`, form
 * `Record<"<source>:<type>", boolean>` där `false` = typen är avstängd. En
 * saknad nyckel = påslagen (default-on). Korrupt/saknad JSON degraderar till
 * allt-påslaget. Tills en BE-port finns är detta klient-lokal state.
 */
const LS_KEY = "jp-oversikt-notice-prefs";

const listeners = new Set<() => void>();

function readRaw(): string {
  if (typeof window === "undefined") return "{}";
  try {
    return window.localStorage.getItem(LS_KEY) ?? "{}";
  } catch {
    return "{}";
  }
}

function parsePrefs(raw: string): Record<string, boolean> {
  try {
    const parsed = JSON.parse(raw) as unknown;
    if (parsed === null || typeof parsed !== "object" || Array.isArray(parsed)) {
      return {};
    }
    const out: Record<string, boolean> = {};
    for (const [key, value] of Object.entries(
      parsed as Record<string, unknown>,
    )) {
      if (typeof value === "boolean") out[key] = value;
    }
    return out;
  } catch {
    return {};
  }
}

function writePrefs(next: Record<string, boolean>): void {
  if (typeof window === "undefined") return;
  try {
    window.localStorage.setItem(LS_KEY, JSON.stringify(next));
  } catch {
    // localStorage blockerad → degradera tyst (paritet use-dismissed-notices).
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
  return "{}";
}

function prefKey(source: string, type: string): string {
  return `${source}:${type}`;
}

export interface NoticePrefsStore {
  /** En typ är påslagen om den inte explicit satts till `false`. */
  readonly isEnabled: (source: string, type: string) => boolean;
  /** Växla en typ på/av. */
  readonly toggle: (source: string, type: string) => void;
}

export function useNoticePrefs(): NoticePrefsStore {
  const raw = useSyncExternalStore(subscribe, readRaw, getServerSnapshot);
  const prefs = useMemo(() => parsePrefs(raw), [raw]);

  const isEnabled = useCallback(
    (source: string, type: string) => prefs[prefKey(source, type)] !== false,
    [prefs],
  );

  const toggle = useCallback((source: string, type: string) => {
    const current = parsePrefs(readRaw());
    const key = prefKey(source, type);
    const enabled = current[key] !== false;
    writePrefs({ ...current, [key]: !enabled });
  }, []);

  return { isEnabled, toggle };
}
