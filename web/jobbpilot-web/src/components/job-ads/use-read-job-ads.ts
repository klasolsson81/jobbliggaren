"use client";

import { useCallback, useSyncExternalStore } from "react";

/**
 * Klient-side "läst"-state för jobbannonser. Driver NY-taggens conditional
 * render i JobTags: NY visas om `isNew` (server-flagga, ≤7 dygn) OCH annonsen
 * inte är markerad läst i localStorage.
 *
 * Mönster: useSyncExternalStore (sanktionerad API för external store-synk —
 * undviker setState-in-effect och hydration-mismatch). Server-snapshot är
 * alltid "inte läst" → NY visas i RSC-payloaden om `isNew=true`; vid hydration
 * läses localStorage och taggen försvinner om annonsen är markerad läst.
 * "Försvinnande" är mindre påträngande visuell-shift än "tillkomst", och
 * accepterad i samma anda som theme-provider.tsx:s pre-paint-mönster.
 *
 * Storage-format: en JSON-objekt-map `{ [id]: true }` under nyckeln
 * `jp-read-jobads`. Felsäker mot quota/disabled storage (returnerar tom map).
 */

const STORAGE_KEY = "jp-read-jobads";
const CHANGE_EVENT = "jp-read-jobads-change";

type ReadMap = Readonly<Record<string, true>>;

const EMPTY: ReadMap = Object.freeze({});

function readStore(): ReadMap {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return EMPTY;
    const parsed = JSON.parse(raw) as unknown;
    if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
      return parsed as ReadMap;
    }
    return EMPTY;
  } catch {
    return EMPTY;
  }
}

function writeStore(next: ReadMap): void {
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
  } catch {
    // localStorage blockerad eller full — markeringen gäller bara sessionen.
  }
  window.dispatchEvent(new Event(CHANGE_EVENT));
}

let cachedSnapshot: ReadMap = EMPTY;
let cachedRaw: string | null = null;

function getSnapshot(): ReadMap {
  // Cache:a snapshot:en så useSyncExternalStore får referentiellt stabil
  // referens mellan render utan store-ändring (annars infinite loop-risk).
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (raw === cachedRaw) return cachedSnapshot;
    cachedRaw = raw;
    cachedSnapshot = readStore();
    return cachedSnapshot;
  } catch {
    return EMPTY;
  }
}

function getServerSnapshot(): ReadMap {
  return EMPTY;
}

function subscribe(onChange: () => void): () => void {
  const onStorage = (e: StorageEvent) => {
    if (e.key === STORAGE_KEY) {
      // Invalidera cache vid storage-event från annan flik.
      cachedRaw = null;
      onChange();
    }
  };
  window.addEventListener("storage", onStorage);
  window.addEventListener(CHANGE_EVENT, onChange);
  return () => {
    window.removeEventListener("storage", onStorage);
    window.removeEventListener(CHANGE_EVENT, onChange);
  };
}

export function useReadJobAds(): {
  isRead: (id: string) => boolean;
  markRead: (id: string) => void;
} {
  const map = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);

  const isRead = useCallback((id: string) => map[id] === true, [map]);

  const markRead = useCallback((id: string) => {
    const current = readStore();
    if (current[id] === true) return;
    const next: ReadMap = { ...current, [id]: true };
    cachedRaw = null;
    writeStore(next);
  }, []);

  return { isRead, markRead };
}
