// Shared design constants for the next/og image renderers (opengraph-image +
// twitter-image). Satori renders OUTSIDE the DOM: globals.css custom
// properties cannot be referenced, so inline px/hex is structurally required
// here (#549 WS3). This module is the single documented mirror of the token
// values — if a mirrored token changes in globals.css, update it HERE too.
// Mirrored from globals.css :root (light):
export const OG_INK_1 = "#0C1A2E"; // = --jp-ink-1
export const OG_INK_2 = "#455366"; // = --jp-ink-2
export const OG_SURFACE = "#FFFFFF"; // = --jp-surface
export const OG_MARK_PRIMARY = "#15603F"; // = --jp-mark-primary (accent-800)
export const OG_MARK_ACCENT = "#E8C77B"; // = --jp-mark-accent (gold)
export const OG_MARK_PAPER = "#FFFFFF"; // = --jp-mark-paper

// Display typography for the 1200x630 canvas (OG-scale, not the app scale).
export const OG_TITLE_STYLE = {
  fontSize: "112px",
  fontWeight: 700,
  color: OG_INK_1,
  letterSpacing: "-0.025em",
  lineHeight: 1,
} as const;

export const OG_TAGLINE_STYLE = {
  fontSize: "32px",
  fontWeight: 500,
  color: OG_INK_2,
  lineHeight: 1.3,
} as const;
