"use client";

// "use client": a controlled radio group with an event handler, and a clear button that moves focus.

import { useId, useRef } from "react";
import { Star } from "lucide-react";
import { useTranslations } from "next-intl";

const RATINGS = [1, 2, 3, 4, 5] as const;

/**
 * The page rating (#1979 PR3): five native radios in a fieldset whose legend is the question, so the
 * group, its name and arrow-key operation are the browser's own. The stars are outline icons only
 * (DESIGN.md §7); a chosen rating is told by more than colour: the stars up to it take a heavier
 * stroke, the chosen one sits on a bordered plate, and the rating is written out beside them.
 *
 * Choosing sends nothing and leaves focus where it is. Clearing moves focus to the first star, since
 * the clear button itself goes away.
 */
export function StarRating({
  value,
  onChange,
}: {
  value: number | null;
  onChange: (value: number | null) => void;
}) {
  const t = useTranslations("feedback");
  const name = useId();
  const firstRef = useRef<HTMLInputElement>(null);

  return (
    <fieldset className="jp-feedback__rating">
      <legend className="jp-feedback__question">{t("question")}</legend>
      <div className="jp-feedback__stars">
        {RATINGS.map((rating) => {
          const lit = value !== null && rating <= value;
          return (
            <label
              key={rating}
              className="jp-feedback__star"
              data-lit={lit || undefined}
              data-selected={value === rating || undefined}
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
              <Star size={24} strokeWidth={lit ? 2.75 : 2} aria-hidden="true" />
            </label>
          );
        })}
        {value !== null && (
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
