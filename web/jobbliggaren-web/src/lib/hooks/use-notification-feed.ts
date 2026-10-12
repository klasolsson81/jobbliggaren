"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { dismissNotificationAction, markNotificationReadAction } from "@/lib/actions/notification-feed";
import {
  notificationFeedSchema,
  type NotificationFeed,
  type NotificationFeedSourceKind,
} from "@/lib/dto/notification-feed";

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

/** What the bell and the settings page share (#2130): the answer, the ways to refresh it, and the read marks. */
export interface NotificationFeedApi {
  readonly state: NotificationFeedState;
  /**
   * A read now if the throttle allows it. `force` skips the throttle and replaces a read already in
   * flight, whose answer may predate an acknowledgement; the promise settles when the read has.
   */
  readonly refresh: (force?: boolean) => Promise<void>;
  /** The new unread total when a read found more than the one before it; `null` otherwise. Never the first read. */
  readonly increasedTo: number | null;
  /**
   * The rows (`readKey`) the user clicked whose mark is still on its way. They render as read at once, and
   * the count follows only when the mark is confirmed, so it is never a number the server has not agreed to.
   */
  readonly pendingRead: ReadonlySet<string>;
  /**
   * #2130 — marks ONE notification read. Fired without waiting: the click that calls this also opens the ad,
   * often as the intercepted job modal, and nothing here may hold that up. A confirmed mark patches the answer
   * in hand (the row reads as read, the source's count falls by one, exactly the token's bound) and forces no
   * read; a refused mark simply puts the dot back, and the row is unread at the next read.
   */
  readonly markRead: (kind: NotificationFeedSourceKind, jobAdId: string) => void;
  /**
   * The rows (`readKey`) the user took out with the X whose dismissal is still on its way. They are not shown at once,
   * and the count follows only when the dismissal is confirmed (the same rule as `pendingRead`).
   */
  readonly pendingDismiss: ReadonlySet<string>;
  /**
   * #2151 — takes ONE notification out of the bell. Fired without waiting, like `markRead`; the promise resolves `true`
   * when the server confirmed and `false` when it refused or the call failed, in which case the row is back (it was never
   * deleted) and the caller says so. A confirmed dismissal removes the row from the answer in hand and lowers its
   * source's count by one if the row was unread; it forces no read.
   */
  readonly dismiss: (kind: NotificationFeedSourceKind, jobAdId: string) => Promise<boolean>;
}

/** The identity of a notification row: one per (source, ad), the same key the list uses. */
export function readKey(kind: NotificationFeedSourceKind, jobAdId: string): string {
  return `${kind}:${jobAdId}`;
}

/**
 * The answer with one row marked read, as the server will answer once the mark has been written: the row is
 * read and its source counts one less. A row that is not in the list, or is already read, changes nothing, so
 * the patch is safe to apply twice and never lowers a count it did not raise.
 */
export function withRowRead(feed: NotificationFeed, kind: NotificationFeedSourceKind, jobAdId: string): NotificationFeed {
  if (!feed.items.some((item) => item.kind === kind && item.jobAdId === jobAdId && item.isUnread)) return feed;
  return {
    items: feed.items.map((item) =>
      item.kind === kind && item.jobAdId === jobAdId ? { ...item, isUnread: false } : item,
    ),
    sources: feed.sources.map((source) =>
      source.kind === kind && source.unreadCount !== null && source.unreadCount > 0
        ? { ...source, unreadCount: source.unreadCount - 1 }
        : source,
    ),
  };
}

/**
 * The answer with one row taken out (#2151), as the server will answer once the dismissal has been written: the row is
 * gone, and its source counts one less if the row was unread (a dismissed row is a read row). A row that is not in the
 * list changes nothing, so the patch is safe to apply twice and never lowers a count it did not raise.
 */
export function withRowDismissed(feed: NotificationFeed, kind: NotificationFeedSourceKind, jobAdId: string): NotificationFeed {
  const row = feed.items.find((item) => item.kind === kind && item.jobAdId === jobAdId);
  if (row === undefined) return feed;
  return {
    items: feed.items.filter((item) => item !== row),
    sources: row.isUnread
      ? feed.sources.map((source) =>
          source.kind === kind && source.unreadCount !== null && source.unreadCount > 0
            ? { ...source, unreadCount: source.unreadCount - 1 }
            : source,
        )
      : feed.sources,
  };
}

/**
 * #1666 — the bell's feed: read on mount, every five minutes while the tab is visible, when the tab
 * becomes visible and when the panel opens (those two throttled to 30 s), through the BFF route
 * `/api/me/notification-feed`. The visibility-aware periodic read of AGENTS.md §4, hardened like
 * `admin-overview-live.tsx`: one read in flight, aborted when the tab hides or the hook unmounts.
 *
 * A failed read keeps the last answer and marks it stale — it never becomes "nothing unread"
 * (ADR 0120). 401 and 403 stop the poll. 429 waits out `Retry-After` before the next read.
 *
 * #2130 — a read that STARTED before a confirmed mark may have been answered before the write landed, so a
 * stale `isUnread: true` would put the dot back. Such an answer is patched with the marks confirmed since it
 * started; a read that started after a mark needs no patch and retires it. The marks live in memory only.
 */
export function useNotificationFeed(): NotificationFeedApi {
  const [state, setState] = useState<NotificationFeedState>({ status: "loading" });
  const [increasedTo, setIncreasedTo] = useState<number | null>(null);
  const [pendingRead, setPendingRead] = useState<ReadonlySet<string>>(() => new Set());
  const [pendingDismiss, setPendingDismiss] = useState<ReadonlySet<string>>(() => new Set());
  // The effect owns the poll; callers reach it through this ref.
  const refreshRef = useRef<(force: boolean) => Promise<void>>(() => Promise.resolve());
  // `readKey` -> when the server confirmed the mark or the dismissal. Kept until a read that started after it has
  // landed. A dismissal outranks a mark: such an answer must not bring the row back, only (at most) read.
  const confirmedRef = useRef<Map<string, { readonly at: number; readonly dismissed: boolean }>>(new Map());
  // The total of the answer on screen, undefined until the first answer, so the first total is never announced
  // as an increase. Synced after each render, so a confirmed mark lowers it too and a later notification is
  // announced against the number the user actually saw.
  const lastTotalRef = useRef<number | null | undefined>(undefined);
  useEffect(() => {
    if (state.status === "ready") lastTotalRef.current = unreadTotal(state.feed);
  }, [state]);

  useEffect(() => {
    let inFlight: AbortController | null = null;
    let stopped = false;
    let lastStartedAt = 0;
    let notBefore = 0;

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
      const startedAt = Date.now();
      lastStartedAt = startedAt;
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
        let feed = parsed.data;
        for (const [key, { at: confirmedAt, dismissed }] of confirmedRef.current) {
          // `>=`: a mark confirmed in the very millisecond a read started may not be in that read's answer, and
          // patching an answer that already says read changes nothing, so a tie is patched, never trusted.
          if (confirmedAt >= startedAt) {
            const separator = key.indexOf(":");
            const kind = key.slice(0, separator) as NotificationFeedSourceKind;
            const jobAdId = key.slice(separator + 1);
            feed = dismissed ? withRowDismissed(feed, kind, jobAdId) : withRowRead(feed, kind, jobAdId);
          } else {
            confirmedRef.current.delete(key);
          }
        }
        const total = unreadTotal(feed);
        const previous = lastTotalRef.current;
        setState({ status: "ready", feed, fetchedAt: Date.now(), stale: false });
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

  const markRead = useCallback((kind: NotificationFeedSourceKind, jobAdId: string) => {
    const key = readKey(kind, jobAdId);
    const settle = () =>
      setPendingRead((current) => {
        if (!current.has(key)) return current;
        const next = new Set(current);
        next.delete(key);
        return next;
      });
    setPendingRead((current) => (current.has(key) ? current : new Set(current).add(key)));

    // Not awaited by the caller, and a rejection (a stale build, a dropped connection) is a refused mark: the
    // dot comes back and the next read tells the truth. Confirming and settling happen in the same tick, so the
    // row never flashes between "pending" and "patched".
    markNotificationReadAction({ kind, jobAdId }).then(
      (result) => {
        if (result.success) {
          // A dismissal confirmed earlier outranks this mark (a click and an X can overlap).
          if (confirmedRef.current.get(key)?.dismissed !== true) confirmedRef.current.set(key, { at: Date.now(), dismissed: false });
          setState((current) => {
            if (current.status !== "ready") return current;
            const feed = withRowRead(current.feed, kind, jobAdId);
            return feed === current.feed ? current : { ...current, feed };
          });
        }
        settle();
      },
      settle,
    );
  }, []);

  const dismiss = useCallback(async (kind: NotificationFeedSourceKind, jobAdId: string): Promise<boolean> => {
    const key = readKey(kind, jobAdId);
    const settle = () =>
      setPendingDismiss((current) => {
        if (!current.has(key)) return current;
        const next = new Set(current);
        next.delete(key);
        return next;
      });
    setPendingDismiss((current) => (current.has(key) ? current : new Set(current).add(key)));

    // A rejection (a stale build, a dropped connection) is a refused dismissal: the row comes back, because it was never
    // deleted, and the caller tells the user. Confirming and settling happen in the same tick, so the row never flashes
    // back between "pending" and "patched".
    try {
      const result = await dismissNotificationAction({ kind, jobAdId });
      if (result.success) {
        confirmedRef.current.set(key, { at: Date.now(), dismissed: true });
        setState((current) => {
          if (current.status !== "ready") return current;
          const feed = withRowDismissed(current.feed, kind, jobAdId);
          return feed === current.feed ? current : { ...current, feed };
        });
      }
      settle();
      return result.success;
    } catch {
      settle();
      return false;
    }
  }, []);

  return { state, refresh, increasedTo, pendingRead, markRead, pendingDismiss, dismiss };
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
