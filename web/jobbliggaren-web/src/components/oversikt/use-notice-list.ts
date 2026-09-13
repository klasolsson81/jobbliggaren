"use client";

import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type RefObject,
} from "react";
import type { NoticeKind, NoticeSource } from "./notice-types";
import { useDismissedNotices } from "./use-dismissed-notices";
import { useNoticePrefs } from "./use-notice-prefs";

/** The least a notice must carry for the list state to sort, filter and dismiss it. */
export interface ListableNotice {
  readonly id: string;
  readonly kind: NoticeKind;
  readonly source: NoticeSource;
  readonly type: string;
}

interface UseNoticeListOptions {
  /**
   * Which kinds sort first. Absent = construction order throughout, which is what the two cards
   * on `/oversikt` want: the orchestrator has already split notices by kind before they arrive.
   */
  readonly isAction?: (kind: NoticeKind) => boolean;
  /**
   * Where focus goes when the LAST read notice is restored and the read foot unmounts with it:
   * the gear button in `NoticeSection`, the card's own root (`tabIndex={-1}`) in the cards.
   * Without it focus falls to `<body>` and a keyboard or screen-reader user loses their place
   * (WCAG 2.4.3, design-reviewer Major, #726).
   */
  readonly fallbackRef: RefObject<HTMLElement | null>;
}

export interface NoticeListState<T extends ListableNotice> {
  readonly unread: T[];
  readonly read: T[];
  readonly showRead: boolean;
  toggleShowRead(): void;
  handleDismiss(id: string): void;
  handleRestore(id: string): void;
  /** Attach to the read foot's Visa/Dölj toggle — the stable sibling focus moves to after a dismiss. */
  readonly footToggleRef: RefObject<HTMLButtonElement | null>;
}

/**
 * The read/unread state of one notice list, lifted out of `NoticeSection` (ADR 0140) so the two
 * list cards on `/oversikt` share one implementation with the section the guest demo still
 * renders. Owns: preference filtering (a switched-off type is removed entirely and counts
 * nowhere), the dismissed/read split, the Visa/Dölj toggle, and the programmatic focus move
 * after a dismiss or restore unmounts the element that held focus.
 *
 * Focus is requested through a ref flag, not state: every focus-relevant action mutates the
 * dismiss store, so the effect keyed on `dismissed` is guaranteed to run after the re-render —
 * and clearing a ref inside an effect is lint-clean where a setState is not
 * (react-hooks/set-state-in-effect).
 */
export function useNoticeList<T extends ListableNotice>(
  notices: ReadonlyArray<T>,
  { isAction, fallbackRef }: UseNoticeListOptions,
): NoticeListState<T> {
  const { dismissed, dismiss, restore } = useDismissedNotices();
  const { isEnabled } = useNoticePrefs();

  const [showRead, setShowRead] = useState(false);
  const footToggleRef = useRef<HTMLButtonElement>(null);
  const pendingFocusRef = useRef<"foot" | "fallback" | null>(null);

  useEffect(() => {
    const target = pendingFocusRef.current;
    if (target === null) return;
    pendingFocusRef.current = null;
    if (target === "foot" && footToggleRef.current) {
      footToggleRef.current.focus();
    } else {
      fallbackRef.current?.focus();
    }
  }, [dismissed, fallbackRef]);

  // A switched-off type is filtered out entirely — it counts nowhere, not even in "N olästa".
  const enabled = useMemo(
    () => notices.filter((n) => isEnabled(n.source, n.type)),
    [notices, isEnabled],
  );

  // Action kinds first, then the rest; construction order within a bucket
  // (Array.prototype.sort is stable since ES2019). Absent `isAction`, construction order only.
  const order = useCallback(
    (list: T[]): T[] =>
      isAction
        ? [...list].sort((a, b) => (isAction(a.kind) ? 0 : 1) - (isAction(b.kind) ? 0 : 1))
        : list,
    [isAction],
  );
  const unread = useMemo(
    () => order(enabled.filter((n) => !dismissed.has(n.id))),
    [enabled, dismissed, order],
  );
  const read = useMemo(
    () => order(enabled.filter((n) => dismissed.has(n.id))),
    [enabled, dismissed, order],
  );

  // The read foot's toggle exists immediately after a dismiss (read ≥ 1).
  const handleDismiss = useCallback(
    (id: string) => {
      pendingFocusRef.current = "foot";
      dismiss(id);
    },
    [dismiss],
  );

  // Restoring the LAST read row unmounts the foot as well → the caller's fallback.
  const handleRestore = useCallback(
    (id: string) => {
      pendingFocusRef.current = read.length > 1 ? "foot" : "fallback";
      restore(id);
    },
    [restore, read.length],
  );

  const toggleShowRead = useCallback(() => setShowRead((v) => !v), []);

  return { unread, read, showRead, toggleShowRead, handleDismiss, handleRestore, footToggleRef };
}
