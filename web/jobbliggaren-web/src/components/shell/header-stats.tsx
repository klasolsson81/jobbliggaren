"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { landingStatsDtoSchema, type LandingStatsDto } from "@/lib/dto/landing";
import { formatNumber } from "@/lib/i18n/format";

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
 *
 * **Omätta tal renderas som en en-dash (–), aldrig som en siffra (CTO-bind 2026-07-13, A′).** Tidigare gav en kall
 * cache ett hårdkodat golv (40 000) som såg ut som ett mätvärde. Nu är en omätt count `null` och raden
 * visar en en-dash (–) tills en riktig siffra finns. Att BEHÅLLA ett senast mätt värde vid poll-fel är
 * däremot fortsatt rätt: inaktualitet i ett mätt värde är OK, fabrikation är det inte.
 */
const POLL_INTERVAL_MS = 10 * 60 * 1000;
const DELTA_VISIBLE_MS = 8_000;

export function HeaderStats({
  initialStats,
}: {
  initialStats: LandingStatsDto;
}) {
  const t = useTranslations("common");
  const format = useFormatter();
  // Vad som visas i stället för en siffra vi inte mätt: ett streck, aldrig en nolla och aldrig ett
  // golv. Samma affordans (och samma en-dash) som /oversikt redan använder vid endpoint-fel
  // (design-reviewer M2). Copy bor i i18n, inte som literal i koden.
  const unmeasured = t("header.valueDash");
  const [stats, setStats] = useState<LandingStatsDto>(initialStats);
  const [deltaToday, setDeltaToday] = useState<number>(0);
  // Track previous newToday för delta-jämförelse. Initieras till samma
  // värde som initialStats så första polling-svar inte visar falsk delta.
  const previousNewToday = useRef<number | null>(initialStats.newToday);
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
      // En delta kräver TVÅ mätta tal. Är endera omätt finns ingen ökning att påstå — och ett omätt
      // värde får aldrig läsas som 0 (det vore fabrikation via aritmetik).
      const diff =
        next.newToday !== null && previousNewToday.current !== null
          ? next.newToday - previousNewToday.current
          : 0;
      // Mutera ref + state först efter alla await:s passerat (code-reviewer
      // M2 — undvik strict-mode-doublet-fire-ratchet). setState i React 19
      // är safe-on-unmount; ingen extra cancelled-flag behövs här.
      previousNewToday.current = next.newToday;
      setStats(next);

      // Blir talet OMÄTT måste en kvarhängande delta-pill bort: "+2" bredvid ett streck påstår en
      // ökning i en storhet vi just sagt oss inte känna. (Deltat var mätt, men dess granne är det
      // inte längre.)
      if (next.newToday === null) {
        setDeltaToday(0);
        if (deltaTimerRef.current !== null) {
          clearTimeout(deltaTimerRef.current);
          deltaTimerRef.current = null;
        }
      }

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
      role="group"
      aria-label={t("header.statsAriaLabel")}
    >
      <div className="jp-header-stats__item">
        <span className="jp-header-stats__num">
          {stats.activeCount === null
            ? unmeasured
            : formatNumber(format, stats.activeCount)}
        </span>
        <span className="jp-header-stats__label">
          {/* Ingen `?? 0` här. Att koerca ett omätt värde till 0 för ICU:s pluralval är ofarligt
              PRECIS SÅ LÄNGE strängen inte interpolerar `#` — och syskonnyckeln deltaAriaLabel gör
              det redan. Ett tangenttryck bort skulle raden tyst säga "0 aktiva annonser" bredvid ett
              streck. Hela poängen med den här ändringen är att invarianten ska bäras av typen, inte
              av att nästa person är vaken: därför en egen count-fri nyckel när talet är omätt. */}
          {stats.activeCount === null
            ? t("header.activeCountUnmeasured")
            : t("header.activeCount", { count: stats.activeCount })}
        </span>
      </div>
      <span
        className="jp-header-stats__sep"
        role="presentation"
        aria-hidden="true"
      />
      <div className="jp-header-stats__item">
        <span className="jp-header-stats__num">
          {stats.newToday === null
            ? unmeasured
            : formatNumber(format, stats.newToday)}
        </span>
        <span className="jp-header-stats__label">{t("header.newToday")}</span>
        {deltaToday > 0 && (
          <span
            key={deltaKey}
            className="jp-header-stats__delta"
            aria-hidden="true"
          >
            +{formatNumber(format, deltaToday)}
          </span>
        )}
        {/*
         * Live region for the delta. The visual pill above is `aria-hidden`:
         * an `aria-label` on that role=generic <span> is dropped by ARIA-in-HTML
         * (name-from-author prohibited) — the #624 bug. Because the delta
         * appears *dynamically* on a poll that finds new jobs it must be
         * *announced*, not merely named, so a screen-reader-only role="status"
         * live region carries the text. It is always mounted (a screen reader
         * only announces changes to a region that already exists) and holds the
         * sentence only while the pill is visible; polite = routine update.
         */}
        <span
          className="sr-only"
          role="status"
          aria-live="polite"
          aria-atomic="true"
        >
          {deltaToday > 0
            ? t("header.deltaAriaLabel", { count: deltaToday })
            : ""}
        </span>
      </div>
    </div>
  );
}
