// F-Pre Punkt 6 — Jobbliggaren brand-mark (CTO 2026-05-24 Beslut 4 + Klas-val C4 2026-05-25).
// Ren RSC + inline SVG via BrandMarkSvg (CTO M1-triage 2026-05-25 Variant B).
// Ärver primary-fill från CSS-vars (.jp-brand color → currentColor) + accent från
// --jp-brand-accent. Funkar i båda RSC- och client-island-kontexter.

import { BrandMarkSvg } from "./brand-mark-svg";

type BrandLogoVariant = "full" | "mark";

export interface BrandLogoProps {
  /**
   * `full` (default) renderar mark + wordmark "Jobbliggaren".
   * `mark` renderar bara compass-stjärnan (för minimala kontexter).
   */
  variant?: BrandLogoVariant;
  /**
   * Mark-storlek i px. Wordmark skalas via .jp-brand__word-CSS i full-varianten.
   */
  markSize?: number;
}

export function BrandLogo({ variant = "full", markSize = 32 }: BrandLogoProps) {
  return (
    <>
      <BrandMarkSvg
        className="jp-brand__mark"
        width={markSize}
        height={markSize}
        primaryFill="currentColor"
        accentFill="var(--jp-brand-accent)"
        ariaHidden={variant === "full" ? true : undefined}
        ariaLabel={variant === "mark" ? "Jobbliggaren" : undefined}
      />
      {variant === "full" ? (
        <span className="jp-brand__word" aria-hidden={true}>
          Jobbliggaren
        </span>
      ) : null}
    </>
  );
}
