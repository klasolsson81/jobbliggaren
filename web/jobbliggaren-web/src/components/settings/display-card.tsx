"use client";

import { useTranslations } from "next-intl";
import { Segment, type SegmentOption } from "@/components/ui/segment";

type LanguageValue = "sv" | "en";

interface DisplayCardProps {
  language: LanguageValue;
  onLanguageChange: (next: LanguageValue) => void;
  isPending: boolean;
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
 */
export function DisplayCard({
  language,
  onLanguageChange,
  isPending,
}: DisplayCardProps) {
  const t = useTranslations("settings");
  // Språk-segmentets options. Båda språken är aktiva (next-intl wirad, ADR 0078);
  // val byter UI-språk direkt via `onLanguageChange` (cookie + refresh).
  const languageOptions: ReadonlyArray<SegmentOption<LanguageValue>> = [
    { value: "sv", label: t("display.languageSwedish") },
    { value: "en", label: t("display.languageEnglish") },
  ];
  return (
    <section className="jp-card">
      <h2 className="jp-card__title">{t("display.title")}</h2>

      <div className="jp-settings-field">
        <span className="jp-settings-field__label">
          {t("display.languageLabel")}
        </span>
        <Segment
          aria-label={t("display.languageLabel")}
          value={language}
          onChange={onLanguageChange}
          options={languageOptions}
          disabled={isPending}
        />
        <p className="jp-settings-field__hint">{t("display.languageHint")}</p>
      </div>
    </section>
  );
}
