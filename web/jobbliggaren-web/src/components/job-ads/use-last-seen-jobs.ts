"use client";

import { useSyncExternalStore } from "react";

/**
 * "Senaste besök på /jobb"-tidsstämpel (high-water mark) som driver NY-taggens
 * conditional render. Modell (Klas-direktiv 2026-05-20, bekräftad 2026-05-23):
 * NY visas på allt med `publishedAt > lastSeen` istället för per-annons
 * "läst"-state. Användarens sid-besök markerar HELA listan som sedd — nästa
 * besök ser bara annonser publicerade efter förra besöket som NY.
 *
 * Server-cap `isNew` (≤7d, ADR 0042 Beslut E) består som defensivt golv: om
 * en användare inte besökt /jobb på t.ex. 3 månader vill vi inte rendera
 * hundratals NY-taggar — server-flaggan kapar fönstret till 7 dygn oavsett.
 *
 * Snapshot-vid-mount-mönster (via useSyncExternalStore): värdet är stabilt
 * under sessionen så NY-status inte hoppar mid-render. Skrivningen av "nu"
 * sker en gång per sid-besök via separat `<MarkJobbVisited />`-island
 * (`mark-jobb-visited.tsx`), INTE per kort — annars 20+ skrivningar per
 * listrendering.
 *
 * Beteende-exempel (Klas-bekräftat 2026-05-23):
 * - Förstagångsbesök (localStorage tom) → ALLA annonser inom 7d får NY.
 * - MarkJobbVisited skriver `lastSeen = now` vid mount.
 * - Refresh → läser nya `lastSeen` → annonser publicerade FÖRE refreshen
 *   tappar NY; annonser publicerade EFTER refresh-tidsstämpeln behåller NY.
 * - Om en annons publiceras 21:00 och du besöker 22:00, refreshar 22:30 →
 *   annonsen visar inte längre NY (publishedAt < lastSeen).
 */

const STORAGE_KEY = "jp-jobb-last-seen";

export function readLastSeen(): number {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return 0;
    const n = Number(raw);
    return Number.isFinite(n) && n > 0 ? n : 0;
  } catch {
    return 0;
  }
}

export function markJobbVisited(): void {
  try {
    window.localStorage.setItem(STORAGE_KEY, String(Date.now()));
  } catch {
    // localStorage blockerad — markeringen gäller bara sessionen.
  }
}

let cachedSnapshot = 0;
let cachedRaw: string | null = null;

function getSnapshot(): number {
  // Cache:a snapshot:en så useSyncExternalStore får referentiellt stabil
  // referens mellan render utan store-ändring.
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (raw === cachedRaw) return cachedSnapshot;
    cachedRaw = raw;
    cachedSnapshot = readLastSeen();
    return cachedSnapshot;
  } catch {
    return 0;
  }
}

function getServerSnapshot(): number {
  // Server-snapshot=0 → allt nyare räknas som NY i RSC-payloaden. Klient
  // hydration läser faktisk lastSeen och döljer NY på äldre annonser om
  // användaren redan besökt sidan. "Försvinnande" är mindre påträngande
  // visuell-shift än "tillkomst", och accepterat i samma anda som
  // theme-provider.tsx:s pre-paint-mönster.
  return 0;
}

function subscribe(): () => void {
  // Ingen live-uppdatering behövs — snapshot vid mount räcker. Sid-besöket
  // skriver lastSeen men den nya baseline-tidsstämpeln tillämpas FÖRST vid
  // nästa sid-besök (medvetet — användaren ska se NY på alla annonser i
  // nuvarande session, inte få dem markerade lästa under tiden de tittar).
  return () => {};
}

export function useLastSeenJobs(): number {
  return useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
}
