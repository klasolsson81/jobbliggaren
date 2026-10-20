"use client";

import { useState } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { allowanceFailureGroup } from "@/lib/admin/email-allowance";
import { emailReadLine, isAlertingRead, type EmailReadLine } from "@/lib/admin/email-delivery";
import { formatStaleLimit } from "@/lib/admin/host-observation";
import { OVERVIEW_STALE_MS, type AwaitingObservation } from "@/lib/admin/overview";
import type { AdminOverviewSnapshot } from "@/lib/dto/admin-overview";

const SOURCES = ["accounts", "audit", "jobs", "backup", "host", "services", "email", "delivery"] as const;
type Source = (typeof SOURCES)[number];
/**
 * `provider.*`: an e-post source answered, and its provider did not, or could not be asked or judged; the card says
 * why, and so does this region, in the same words (`admin.email.allowance`).
 */
type Health =
  | "current" | "retained" | "stale" | "retainedStale" | "failed" | "loading" | "awaiting"
  | `provider.${EmailReadLine}`;

/**
 * The limit a source names for itself, with its card's sentence for an old sample: the Server and Tjänster cards are
 * sampled every 30 s and the API states their limit. Null for a source that shares the overview's five minutes.
 */
function ownLimit(source: Source, observations: AdminOverviewSnapshot):
  { readonly seconds: number; readonly sentence: "server.stale" | "services.stale" } | null {
  const { host, services } = observations;
  if (source === "host" && host.kind === "loaded") return { seconds: host.data.staleAfterSeconds, sentence: "server.stale" };
  if (source === "services" && services.kind === "loaded") {
    return { seconds: services.data.staleAfterSeconds, sentence: "services.stale" };
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

function providerLine(state: Health): EmailReadLine | null {
  switch (state) {
    case "provider.notConfigured": return "notConfigured";
    case "provider.notObservable": return "notObservable";
    case "provider.readKeyMissing": return "readKeyMissing";
    case "provider.failure.noAnswer": return "failure.noAnswer";
    case "provider.failure.rateLimited": return "failure.rateLimited";
    case "provider.failure.keyRefused": return "failure.keyRefused";
    case "provider.failure.readRefused": return "failure.readRefused";
    case "provider.failure.malformed": return "failure.malformed";
    default: return null;
  }
}

/** What a current e-post source's provider read says, when it is a state the reader must act on or cannot judge. */
function providerHealth(source: Source, observations: AdminOverviewSnapshot): Health | null {
  const { email, delivery } = observations;
  if (source === "email" && email.kind === "loaded") {
    const usage = email.data.usage;
    if (usage.status === "Failed") return `provider.failure.${allowanceFailureGroup(usage.reason)}`;
    if (usage.status === "ReadKeyMissing") return "provider.readKeyMissing";
  }
  if (source === "delivery" && delivery.kind === "loaded" && isAlertingRead(delivery.data)) {
    const line = emailReadLine(delivery.data);
    if (line !== null) return `provider.${line}`;
  }
  return null;
}

function sourceHealth(source: Source, observations: AdminOverviewSnapshot, now: number): Health {
  const state = health(observations[source], now, staleLimit(source, observations));
  return state === "current" ? providerHealth(source, observations) ?? state : state;
}

export function AdminOverviewAnnouncements({ observations, now }: {
  readonly observations: AdminOverviewSnapshot;
  readonly now: number;
}) {
  const t = useTranslations("admin.overview");
  const regions = useTranslations("admin.regions");
  const lines = useTranslations("admin.email.allowance");
  const format = useFormatter();
  const current = SOURCES.map((source) => sourceHealth(source, observations, now));
  const [previous, setPrevious] = useState({ health: current, message: "" });

  if (current.some((state, index) => state !== previous.health[index])) {
    const message = SOURCES.flatMap((source, index) => {
      const state = current[index] ?? "current";
      if (state === previous.health[index]) return [];
      // A card that words its own limit (the API names it) is announced in the same sentence.
      const own = ownLimit(source, observations);
      const stale = own === null
        ? t("observation.stale")
        : t(own.sentence, { limit: formatStaleLimit(format, own.seconds) });
      // A kept services observation whose newest sample is past its limit has had every row withdrawn, so no last value
      // is shown: the live region says what the card says, that the data could not be fetched.
      const failedAndOld = source === "services" ? regions("failed") : t("observation.refreshFailed");
      const provider = providerLine(state);
      const text = provider !== null ? lines(provider)
        : state === "failed" || state === "loading" || state === "awaiting" ? regions(state)
        : state === "retainedStale"
          ? `${failedAndOld} ${stale}`
          : state === "stale"
            ? stale
            : t(`observation.${state === "current" ? "current" : "refreshFailed"}`);
      return [t("observation.announcement", {
        source: t(`observation.sources.${source}`),
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
