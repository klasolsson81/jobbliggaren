"use client";

import { useState } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { formatStaleLimit } from "@/lib/admin/host-observation";
import { OVERVIEW_STALE_MS, type AwaitingObservation } from "@/lib/admin/overview";
import type { AdminOverviewSnapshot } from "@/lib/dto/admin-overview";

const SOURCES = ["accounts", "audit", "jobs", "backup", "host", "services"] as const;
type Source = (typeof SOURCES)[number];
type Health = "current" | "retained" | "stale" | "retainedStale" | "failed" | "loading" | "awaiting";

/**
 * The limit a source names for itself, with its card's sentence for an old sample: the Server and Tjänster cards are
 * sampled every 30 s and the API states their limit. Null for a source that shares the overview's five minutes.
 */
function ownLimit(source: Source, observations: AdminOverviewSnapshot):
  { readonly seconds: number; readonly sentence: "overview.server.stale" | "overview.services.stale" } | null {
  const { host, services } = observations;
  if (source === "host" && host.kind === "loaded") return { seconds: host.data.staleAfterSeconds, sentence: "overview.server.stale" };
  if (source === "services" && services.kind === "loaded") {
    return { seconds: services.data.staleAfterSeconds, sentence: "overview.services.stale" };
  }
  return null;
}

function staleLimit(source: Source, observations: AdminOverviewSnapshot): number {
  const own = ownLimit(source, observations);
  return own === null ? OVERVIEW_STALE_MS : own.seconds * 1000;
}

function health(observation: AwaitingObservation<unknown>, now: number, limit: number): Health {
  if (observation.kind !== "loaded" && observation.kind !== "empty") return observation.kind;
  const stale = now - Date.parse(observation.sampledAt) > limit;
  return observation.refreshFailed
    ? stale ? "retainedStale" : "retained"
    : stale ? "stale" : "current";
}

export function AdminOverviewAnnouncements({ observations, now }: {
  readonly observations: AdminOverviewSnapshot;
  readonly now: number;
}) {
  const t = useTranslations("admin");
  const format = useFormatter();
  const current = SOURCES.map((source) => health(observations[source], now, staleLimit(source, observations)));
  const [previous, setPrevious] = useState({ health: current, message: "" });

  if (current.some((state, index) => state !== previous.health[index])) {
    const message = SOURCES.flatMap((source, index) => {
      const state = health(observations[source], now, staleLimit(source, observations));
      if (state === previous.health[index]) return [];
      // A card that words its own limit (the API names it) is announced in the same sentence.
      const own = ownLimit(source, observations);
      const stale = own === null
        ? t("overview.observation.stale")
        : t(own.sentence, { limit: formatStaleLimit(format, own.seconds) });
      // A kept services observation whose newest sample is past its limit has had every row withdrawn, so no last value
      // is shown: the live region says what the card says, that the data could not be fetched.
      const failedAndOld = source === "services" ? t("regions.failed") : t("overview.observation.refreshFailed");
      const text = state === "failed" || state === "loading" || state === "awaiting" ? t(`regions.${state}`)
        : state === "retainedStale"
          ? `${failedAndOld} ${stale}`
          : state === "stale"
            ? stale
            : t(`overview.observation.${state === "retained" ? "refreshFailed" : state}`);
      return [t("overview.observation.announcement", {
        source: t(`overview.observation.sources.${source}`),
        state: text,
      })];
    }).join(" ");
    setPrevious({ health: current, message });
  }

  return (
    <div role="status" aria-live="polite" aria-atomic="true" className="sr-only">
      {previous.message}
    </div>
  );
}
