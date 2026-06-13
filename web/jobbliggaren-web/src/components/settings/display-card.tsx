"use client";

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

const LANGUAGE_OPTIONS: ReadonlyArray<SegmentOption<LanguageValue>> = [
  { value: "sv", label: "Svenska" },
  { value: "en", label: "English", disabled: true },
];

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
  return (
    <section className="jp-card">
      <h2 className="jp-card__title">Visning</h2>

      <div className="jp-settings-field">
        <span className="jp-settings-field__label">Tema</span>
        <Segment
          aria-label="Tema"
          value={theme}
          onChange={onThemeChange}
          options={themeOptions}
        />
        <p className="jp-settings-field__hint">
          Påverkar hela appen direkt. Sparas på din enhet.
        </p>
      </div>

      <div className="jp-settings-field">
        <span className="jp-settings-field__label">Språk</span>
        <Segment
          aria-label="Språk"
          value={language}
          onChange={onLanguageChange}
          options={LANGUAGE_OPTIONS}
          disabled={isPending}
        />
        <p className="jp-settings-field__hint">
          Engelska är ännu inte tillgängligt.
        </p>
      </div>
    </section>
  );
}
