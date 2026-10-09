"use client";

// "use client": a controlled radio group with event handlers, a pointer preview, and a clear button that
// moves focus.

import { useId, useRef, useState, type PointerEvent } from "react";
import { Star } from "lucide-react";
import { useTranslations } from "next-intl";

const RATINGS = [1, 2, 3, 4, 5] as const;

/**
 * The rating (#1979 PR3): five native radios in a fieldset whose legend is the question, so the group,
 * its name and arrow-key operation are the browser's own. The stars up to the chosen rating, or
 * up to the one a mouse points at, are filled and the rest outlined (DESIGN.md §7's exception), so the
 * state is told by shape as well as colour.
 *
 * `clearable` adds the rating written out beside the stars and a clear button; clearing moves focus to
 * the first star, since the clear button itself goes away.
 *
 * With `onCommit` a star is an answer: a click, a tap, Space or Enter on a star commits it, also the one
 * already chosen. The arrow keys only move the choice and report it through `onChange`, because a
 * keyboard reaches the fourth star through the second and third.
 */
export function StarRating({
  question,
  value,
  onChange,
  onCommit,
  clearable = true,
}: {
  question: string;
  value: number | null;
  onChange: (value: number | null) => void;
  onCommit?: (value: number) => void;
  clearable?: boolean;
}) {
  const t = useTranslations("feedback");
  const name = useId();
  const firstRef = useRef<HTMLInputElement>(null);
  // The browser answers an arrow key with a click on the next radio, so the click alone cannot tell.
  const arrowRef = useRef(false);
  const [pointed, setPointed] = useState<number | null>(null);
  const shown = pointed ?? value;
  // A touch has no hover: its emulated enter would leave a preview behind after the tap.
  const point = (rating: number | null) => (event: PointerEvent) => {
    if (event.pointerType === "mouse") setPointed(rating);
  };

  return (
    <fieldset className="jp-feedback__rating">
      <legend className="jp-feedback__question">{question}</legend>
      <div className="jp-feedback__stars">
        <span
          className="jp-feedback__starset"
          onPointerLeave={point(null)}
          onPointerDown={() => {
            arrowRef.current = false;
          }}
          onKeyDown={(event) => {
            arrowRef.current = event.key.startsWith("Arrow");
          }}
        >
          {RATINGS.map((rating) => {
            const filled = shown !== null && rating <= shown;
            return (
              <label
                key={rating}
                className="jp-feedback__star"
                data-filled={filled || undefined}
                onPointerEnter={point(rating)}
              >
                <input
                  ref={rating === 1 ? firstRef : undefined}
                  type="radio"
                  name={name}
                  value={rating}
                  checked={value === rating}
                  onChange={() => {
                    if (onCommit === undefined || arrowRef.current) onChange(rating);
                  }}
                  onClick={() => {
                    if (onCommit !== undefined && !arrowRef.current) onCommit(rating);
                  }}
                  onKeyDown={(event) => {
                    if (onCommit === undefined || event.key !== "Enter") return;
                    event.preventDefault();
                    onCommit(rating);
                  }}
                  aria-label={t("rating.option", { rating })}
                  className="sr-only"
                />
                <Star size={32} strokeWidth={2} fill={filled ? "currentColor" : "none"} aria-hidden="true" />
              </label>
            );
          })}
        </span>
        {clearable && value !== null && (
          <>
            {/* The radio's own name already says it; this is the same words for the eye. */}
            <span className="jp-feedback__readout" aria-hidden="true">
              {t("rating.readout", { rating: value })}
            </span>
            <button
              type="button"
              className="jp-btn jp-btn--ghost jp-btn--sm"
              onClick={() => {
                onChange(null);
                firstRef.current?.focus();
              }}
            >
              {t("rating.clear")}
            </button>
          </>
        )}
      </div>
    </fieldset>
  );
}
