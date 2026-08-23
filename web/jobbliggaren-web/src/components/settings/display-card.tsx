"use client";

import { useEffect, useId, useRef } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { formatTime } from "@/lib/i18n/format";
import { Segment, type SegmentOption } from "@/components/ui/segment";

type LanguageValue = "sv" | "en";

interface DisplayCardProps {
  language: LanguageValue;
  onLanguageChange: (next: LanguageValue) => void;
  isPending: boolean;
  /** Message from a refused language save, or null. */
  error: string | null;
  /** When the language was last saved from this card, or null. */
  savedAt: Date | null;
}

/**
 * Visning-kort. Språk-segment är direct-apply (per CTO 2026-05-20 Val 2B +
 * Klas-direktiv "Visning är direct-apply"): `onLanguageChange` persisterar
 * preferensen via `updateMyProfileAction` OCH sätter `NEXT_LOCALE`-cookien +
 * `router.refresh()` (i `settings-form`) så UI:t byter språk direkt (ADR 0078).
 *
 * MVP (Klas 2026-06-24): tema-segmentet är BORTTAGET — appen har bara ETT
 * färgläge (light). Dark-mode-CSS + `theme-provider`/`ThemeToggle` behålls
 * DORMANT i koden; re-enable = återställ tema-segmentet här + flagga
 * `DARK_MODE_ENABLED` i `theme-provider.tsx`.
 *
 * #1391: the outcome of the language save arrives as props rather than living here,
 * because the write itself is orchestrated by `SettingsForm` (it also flips the locale
 * cookie and refreshes). A card that owns its write owns its outcome state; this card owns
 * the control, which is what decides where the message goes.
 */
export function DisplayCard({
  language,
  onLanguageChange,
  isPending,
  error,
  savedAt,
}: DisplayCardProps) {
  const t = useTranslations("settings");
  const format = useFormatter();
  const hintId = useId();
  const errorId = useId();
  const cardRef = useRef<HTMLElement>(null);

  // Focus the control the message belongs to, so a keyboard or screen-reader user lands on it
  // instead of only hearing that something is wrong. The segment is `disabled` while the save is
  // pending, which drops focus to <body>, and `Segment`'s own restore effect is gated on the
  // group already holding focus — so nothing brings it back and `aria-describedby` is never read.
  // Same remedy `personal-info-card.tsx` uses for its input.
  //
  // `isPending` is in the guard, not just the deps: the error renders while the buttons are still
  // disabled, and `focus()` on a disabled element does nothing. Traced in Chromium at 30ms
  // intervals — alert at 120ms with the button still disabled, enabled at 150ms.
  // Queried off the card element rather than a wrapper: a new node in `.jp-settings-field` would
  // change what the flex column lays out.
  useEffect(() => {
    if (!error || isPending) return;
    cardRef.current
      ?.querySelector<HTMLButtonElement>('[role="radiogroup"] button[aria-checked="true"]')
      ?.focus();
  }, [error, isPending]);
  // Språk-segmentets options. Båda språken är aktiva (next-intl wirad, ADR 0078);
  // val byter UI-språk direkt via `onLanguageChange` (cookie + refresh).
  const languageOptions: ReadonlyArray<SegmentOption<LanguageValue>> = [
    { value: "sv", label: t("display.languageSwedish") },
    { value: "en", label: t("display.languageEnglish") },
  ];
  return (
    <section ref={cardRef} className="jp-card">
      <h2 className="jp-card__title">{t("display.title")}</h2>

      <div className="jp-settings-field">
        <span className="jp-settings-field__label">
          {t("display.languageLabel")}
        </span>
        <Segment
          aria-label={t("display.languageLabel")}
          aria-describedby={error ? `${hintId} ${errorId}` : hintId}
          value={language}
          onChange={onLanguageChange}
          options={languageOptions}
          disabled={isPending}
        />
        <p id={hintId} className="jp-settings-field__hint">
          {t("display.languageHint")}
        </p>
      </div>

      {/* Mutually exclusive live regions, the shape `background-match-card.tsx` ships:
          a failure is an assertive alert, otherwise a polite receipt. */}
      <div className="mt-4">
        {error ? (
          <p id={errorId} role="alert" className="text-body-sm text-danger-600">
            {error}
          </p>
        ) : (
          <p
            role="status"
            aria-live="polite"
            className="text-body-sm text-text-secondary"
          >
            {savedAt
              ? t("display.savedAt", { time: formatTime(format, savedAt) })
              : ""}
          </p>
        )}
      </div>
    </section>
  );
}
