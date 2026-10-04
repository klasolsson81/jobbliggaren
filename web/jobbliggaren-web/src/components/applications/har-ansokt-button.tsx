"use client";

import { useId, useRef, useState, useTransition } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { ArrowRight, Circle, CircleCheck } from "lucide-react";
import { createApplicationFromJobAdAction } from "@/lib/actions/applications";
import { useFocusAfterCommit } from "@/lib/hooks/use-focus-after-commit";
import styles from "./har-ansokt-button.module.css";

interface HarAnsoktButtonProps {
  jobAdId: string;
  /**
   * Server-fetched initialApplied (`hasAppliedJobAd(id)`). When true the applied state shows from
   * the start, so reopening the modal after a mark needs no extra round-trip.
   */
  initialApplied: boolean;
}

/**
 * "Markera som ansökt" in the job-ad footer (ADR 0053 Amendment 2026-10-03, #1963).
 *
 * At rest it is a button in the info tone of the applied axis. Once applied, the button gives way to
 * a status that is not a control (#1863): an "Ansökt" badge and a "Visa ansökan" link. The link goes
 * to the application this button just created, whose id the action returns; an ad applied before
 * this mount links to the list, since one ad can carry several applications and the server only
 * says whether one exists. Leaving through the link soft-navigates out of the job modal.
 *
 * The mark is optimistic and rolls back on failure. The button removes itself when used, so focus
 * moves to the link after the commit and back to the button on a rollback (WCAG 2.4.3).
 */
export function HarAnsoktButton({ jobAdId, initialApplied }: HarAnsoktButtonProps) {
  const tUi = useTranslations("applications.ui");
  const [applied, setApplied] = useState(initialApplied);
  const [applicationId, setApplicationId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();
  const focusAfterCommit = useFocusAfterCommit();
  const buttonRef = useRef<HTMLButtonElement>(null);
  const linkRef = useRef<HTMLAnchorElement>(null);
  const badgeId = useId();

  function handleClick() {
    setError(null);
    setApplied(true); // Optimistic
    focusAfterCommit(() => linkRef.current);

    startTransition(async () => {
      const result = await createApplicationFromJobAdAction(jobAdId);
      if (result.success) {
        setApplicationId(result.applicationId);
        return;
      }
      // Rollback. Focus returns to the button only if it was still on the link, or had fallen out.
      const active = document.activeElement;
      if (active === linkRef.current || active === document.body || active === null) {
        focusAfterCommit(() => buttonRef.current);
      }
      setApplied(false);
      setError(result.error);
    });
  }

  if (applied) {
    return (
      <div className={styles.applied}>
        <span id={badgeId} className={styles.badge}>
          <CircleCheck size={14} aria-hidden="true" />
          {tUi("harAnsokt.applied")}
        </span>
        <Link
          ref={linkRef}
          href={applicationId === null ? "/ansokningar" : `/ansokningar/${applicationId}`}
          className={styles.link}
          aria-describedby={badgeId}
        >
          {tUi("harAnsokt.viewApplication")}
          <ArrowRight size={14} aria-hidden="true" />
        </Link>
      </div>
    );
  }

  return (
    <div className={styles.control}>
      <button
        ref={buttonRef}
        type="button"
        className="jp-btn jp-btn--info-soft"
        onClick={handleClick}
        style={{ opacity: isPending ? 0.7 : 1 }}
      >
        <Circle size={14} aria-hidden="true" />
        {tUi("harAnsokt.markAsApplied")}
      </button>
      {error && (
        <span role="alert" className="text-body-sm text-danger-700">
          {error}
        </span>
      )}
    </div>
  );
}
