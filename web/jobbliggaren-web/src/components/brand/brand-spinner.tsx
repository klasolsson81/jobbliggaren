// Brand-spinner "Sigillet i rörelse" (logo-översyn 2026-06-13, Fas 2). Delar geometri med
// BrandMarkSvg (sigillet) men lägger till rörelse: registret pulserar (raderna tänds i
// sekvens) medan en guldbåge roterar längs inre ringen. Ren RSC + CSS-animation
// (klasser i globals.css, ingen JS/lib). prefers-reduced-motion → stillastående sigill.
// a11y: role="status" + aria-live + sr-only-text; SVG aria-hidden.

export interface BrandSpinnerProps {
  /** Diameter i px. Default 48. */
  size?: number;
  /** Status-text för skärmläsare. Default "Laddar". */
  label?: string;
}

export function BrandSpinner({ size = 48, label = "Laddar" }: BrandSpinnerProps) {
  return (
    <span role="status" aria-live="polite">
      <svg
        className="jp-brand-spinner"
        width={size}
        height={size}
        viewBox="0 0 100 100"
        xmlns="http://www.w3.org/2000/svg"
        aria-hidden={true}
      >
        <circle cx="50" cy="50" r="45" fill="var(--jp-mark-primary)" />
        <circle cx="50" cy="50" r="37" fill="none" stroke="var(--jp-mark-paper)" strokeWidth="2.4" />
        <circle
          className="jp-brand-spinner__arc"
          cx="50"
          cy="50"
          r="35"
          fill="none"
          stroke="var(--jp-mark-accent)"
          strokeWidth="5"
          strokeLinecap="round"
          strokeDasharray="58 162"
        />
        <rect
          className="jp-brand-spinner__row"
          x="36"
          y="39.5"
          width="24"
          height="4.5"
          rx="2"
          fill="var(--jp-mark-paper)"
        />
        <rect
          className="jp-brand-spinner__row jp-brand-spinner__row--2"
          x="33"
          y="47.5"
          width="30"
          height="5.5"
          rx="2.5"
          fill="var(--jp-mark-accent)"
        />
        <rect
          className="jp-brand-spinner__row jp-brand-spinner__row--3"
          x="36"
          y="56.5"
          width="18"
          height="4.5"
          rx="2"
          fill="var(--jp-mark-paper)"
        />
        <path
          d="M56.5,50.6 L59,52.8 L64,47.6"
          fill="none"
          stroke="var(--jp-mark-primary)"
          strokeWidth="1.7"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </svg>
      <span className="sr-only">{label}</span>
    </span>
  );
}
