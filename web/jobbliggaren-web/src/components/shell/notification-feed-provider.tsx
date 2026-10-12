"use client";

import { createContext, useCallback, useContext, useMemo, type ReactNode } from "react";
import { useNotificationFeed, type NotificationFeedApi } from "@/lib/hooks/use-notification-feed";

const NotificationFeedContext = createContext<NotificationFeedApi | null>(null);

/**
 * #2130 — owns the bell's one feed for the whole shell, so the bell in the header and a page in `main` (the
 * notification settings) talk to the same poll: a saved setting asks the bell to read again at once instead of
 * waiting for its 30 s throttle, and no second hook runs a second poll. Provided by `AppShell`, which both
 * `(app)` and `(admin)` render. A plain React context: no state library (AGENTS.md §4).
 */
export function NotificationFeedProvider({ children }: { children: ReactNode }) {
  const { state, refresh, increasedTo, pendingRead, markRead, pendingDismiss, dismiss } = useNotificationFeed();
  const value = useMemo<NotificationFeedApi>(
    () => ({ state, refresh, increasedTo, pendingRead, markRead, pendingDismiss, dismiss }),
    [state, refresh, increasedTo, pendingRead, markRead, pendingDismiss, dismiss],
  );
  return <NotificationFeedContext value={value}>{children}</NotificationFeedContext>;
}

/** The shell's feed. Only the bell reads it; it throws outside a provider so a missing one is never silent. */
export function useNotificationFeedContext(): NotificationFeedApi {
  const value = useContext(NotificationFeedContext);
  if (value === null) throw new Error("useNotificationFeedContext needs a NotificationFeedProvider (AppShell renders one).");
  return value;
}

/**
 * Asks the bell to read again now (a forced read, past the throttle). Outside a provider it does nothing, so a
 * page that renders without the shell (a test, a standalone surface) never fails for want of a bell.
 */
export function useRefreshNotificationFeed(): () => Promise<void> {
  // The hook's `refresh` is stable, so the returned function is too and a caller may list it as a dependency.
  const refresh = useContext(NotificationFeedContext)?.refresh;
  return useCallback(() => (refresh ? refresh(true) : Promise.resolve()), [refresh]);
}
