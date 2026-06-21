"use client";

import { useTranslations } from "next-intl";
import { Segment, type SegmentOption } from "@/components/ui/segment";

type Theme = "light" | "dark";
type LanguageValue = "sv" | "en";

interface DisplayCardProps {
  theme: Theme;
  onThemeChange: (next: Theme) => void;
  language: LanguageValue;
  onLanguageChange: (next: LanguageValue) => void;
  isPending: boolean;
  themeOptions: ReadonlyArray<SegmentOption<Theme>>;
}

/**
 * Visning-kort. Tema-segment via `useTheme()` (klient-only, persisterad i
 * localStorage). Språk-segment via `updateMyProfileAction` (direct-apply
 * per CTO 2026-05-20 Val 2B + Klas-direktiv "Visning är direct-apply").
 *
 * FAS-DEFERRAL: English-option är disabled (next-intl ej aktiverad ännu).
 * Hint under språk-segmentet förmedlar status.
 */
export function DisplayCard({
  theme,
  onThemeChange,
  language,
  onLanguageChange,
  isPending,
  themeOptions,
}: DisplayCardProps) {
  const t = useTranslations("settings");
  // Språk-segmentets options. English är `disabled` (next-intl ej aktiverad
  // ännu) — bara etiketterna är översatta, inte aktiverings-logiken (en senare
  // batch äger den live språk-växlaren).
  const languageOptions: ReadonlyArray<SegmentOption<LanguageValue>> = [
    { value: "sv", label: t("display.languageSwedish") },
    { value: "en", label: t("display.languageEnglish"), disabled: true },
  ];
  return (
    <section className="jp-card">
      <h2 className="jp-card__title">{t("display.title")}</h2>

      <div className="jp-settings-field">
        <span className="jp-settings-field__label">{t("display.themeLabel")}</span>
        <Segment
          aria-label={t("display.themeLabel")}
          value={theme}
          onChange={onThemeChange}
          options={themeOptions}
        />
        <p className="jp-settings-field__hint">{t("display.themeHint")}</p>
      </div>

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
