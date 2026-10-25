// SSOT för Jobbliggaren brand-mark-geometri — "Sigillet": fyllt register-sigill
// (slät grön skiva + tunn inre ring + tre liggar-rader, mittersta guldmarkerad med
// en bock = loggad post). Ersätter den tidigare 4-uddiga kompassen (logo-översyn
// 2026-06-13; ADR 0068-amendment — kompassens navy+guldprick utgår).
// `app/icon.svg` är file-convention-mirror (Next.js auto-favicon) —
// synka manuellt vid geometri-justering och kör sedan scripts/generate-brand-icons.mjs
// (favicon.ico + public/icons/*.png genereras ur icon.svg).
//
// Tre färgroller (utökar tidigare 2-fill-kontrakt → 3):
//   primaryFill  grön skiva + bock   (--jp-mark-primary → --jp-accent-800 #15603F)
//   accentFill   guld mittrad        (--jp-mark-accent  → --jp-gold #E8C77B, ADR 0068)
//   paperFill    inre ring + rader   (--jp-mark-paper   → #FFFFFF, tema-stabilt; EJ --jp-surface)
// Mono-fallback: sätt accent = paper (raderna läses som urtag), primary = bläck.

export interface BrandMarkSvgProps {
  width: number;
  height: number;
  primaryFill: string;
  accentFill: string;
  paperFill: string;
  className?: string;
  ariaLabel?: string;
  ariaHidden?: boolean;
}

export function BrandMarkSvg({
  width,
  height,
  primaryFill,
  accentFill,
  paperFill,
  className,
  ariaLabel,
  ariaHidden,
}: BrandMarkSvgProps) {
  return (
    <svg
      width={width}
      height={height}
      viewBox="0 0 100 100"
      xmlns="http://www.w3.org/2000/svg"
      role="img"
      aria-label={ariaLabel}
      aria-hidden={ariaHidden}
      className={className}
    >
      <circle cx="50" cy="50" r="45" fill={primaryFill} />
      <circle cx="50" cy="50" r="37" fill="none" stroke={paperFill} strokeWidth="2.4" />
      <rect x="36" y="39.5" width="24" height="4.5" rx="2" fill={paperFill} />
      <rect x="33" y="47.5" width="30" height="5.5" rx="2.5" fill={accentFill} />
      <rect x="36" y="56.5" width="18" height="4.5" rx="2" fill={paperFill} />
      <path
        d="M56.5,50.6 L59,52.8 L64,47.6"
        fill="none"
        stroke={primaryFill}
        strokeWidth="1.7"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}
