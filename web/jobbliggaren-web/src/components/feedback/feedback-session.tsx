"use client";

// "use client": the visit's answered pages are client state shared by every page's rating row and the
// footer's dialog, which live in different parts of the tree.

import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import type { FeedbackPromptState } from "@/lib/dto/feedback";
import type { FeedbackPageKey } from "@/lib/feedback/page-keys";

export type FeedbackSession = {
  /** Whether feedback is collected at all for this user right now. */
  readonly open: boolean;
  /** Answered before this visit, by the server's account, or during it. */
  readonly isAnswered: (page: FeedbackPageKey) => boolean;
  readonly markAnswered: (page: FeedbackPageKey) => void;
  /** The web build the page was rendered by; the route compares it before passing device context on. */
  readonly renderedVersion: string | null;
};

const FeedbackSessionContext = createContext<FeedbackSession | null>(null);

const NONE: ReadonlyArray<FeedbackPageKey> = [];

/**
 * The feedback state of one visit (#1979 PR3). The layout reads the server's state once per render and
 * hands it down; pages answered during the visit are kept in memory only, never in browser storage, so a
 * reload asks the server again. A new `state` from a re-render replaces the server half without
 * remounting anything below.
 */
export function FeedbackSessionProvider({
  state,
  renderedVersion,
  children,
}: {
  state: FeedbackPromptState;
  renderedVersion: string | null;
  children: ReactNode;
}) {
  const [answeredHere, setAnsweredHere] = useState<ReadonlySet<FeedbackPageKey>>(() => new Set());
  const open = state.kind === "open";
  const answeredBefore = state.kind === "open" ? state.answered : NONE;

  const markAnswered = useCallback((page: FeedbackPageKey) => {
    setAnsweredHere((previous) => (previous.has(page) ? previous : new Set(previous).add(page)));
  }, []);

  const value = useMemo<FeedbackSession>(
    () => ({
      open,
      isAnswered: (page) => answeredHere.has(page) || answeredBefore.includes(page),
      markAnswered,
      renderedVersion,
    }),
    [open, answeredHere, answeredBefore, markAnswered, renderedVersion],
  );

  return <FeedbackSessionContext.Provider value={value}>{children}</FeedbackSessionContext.Provider>;
}

/** The visit's feedback session, or null outside the signed-in layout. */
export function useFeedbackSession(): FeedbackSession | null {
  return useContext(FeedbackSessionContext);
}
