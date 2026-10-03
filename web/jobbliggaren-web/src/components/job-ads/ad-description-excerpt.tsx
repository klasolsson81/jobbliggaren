"use client";

import { useId, useLayoutEffect, useRef, useState } from "react";
import { ChevronDown, ChevronUp } from "lucide-react";
import styles from "./ad-description-excerpt.module.css";

/**
 * The ad text, clamped to an excerpt until the reader opens it (#1963). Whether the text overflows is
 * measured from the real heights, never guessed from its length.
 *
 * The server and the first client render assume an overflowing, collapsed text (fade and toggle
 * drawn): most ads are longer than the excerpt, so a hard load does not shift, and the measurement
 * removes the fade and the toggle when the text fits. The children are the server-rendered
 * `formatAdDescription` output; clipped text stays in the accessibility tree.
 */
export function AdDescriptionExcerpt({
  children,
  showFullLabel,
  showLessLabel,
}: {
  children: React.ReactNode;
  showFullLabel: string;
  showLessLabel: string;
}) {
  const id = useId();
  const clipRef = useRef<HTMLDivElement>(null);
  const textRef = useRef<HTMLDivElement>(null);
  const [overflowing, setOverflowing] = useState(true);
  const [expanded, setExpanded] = useState(false);

  useLayoutEffect(() => {
    if (expanded) return;
    const clip = clipRef.current;
    const text = textRef.current;
    if (clip === null || text === null) return;
    const measure = () => setOverflowing(text.getBoundingClientRect().height > clip.clientHeight + 1);
    measure();
    if (typeof ResizeObserver === "undefined") return;
    // The clip follows the viewport, the text follows its width and fonts: either can change the answer.
    const observer = new ResizeObserver(measure);
    observer.observe(clip);
    observer.observe(text);
    return () => observer.disconnect();
  }, [expanded]);

  return (
    <>
      <div
        ref={clipRef}
        id={id}
        className={styles.clip}
        data-collapsed={expanded ? undefined : ""}
        data-overflowing={overflowing ? "" : undefined}
      >
        <div ref={textRef} className="jp-modal__description">
          {children}
        </div>
      </div>
      {overflowing && (
        <button
          type="button"
          className={`jp-btn jp-btn--sm jp-btn--tonal ${styles.toggle}`}
          aria-expanded={expanded}
          aria-controls={id}
          onClick={() => setExpanded((was) => !was)}
        >
          {expanded ? showLessLabel : showFullLabel}
          {expanded ? (
            <ChevronUp size={16} aria-hidden="true" />
          ) : (
            <ChevronDown size={16} aria-hidden="true" />
          )}
        </button>
      )}
    </>
  );
}
