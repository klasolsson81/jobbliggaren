"use client";

// "use client": event handlers, a pointer and focus preview, and a clear button that moves focus.

import { useId, useRef, useState, type PointerEvent } from "react";
import { Star } from "lucide-react";
import { useTranslations } from "next-intl";

const RATINGS = [1, 2, 3, 4, 5] as const;

/**
 * Which star the mouse points at or the keyboard stands on, and how far the stars are filled for it.
 * A touch has no hover: its emulated enter would leave a preview behind after the tap.
 */
function useStarPreview(value: number | null) {
  const [hovered, setHovered] = useState<number | null>(null);
  const [focused, setFocused] = useState<number | null>(null);
  const hover = (rating: number | null) => (event: PointerEvent) => {
    if (event.pointerType === "mouse") setHovered(rating);
  };
  return { shown: hovered ?? focused ?? value, hover, setFocused };
}

/** Filled up to the rating shown and outlined after it (DESIGN.md §7's exception), so shape tells the state. */
function StarGlyph({ filled }: { filled: boolean }) {
  return <Star size={32} strokeWidth={2} fill={filled ? "currentColor" : "none"} aria-hidden="true" />;
}

/**
 * The rating inside the feedback dialog (#1979 PR3): five native radios in a fieldset whose legend is the
 * question, so the group, its name and arrow-key operation are the browser's own. The rating is a form
 * value that Send carries; `clearable` adds it written out beside the stars and a clear button, and
 * clearing moves focus to the first star, since the clear button itself goes away.
 */
export function StarRating({
  question,
  value,
  onChange,
  clearable = true,
}: {
  question: string;
  value: number | null;
  onChange: (value: number | null) => void;
  clearable?: boolean;
}) {
  const t = useTranslations("feedback");
  const name = useId();
  const firstRef = useRef<HTMLInputElement>(null);
  const { shown, hover } = useStarPreview(value);

  return (
    <fieldset className="jp-feedback__rating">
      <legend className="jp-feedback__question">{question}</legend>
      <div className="jp-feedback__stars">
        <span className="jp-feedback__starset" onPointerLeave={hover(null)}>
          {RATINGS.map((rating) => (
            <label
              key={rating}
              className="jp-feedback__star"
              data-filled={(shown !== null && rating <= shown) || undefined}
              onPointerEnter={hover(rating)}
            >
              <input
                ref={rating === 1 ? firstRef : undefined}
                type="radio"
                name={name}
                value={rating}
                checked={value === rating}
                onChange={() => onChange(rating)}
                aria-label={t("rating.option", { rating })}
                className="sr-only"
              />
              <StarGlyph filled={shown !== null && rating <= shown} />
            </label>
          ))}
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

/**
 * The rating row's stars (#1979 PR3): five buttons in a fieldset whose legend is the question. A star is
 * an answer, not a value to be confirmed later, so pressing one — click, tap, Enter or Space — sends it,
 * also the one already filled. The stars are filled up to the one the mouse points at or focus stands on,
 * and up to `value` where a refused answer is kept. `sending` writes it beside the stars.
 */
export function StarButtons({
  question,
  value,
  onCommit,
  sending,
}: {
  question: string;
  value: number | null;
  onCommit: (value: number) => void;
  sending: boolean;
}) {
  const t = useTranslations("feedback");
  const { shown, hover, setFocused } = useStarPreview(value);

  return (
    <fieldset className="jp-feedback__rating">
      <legend className="jp-feedback__question">{question}</legend>
      <div className="jp-feedback__stars">
        <span className="jp-feedback__starset" onPointerLeave={hover(null)}>
          {RATINGS.map((rating) => (
            <button
              key={rating}
              type="button"
              className="jp-feedback__star"
              data-filled={(shown !== null && rating <= shown) || undefined}
              aria-label={t("rating.option", { rating })}
              onPointerEnter={hover(rating)}
              onFocus={() => setFocused(rating)}
              onBlur={() => setFocused(null)}
              onClick={() => onCommit(rating)}
            >
              <StarGlyph filled={shown !== null && rating <= shown} />
            </button>
          ))}
        </span>
        {/* The row's live region already says it; this is the same word for the eye. */}
        {sending && (
          <span className="jp-feedback__readout" aria-hidden="true">
            {t("sending")}
          </span>
        )}
      </div>
    </fieldset>
  );
}
