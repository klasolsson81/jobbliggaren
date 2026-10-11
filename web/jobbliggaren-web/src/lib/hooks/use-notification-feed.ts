"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { notificationFeedSchema, type NotificationFeed } from "@/lib/dto/notification-feed";

/** The bell's background cadence while the tab is visible (#1666; half of the 10-minute ad ingest). */
export const NOTIFICATION_FEED_POLL_MS = 5 * 60 * 1000;
/** The floor between two reads triggered by opening the panel or the tab becoming visible. */
export const NOTIFICATION_FEED_THROTTLE_MS = 30 * 1000;

export type NotificationFeedState =
  /** No answer yet. */
  | { readonly status: "loading" }
  /** No answer, and the last read failed. */
  | { readonly status: "failed" }
  /** The session is gone (401/403): the poll has stopped. */
  | { readonly status: "stopped" }
  /** An answer. `stale` when a later read failed, so the shown answer is from `fetchedAt`. */
  | { readonly status: "ready"; readonly feed: NotificationFeed; readonly fetchedAt: number; readonly stale: boolean };

/**
 * #1666 — the bell's feed: read on mount, every five minutes while the tab is visible, when the tab
 * becomes visible and when the panel opens (those two throttled to 30 s), through the BFF route
 * `/api/me/notification-feed`. The visibility-aware periodic read of AGENTS.md §4, hardened like
 * `admin-overview-live.tsx`: one read in flight, aborted when the tab hides or the hook unmounts.
 *
 * A failed read keeps the last answer and marks it stale — it never becomes "nothing unread"
 * (ADR 0120). 401 and 403 stop the poll. 429 waits out `Retry-After` before the next read.
 */
export function useNotificationFeed(): {
  readonly state: NotificationFeedState;
  /**
   * A read now if the throttle allows it. `force` skips the throttle and replaces a read already in
   * flight, whose answer may predate an acknowledgement; the promise settles when the read has.
   */
  readonly refresh: (force?: boolean) => Promise<void>;
  /** The new unread total when a read found more than the one before it; `null` otherwise. Never the first read. */
  readonly increasedTo: number | null;
} {
  const [state, setState] = useState<NotificationFeedState>({ status: "loading" });
  const [increasedTo, setIncreasedTo] = useState<number | null>(null);
  // The effect owns the poll; callers reach it through this ref.
  const refreshRef = useRef<(force: boolean) => Promise<void>>(() => Promise.resolve());

  useEffect(() => {
    let inFlight: AbortController | null = null;
    let stopped = false;
    let lastStartedAt = 0;
    let notBefore = 0;
    // undefined until the first answer, so the first total is never announced as an increase.
    let lastTotal: number | null | undefined;

    const fail = () =>
      setState((current) =>
        current.status === "ready" ? { ...current, stale: true } : current.status === "stopped" ? current : { status: "failed" },
      );

    async function read(force: boolean) {
      if (stopped || Date.now() < notBefore) return;
      if (inFlight !== null) {
        if (!force) return;
        inFlight.abort();
        inFlight = null;
      }
      const controller = new AbortController();
      inFlight = controller;
      lastStartedAt = Date.now();
      try {
        const response = await fetch("/api/me/notification-feed", { cache: "no-store", signal: controller.signal });
        if (controller.signal.aborted) return;
        if (response.status === 401 || response.status === 403) {
          stopped = true;
          setState({ status: "stopped" });
          return;
        }
        if (response.status === 429) {
          const seconds = Number(response.headers.get("Retry-After"));
          notBefore = Date.now() + (Number.isFinite(seconds) && seconds > 0 ? seconds : 60) * 1000;
          fail();
          return;
        }
        const parsed = response.ok ? notificationFeedSchema.safeParse(await response.json()) : null;
        if (controller.signal.aborted) return;
        if (!parsed?.success) {
          fail();
          return;
        }
        const total = unreadTotal(parsed.data);
        const previous = lastTotal;
        lastTotal = total;
        setState({ status: "ready", feed: parsed.data, fetchedAt: Date.now(), stale: false });
        setIncreasedTo(typeof previous === "number" && total !== null && total > previous ? total : null);
      } catch {
        if (!controller.signal.aborted) fail();
      } finally {
        if (inFlight === controller) inFlight = null;
      }
    }

    function refresh(force: boolean): Promise<void> {
      if (!force && Date.now() - lastStartedAt < NOTIFICATION_FEED_THROTTLE_MS) return Promise.resolve();
      return read(force);
    }
    refreshRef.current = refresh;

    function visibilityChanged() {
      if (document.hidden) {
        if (inFlight !== null) {
          inFlight.abort();
          inFlight = null;
          // An abandoned read must not hold the throttle shut when the tab comes back.
          lastStartedAt = 0;
        }
      } else {
        void refresh(false);
      }
    }

    const first = setTimeout(() => void read(false), 0);
    const timer = setInterval(() => {
      if (!document.hidden) void read(false);
    }, NOTIFICATION_FEED_POLL_MS);
    document.addEventListener("visibilitychange", visibilityChanged);
    return () => {
      stopped = true;
      clearTimeout(first);
      clearInterval(timer);
      document.removeEventListener("visibilitychange", visibilityChanged);
      inFlight?.abort();
      refreshRef.current = () => Promise.resolve();
    };
  }, []);

  const refresh = useCallback((force = false) => refreshRef.current(force), []);

  return { state, refresh, increasedTo };
}

/**
 * The bell's unread total: the sum of the sources' exact counts, or `null` when any source refused
 * to count (no number may be shown then, ADR 0120). No sources means nothing unread.
 */
export function unreadTotal(feed: NotificationFeed): number | null {
  let total = 0;
  for (const source of feed.sources) {
    if (source.unreadCount === null) return null;
    total += source.unreadCount;
  }
  return total;
}
