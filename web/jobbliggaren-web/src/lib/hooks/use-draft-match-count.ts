"use client";

import { useEffect, useState } from "react";
import type { DraftMatchCountRequest } from "@/lib/dto/match-count";

const DEBOUNCE_MS = 400;

/**
 * Epik #526 (ADR 0089) — live sök-preview-räknaren för matchnings-setup-modalen.
 * Räknar (debouncat ~400 ms) hur många aktiva annonser som matchar utkastets
 * sök-facetter, via BFF-routen `POST /api/me/match-count-preview`.
 *
 * Kontrakt:
 * - Nyckeln byggs av de FYRA sökbara dimensionerna (sorterade) — kompetenser och
 *   erfarenhet ingår INTE (de gallrar inte counten; Klas: kvalitet, ej filter),
 *   så en ändring i dem trigger:ar ingen refetch.
 * - `AbortController` avbryter en pågående förfrågan när utkastet ändras (den
 *   senaste vinner; en superserad förfrågan rör aldrig state).
 * - `count` behålls under laddning (ingen blink-till-tomt vid varje tangenttryck);
 *   `loading` signalerar pågående förfrågan. Vid fel/degradering nollas `count`
 *   (neutral platshållare, aldrig ett falskt 0). Initialt `null`.
 * - `enabled` (default true) kortsluter effekten när den är false (stängd modal)
 *   → ingen bakgrunds-poll mot den rate-limitade endpointen på en sida där
 *   modalen bara är monterad men ostängd (parity `use-facet-counts`).
 */
export function useDraftMatchCount(
  draft: DraftMatchCountRequest,
  enabled = true,
): {
  readonly count: number | null;
  readonly loading: boolean;
} {
  const [count, setCount] = useState<number | null>(null);
  const [loading, setLoading] = useState(false);

  // Stabil nyckel: sorterade kopior så att bara MÄNGD-ändringar (inte ordning)
  // trigger:ar en refetch. Skills/erfarenhet är medvetet uteslutna.
  const key = JSON.stringify({
    o: [...draft.occupationGroups].sort(),
    r: [...draft.regions].sort(),
    m: [...draft.municipalities].sort(),
    e: [...draft.employmentTypes].sort(),
  });

  useEffect(() => {
    // Stängd modal (endast monterad, ej öppen — t.ex. /cv-triggern) → ingen
    // förfrågan; behåll `null` tills den öppnas (parity use-facet-counts).
    if (!enabled) return;

    const { o, r, m, e } = JSON.parse(key) as {
      o: string[];
      r: string[];
      m: string[];
      e: string[];
    };
    const controller = new AbortController();

    const timer = setTimeout(() => {
      // setState deferrat till timer-callbacken (ej synkront i effect-kroppen):
      // loading = en faktisk pågående förfrågan, inte debounce-väntan.
      setLoading(true);
      void (async () => {
        try {
          const res = await fetch("/api/me/match-count-preview", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              occupationGroups: o,
              regions: r,
              municipalities: m,
              employmentTypes: e,
            }),
            signal: controller.signal,
          });
          if (controller.signal.aborted) return;
          if (!res.ok) {
            setCount(null);
            setLoading(false);
            return;
          }
          const data = (await res.json()) as { count?: unknown };
          if (controller.signal.aborted) return;
          setCount(typeof data.count === "number" ? data.count : null);
          setLoading(false);
        } catch {
          // AbortError (superserad) → låt den nya förfrågan äga state; annars nolla.
          if (!controller.signal.aborted) {
            setCount(null);
            setLoading(false);
          }
        }
      })();
    }, DEBOUNCE_MS);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [key, enabled]);

  return { count, loading };
}
