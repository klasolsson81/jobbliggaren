// Jobbliggaren brand mark — "Sigillet" (logo-översyn 2026-06-13, replaces the compass).
// Pure RSC + inline SVG via BrandMarkSvg. The mark is three-colour (green disc + gold +
// paper) and cannot inherit a single currentColor like the old compass — fills are set
// explicitly via mark tokens (--jp-mark-*). The wordmark inherits .jp-brand color (ink).
//
// Logo Fas 3 (ADR 0070 amendment 2026-06-13): the `full` variant is now the full header
// lockup — mark + a stacked [wordmark / tagline]. The tagline "Den svenska
// jobbansökningshanteraren" (= the OG/social tagline, Klas-STOPP A val H2 2026-05-25) sits
// under the wordmark like Platsbanken's "SWEDISH PUBLIC EMPLOYMENT SERVICE". Sentence-case
// treatment, consistent with opengraph-image.tsx (Klas choice, sv-val 2026-06-13).

import { BrandMarkSvg } from "./brand-mark-svg";

type BrandLogoVariant = "full" | "mark";

export interface BrandLogoProps {
  /**
   * `full` (default) renders the header lockup: mark + wordmark "Jobbliggaren"
   * + the tagline subline.
   * `mark` renders only the seal (for minimal contexts — no wordmark/tagline).
   */
  variant?: BrandLogoVariant;
  /**
   * Mark size in px. The wordmark + tagline scale via the .jp-brand__* CSS in
   * the full lockup. Default 40 (the enlarged header lockup).
   */
  markSize?: number;
}

export function BrandLogo({ variant = "full", markSize = 40 }: BrandLogoProps) {
  return (
    <>
      <BrandMarkSvg
        className="jp-brand__mark"
        width={markSize}
        height={markSize}
        primaryFill="var(--jp-mark-primary)"
        accentFill="var(--jp-mark-accent)"
        paperFill="var(--jp-mark-paper)"
        ariaHidden={variant === "full" ? true : undefined}
        ariaLabel={variant === "mark" ? "Jobbliggaren" : undefined}
      />
      {variant === "full" ? (
        <span className="jp-brand__lockup">
          <span className="jp-brand__word" aria-hidden={true}>
            Jobbliggaren
          </span>
          <span className="jp-brand__tagline" aria-hidden={true}>
            Den svenska jobbansökningshanteraren
          </span>
        </span>
      ) : null}
    </>
  );
}
