"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { OVERVIEW_STALE_MS, type AwaitingObservation } from "@/lib/admin/overview";
import type { AdminOverviewSnapshot } from "@/lib/dto/admin-overview";

const SOURCES = ["accounts", "audit", "jobs", "backup"] as const;
type Health = "current" | "retained" | "stale" | "retainedStale" | "failed" | "loading" | "awaiting";

function health(observation: AwaitingObservation<unknown>, now: number): Health {
  if (observation.kind !== "loaded" && observation.kind !== "empty") return observation.kind;
  const stale = now - Date.parse(observation.sampledAt) > OVERVIEW_STALE_MS;
  return observation.refreshFailed
    ? stale ? "retainedStale" : "retained"
    : stale ? "stale" : "current";
}

export function AdminOverviewAnnouncements({ observations, now }: {
  readonly observations: AdminOverviewSnapshot;
  readonly now: number;
}) {
  const t = useTranslations("admin");
  const current = SOURCES.map((source) => health(observations[source], now));
  const [previous, setPrevious] = useState({ health: current, message: "" });

  if (current.some((state, index) => state !== previous.health[index])) {
    const message = SOURCES.flatMap((source, index) => {
      const state = health(observations[source], now);
      if (state === previous.health[index]) return [];
      const text = state === "failed" || state === "loading" || state === "awaiting" ? t(`regions.${state}`)
        : state === "retainedStale"
          ? `${t("overview.observation.refreshFailed")} ${t("overview.observation.stale")}`
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
