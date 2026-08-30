"use client";

import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useSyncExternalStore,
} from "react";

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

/**
 * Överskriver modul-storen för det träd den omsluter (CTO-dom 2026-08-29, #1572).
 *
 * Injektionssömmen finns för att `oversikt-page.tsx` är en Server Component: prefs-
 * läsningen är därför tvingad ut i klient-löven (`notice-section`, `mark-all-read-row`),
 * och en yta utan preferenser kan inte uttryckas där utan att varje löv bär en egen
 * flagga. En store-variant i stället för en flagga per konsument: `null` = använd
 * modul-storen (app-ytan, oförändrad).
 *
 * Providern bor i `notice-prefs-provider.tsx` — värdet MÅSTE konstrueras på
 * klientsidan, eftersom ett funktionsbärande `value` inte kan passera RSC-gränsen.
 */
export const NoticePrefsContext = createContext<NoticePrefsStore | null>(null);

/**
 * Inert prenumeration + ögonblicksbild för ett träd som har en injicerad store.
 *
 * Modul-konstanter, inte inline-closures: `useSyncExternalStore` jämför referenser, och
 * en ny funktion per render hade gett en prenumerationscykel per render. `"{}"` är en
 * primitiv, alltså referens-stabil mellan anrop.
 */
const inertSubscribe = (): (() => void) => () => {};
const inertSnapshot = (): string => "{}";

export function useNoticePrefs(): NoticePrefsStore {
  // Hookarna kallas ovillkorligt och i fast ordning — att hoppa över
  // `useSyncExternalStore` bakom ett villkor vore ett Rules-of-Hooks-brott. Det som
  // väljs på contexten är store-FUNKTIONERNA, inte returvärdet: en injicerad store ska
  // inte bara göra läsningen verkningslös, den ska låta bli att läsa. ePrivacy Art. 5(3)
  // täcker åtkomst till redan lagrad information separat från lagringen, och en
  // gäst-yta som kastar resultatet kan inte kalla åtkomsten nödvändig
  // (`security-auditor` Minor 1, 2026-08-29).
  const injected = useContext(NoticePrefsContext);
  const raw = useSyncExternalStore(
    injected ? inertSubscribe : subscribe,
    injected ? inertSnapshot : readRaw,
    getServerSnapshot,
  );
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

  return injected ?? { isEnabled, toggle };
}
