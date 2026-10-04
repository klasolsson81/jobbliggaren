"use client";

import { useLayoutEffect, useRef, useState } from "react";
import { ChevronDown, ChevronUp } from "lucide-react";
import styles from "./job-ad-match-section.module.css";

/**
 * Collapses the "Finns inte i din profil" chips past the first six (#1963). The chips are rendered
 * by the server inside `children`, with `data-overflow` on the ones that start hidden, so the server
 * and the first client render are the same markup; this island only owns the expanded flag and the
 * toggle. The toggle's name is its visible text (WCAG 2.5.3); the group's heading describes it.
 */
export function MatchSkillOverflow({
  children,
  listId,
  describedById,
  moreLabel,
  lessLabel,
}: {
  children: React.ReactNode;
  listId: string;
  describedById: string;
  moreLabel: string;
  lessLabel: string;
}) {
  const [expanded, setExpanded] = useState(false);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const wasExpanded = useRef(false);

  // Collapsing pulls the toggle up past the chips that disappear; keep it in view.
  useLayoutEffect(() => {
    if (wasExpanded.current && !expanded) {
      buttonRef.current?.scrollIntoView?.({ block: "nearest" });
    }
    wasExpanded.current = expanded;
  }, [expanded]);

  return (
    <div className={`${styles.flow} ${styles.overflow}`} data-expanded={expanded ? "" : undefined}>
      {children}
      <button
        ref={buttonRef}
        type="button"
        className={styles.more}
        aria-expanded={expanded}
        aria-controls={listId}
        aria-describedby={describedById}
        onClick={() => setExpanded((was) => !was)}
      >
        {expanded ? lessLabel : moreLabel}
        {expanded ? (
          <ChevronUp size={16} aria-hidden="true" />
        ) : (
          <ChevronDown size={16} aria-hidden="true" />
        )}
      </button>
    </div>
  );
}
