import "server-only";
import { cache } from "react";
import { env } from "@/lib/env";
import {
  landingStatsDtoSchema,
  type LandingStatsDto,
} from "@/lib/dto/landing";

/**
 * ADR 0064 — publik anonym landing-stats. Server-only fetch mot
 * `GET /api/v1/landing/stats`. Ingen auth-header (endpoint är publik).
 *
 * <p>
 * `React.cache` memoiserar inom samma request — landing-routen rendrar
 * `LandingTopbar` (förbrukar `activeCount`) på samma request som ev.
 * andra konsumenter (i framtida `/oversikt`-fas). Per-request-dedup
 * undviker dubbla nätverkstrampolinger.
 * </p>
 * <p>
 * `cache: "no-store"` — backend äger TTL via Worker-cron + Redis. FE-cache
 * skulle dölja backendens IsStale-signal och ackumulera multi-instans-drift
 * (per CTO-dom 2026-05-23 avvisning av Variant D Next.js fetch-cache).
 * </p>
 * <p>
 * Vid fetch-fail (nätverk, 5xx, 429, shape-mismatch) returneras `null` —
 * caller (`getLandingStats`-helper i landing-components) ansvarar för
 * civil UX-degradering (visa inga tal eller dölja sektion).
 * </p>
 */
export const fetchLandingStats = cache(
  async (): Promise<LandingStatsDto | null> => {
    try {
      const res = await fetch(`${env.BACKEND_URL}/api/v1/landing/stats`, {
        cache: "no-store",
      });
      if (!res.ok) return null;
      const raw: unknown = await res.json();
      const parsed = landingStatsDtoSchema.safeParse(raw);
      return parsed.success ? parsed.data : null;
    } catch {
      return null;
    }
  },
);
