"use client";

import { useId, useLayoutEffect, useRef, useState } from "react";
import { ChevronDown, ChevronUp } from "lucide-react";
import styles from "./ad-description-excerpt.module.css";

/** The fade's height in ad-description-excerpt.module.css: less hidden text than this is shown in full. */
const FADE_PX = 96;

/**
 * The ad text, clamped to an excerpt until the reader opens it (#1963). Whether the text overflows is
 * measured from the real heights, never guessed from its length, against a probe that holds the
 * excerpt's height in every state.
 *
 * The server and the first client render assume an overflowing, collapsed text (fade and toggle
 * drawn): most ads are longer than the excerpt, so a hard load does not shift, and the measurement
 * removes the clamp, the fade and the toggle when the text fits. The children are the
 * server-rendered `formatAdDescription` output; clipped text stays in the accessibility tree.
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
  const probeRef = useRef<HTMLDivElement>(null);
  const textRef = useRef<HTMLDivElement>(null);
  const toggleRef = useRef<HTMLButtonElement>(null);
  const wasExpanded = useRef(false);
  const [overflowing, setOverflowing] = useState(true);
  const [expanded, setExpanded] = useState(false);

  useLayoutEffect(() => {
    if (expanded) return;
    const probe = probeRef.current;
    const text = textRef.current;
    if (probe === null || text === null) return;
    const measure = () =>
      setOverflowing(text.getBoundingClientRect().height > probe.getBoundingClientRect().height + FADE_PX);
    measure();
    if (typeof ResizeObserver === "undefined") return;
    // The excerpt follows the viewport, the text follows its width and fonts: either can change the answer.
    const observer = new ResizeObserver(measure);
    observer.observe(probe);
    observer.observe(text);
    return () => observer.disconnect();
  }, [expanded]);

  // Closing the text shortens the page above the toggle; keep the toggle where the reader is.
  useLayoutEffect(() => {
    if (wasExpanded.current && !expanded) toggleRef.current?.scrollIntoView?.({ block: "nearest" });
    wasExpanded.current = expanded;
  }, [expanded]);

  return (
    <>
      <div
        id={id}
        className={styles.clip}
        data-collapsed={overflowing && !expanded ? "" : undefined}
      >
        <div ref={probeRef} className={styles.probe} aria-hidden="true" />
        <div ref={textRef} className="jp-modal__description">
          {children}
        </div>
      </div>
      {overflowing && (
        <button
          ref={toggleRef}
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
