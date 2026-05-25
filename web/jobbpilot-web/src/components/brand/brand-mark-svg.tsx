// SSOT för JobbPilot brand-mark-geometri (4-point compass-star + center accent).
// Konsumeras av brand-logo.tsx, apple-icon.tsx, opengraph-image.tsx, twitter-image.tsx.
// `app/icon.svg` är file-convention-mirror (Next.js auto-favicon) — synka manuellt
// vid geometri-justering. CTO M1-triage 2026-05-25 Variant B.

export interface BrandMarkSvgProps {
  width: number;
  height: number;
  primaryFill: string;
  accentFill: string;
  className?: string;
  ariaLabel?: string;
  ariaHidden?: boolean;
}

export function BrandMarkSvg({
  width,
  height,
  primaryFill,
  accentFill,
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
      <polygon points="50,8 56,30 50,47 44,30" fill={primaryFill} />
      <polygon points="92,50 70,56 53,50 70,44" fill={primaryFill} />
      <polygon points="50,92 44,70 50,53 56,70" fill={primaryFill} />
      <polygon points="8,50 30,44 47,50 30,56" fill={primaryFill} />
      <circle cx="50" cy="50" r="5" fill={accentFill} />
    </svg>
  );
}
