"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { landingStatsDtoSchema, type LandingStatsDto } from "@/lib/dto/landing";
import { formatLandingNumber } from "@/components/landing/landing-stats-format";

/**
 * App-header live-stats för inloggade. Polling-baserad delta-affordans:
 *
 * <ul>
 *   <li>Initial-värdet hämtas server-side i `(app)/layout.tsx` och passeras
 *       som prop — ingen flash-of-empty-state.</li>
 *   <li>Klienten pollar `/api/landing-stats` var 10:e minut (Klas-direktiv
 *       2026-05-24). Worker-cronnen refreshar Redis var 5:e min, så
 *       worst-case latens från ny annons → synlig är ~15 min.</li>
 *   <li>När polling-svaret ger högre `newToday` än senaste sedda värdet
 *       visas en grön <code>+N</code>-pill via fade-in (200ms), syns i 8
 *       sekunder, sen fade-out (Klas-feedback 2026-05-24 svans-PR5 —
 *       tidigare "stay forever" upplevdes som "livräknaren har +1 hela tiden"
 *       istället för "nu kom det in nya jobb"-affordance).</li>
 * </ul>
 *
 * Rate-limit-budget: 10-min polling = 0.1 req/min per tab; backend
 * `LandingPublicReadPolicy` är 60/min/IP → rooom för 600 öppna tabbar.
 *
 * Vid network-fel / 5xx behåller komponenten senaste lyckade värdet (ingen
 * synlig regression). 429 från backend hanteras av proxy:n som 503 → samma
 * "behåll nuvarande"-disciplin.
 */
const POLL_INTERVAL_MS = 10 * 60 * 1000;
const DELTA_VISIBLE_MS = 8_000;

export function HeaderStats({
  initialStats,
}: {
  initialStats: LandingStatsDto;
}) {
  const [stats, setStats] = useState<LandingStatsDto>(initialStats);
  const [deltaToday, setDeltaToday] = useState<number>(0);
  // Track previous newToday för delta-jämförelse. Initieras till samma
  // värde som initialStats så första polling-svar inte visar falsk delta.
  const previousNewToday = useRef<number>(initialStats.newToday);
  // Unik key för fade-in-animationen — bumpar varje gång en ny delta visas
  // så React monterar om elementet och CSS-keyframes startar om.
  const [deltaKey, setDeltaKey] = useState<number>(0);
  // Auto-clear-timer för delta-pillen (Klas-feedback 2026-05-24 svans-PR5).
  // Ref håller pågående timer så ny delta innan timeout-utgång nollställer
  // gammal timer och startar om — pillen syns 8s från SENASTE delta, inte
  // permanent. Unmount-cleanup hindrar timer från att fira på unmount.
  const deltaTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const poll = useCallback(async () => {
    try {
      const res = await fetch("/api/landing-stats", { cache: "no-store" });
      if (!res.ok) return;
      const raw: unknown = await res.json();
      const parsed = landingStatsDtoSchema.safeParse(raw);
      if (!parsed.success) return;

      const next = parsed.data;
      const diff = next.newToday - previousNewToday.current;
      // Mutera ref + state först efter alla await:s passerat (code-reviewer
      // M2 — undvik strict-mode-doublet-fire-ratchet). setState i React 19
      // är safe-on-unmount; ingen extra cancelled-flag behövs här.
      previousNewToday.current = next.newToday;
      setStats(next);
      if (diff > 0) {
        setDeltaToday(diff);
        setDeltaKey((k) => k + 1);
        // Restart auto-clear-timer — om ny delta kommer innan tidigare
        // pill hunnit nollställas, så restartas synligheten med ny delta.
        if (deltaTimerRef.current !== null) {
          clearTimeout(deltaTimerRef.current);
        }
        deltaTimerRef.current = setTimeout(() => {
          setDeltaToday(0);
          deltaTimerRef.current = null;
        }, DELTA_VISIBLE_MS);
      }
    } catch {
      // Polling-fel = behåll nuvarande värde. Civic-utility:
      // användaren ser inga "Något gick fel"-toast.
    }
  }, []);

  useEffect(() => {
    // Visibility-aware polling (code-reviewer M1 2026-05-24): polla bara när
    // tabben är synlig — undviker onödig nätverkslast för bakgrundsfönster.
    // Vid revisit (visibility → visible) triggas en omedelbar poll så
    // användaren inte ser stale-data tills nästa 10-min-tick.
    const tick = () => {
      if (typeof document === "undefined") return;
      if (document.visibilityState === "visible") void poll();
    };
    const onVisibility = () => {
      if (document.visibilityState === "visible") void poll();
    };

    const id = setInterval(tick, POLL_INTERVAL_MS);
    document.addEventListener("visibilitychange", onVisibility);
    return () => {
      clearInterval(id);
      document.removeEventListener("visibilitychange", onVisibility);
      // Cleanup pågående delta-timer vid unmount så pending setState inte
      // försöker exekvera på unmounted komponent.
      if (deltaTimerRef.current !== null) {
        clearTimeout(deltaTimerRef.current);
        deltaTimerRef.current = null;
      }
    };
  }, [poll]);

  return (
    <div
      className="jp-header-stats"
      aria-label="Liveräkning från Platsbanken"
    >
      <div className="jp-header-stats__item">
        <span className="jp-header-stats__num">
          {formatLandingNumber(stats.activeCount)}
        </span>
        <span className="jp-header-stats__label">aktiva annonser</span>
      </div>
      <span
        className="jp-header-stats__sep"
        role="presentation"
        aria-hidden="true"
      />
      <div className="jp-header-stats__item">
        <span className="jp-header-stats__num">
          {formatLandingNumber(stats.newToday)}
        </span>
        <span className="jp-header-stats__label">nya idag</span>
        {deltaToday > 0 && (
          <span
            key={deltaKey}
            className="jp-header-stats__delta"
            aria-label={`${deltaToday} nya sedan senaste kontroll`}
          >
            +{formatLandingNumber(deltaToday)}
          </span>
        )}
      </div>
    </div>
  );
}
