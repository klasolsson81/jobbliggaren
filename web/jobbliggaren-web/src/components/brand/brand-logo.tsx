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

import { useTranslations } from "next-intl";
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
  /**
   * `inverse` renders the seal for a dark surface (the deep-green footer,
   * `--jp-accent-900` #0B2A1E): a white disc with dark-green ring/rows, the
   * gold mid-row kept, and a white check. The mark exposes its three fills as
   * props, so this is purely a fill-set swap — the geometry SSOT
   * (`BrandMarkSvg`) is untouched. Default `false` (the green-disc mark).
   */
  inverse?: boolean;
}

export function BrandLogo({
  variant = "full",
  markSize = 40,
  inverse = false,
}: BrandLogoProps) {
  const t = useTranslations("landing");
  // Inverse = white disc on the dark-green footer; default = green disc on a
  // light surface. Fills are theme-stable literals/tokens that do not shift
  // (accent-900 + gold are pinned), so the inverse seal stays legible whatever
  // the surrounding shell theme is.
  const markFills = inverse
    ? {
        primaryFill: "#FFFFFF",
        accentFill: "var(--jp-gold)",
        paperFill: "var(--jp-accent-900)",
      }
    : {
        primaryFill: "var(--jp-mark-primary)",
        accentFill: "var(--jp-mark-accent)",
        paperFill: "var(--jp-mark-paper)",
      };
  return (
    <>
      <BrandMarkSvg
        className="jp-brand__mark"
        width={markSize}
        height={markSize}
        {...markFills}
        ariaHidden={variant === "full" ? true : undefined}
        ariaLabel={variant === "mark" ? t("brand.markLabel") : undefined}
      />
      {variant === "full" ? (
        <span className="jp-brand__lockup">
          <span className="jp-brand__word" aria-hidden={true}>
            {t("brand.wordmark")}
          </span>
          <span className="jp-brand__tagline" aria-hidden={true}>
            {t("brand.tagline")}
          </span>
        </span>
      ) : null}
    </>
  );
}
