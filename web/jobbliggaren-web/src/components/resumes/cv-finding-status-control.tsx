"use client";

// Client island: the only interactive part of a ledger row. `"use client"` is needed for the click
// handlers, useTransition (pending UI) and local error state. No client optimism: the server action
// writes and revalidates both the review and /cv, so the status and the stale hint come back as new
// props.

import { useEffect, useRef, useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import { Check } from "lucide-react";
import { StatusPill } from "@/components/ui/status-pill";
import {
  setFindingStatusAction,
  type FindingStatusValue,
} from "@/lib/actions/resumes";

/**
 * The user's decision on one finding of the canonical review, in the ledger's action column
 * (#2083). An open finding shows only the action: a row with no status already reads as open.
 *
 * §5 honesty: "Ignorera regeln (stilfråga)" renders ONLY when `isIgnorable` is true — the same set
 * the backend enforces (400 `FindingNotIgnorable` otherwise). Never an offer the server refuses.
 *
 * A status is always a word in a StatusPill, never colour alone (WCAG 1.4.1). An unknown value
 * (deploy skew; the zod schema keeps the set open on purpose) renders no pill: a pill would claim
 * a state we do not know.
 *
 * While the action runs, the pressed button is `aria-disabled` and the handler refuses, rather than
 * `disabled`: a disabled button that has focus drops it to `<body>` (DESIGN.md §6, #1391). When the
 * new status arrives, the button that was pressed has gone, so focus moves to the group's first
 * button if it was inside the group.
 */
export function CvFindingStatusControl({
  resumeId,
  criterionId,
  labelledBy,
  userStatus,
  userStatusStaleAt,
  isIgnorable,
}: {
  resumeId: string;
  criterionId: string;
  /** The id of the row's criterion name, which names this group. */
  labelledBy: string;
  userStatus: string | null;
  userStatusStaleAt: string | null;
  isIgnorable: boolean;
}) {
  const t = useTranslations("resumes.review.status");
  const [isPending, startTransition] = useTransition();
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState<FindingStatusValue | null>(null);
  const groupRef = useRef<HTMLDivElement>(null);
  const restoreFocus = useRef(false);

  useEffect(() => {
    if (!restoreFocus.current) return;
    restoreFocus.current = false;
    groupRef.current?.querySelector<HTMLButtonElement>("button")?.focus();
  }, [userStatus]);

  function submit(status: FindingStatusValue) {
    if (isPending) return;
    setError(null);
    setPending(status);
    restoreFocus.current =
      groupRef.current?.contains(document.activeElement) ?? false;
    startTransition(async () => {
      const result = await setFindingStatusAction(resumeId, criterionId, status);
      if (!result.success) {
        restoreFocus.current = false;
        setError(result.error);
      }
      setPending(null);
    });
  }

  const isResolved = userStatus === "Resolved";
  const isIgnored = userStatus === "Ignored";

  function label(status: FindingStatusValue, resting: string): string {
    return isPending && pending === status ? t("updating") : resting;
  }

  function busyProps(status: FindingStatusValue) {
    return {
      "aria-disabled": isPending || undefined,
      "aria-busy": (isPending && pending === status) || undefined,
    };
  }

  const revert = (
    <button
      type="button"
      className="jp-btn jp-btn--ghost jp-btn--sm"
      onClick={() => submit("Open")}
      {...busyProps("Open")}
    >
      {label("Open", t("revert"))}
    </button>
  );

  return (
    <div
      ref={groupRef}
      className="jp-findingstatus"
      role="group"
      aria-labelledby={labelledBy}
    >
      {isResolved || isIgnored ? (
        <>
          <div className="jp-findingstatus__line">
            {isResolved ? (
              <StatusPill tone="success">{t("resolvedLabel")}</StatusPill>
            ) : (
              <StatusPill tone="neutral">{t("ignoredLabel")}</StatusPill>
            )}
            {revert}
          </div>
          <p className="jp-findingstatus__hint">
            {isIgnored
              ? t("ignoredHint")
              : userStatusStaleAt !== null
                ? t("staleHint")
                : t("resolvedHint")}
          </p>
        </>
      ) : (
        <div className="jp-findingstatus__line">
          <button
            type="button"
            className="jp-btn jp-btn--secondary jp-btn--sm"
            onClick={() => submit("Resolved")}
            {...busyProps("Resolved")}
          >
            <Check size={16} aria-hidden="true" />
            {label("Resolved", t("markResolved"))}
          </button>
          {/* §5 honesty gate: only style criteria (isIgnorable) may be ignored. */}
          {isIgnorable && (
            <button
              type="button"
              className="jp-btn jp-btn--ghost jp-btn--sm"
              onClick={() => submit("Ignored")}
              {...busyProps("Ignored")}
            >
              {label("Ignored", t("ignoreRule"))}
            </button>
          )}
        </div>
      )}

      {error !== null && (
        <p className="jp-findingstatus__error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
