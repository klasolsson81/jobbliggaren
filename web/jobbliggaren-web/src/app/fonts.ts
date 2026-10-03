import { JetBrains_Mono, Source_Sans_3 } from "next/font/google";

// The two self-hosted faces, in their own module so that BOTH documents the
// app renders can set their variables on <html>: the root layout, and
// global-error.tsx, which replaces the root layout and whose <html> therefore
// carried none — the whole last-resort surface then fell to the browser's
// default serif, because `--font-sans` resolves through `var(--font-sans)` and
// an unset variable makes the chain invalid (design-reviewer Major on PR #1953,
// measured 2026-10-03 in Chromium 153 with the production CSS).
//
// Weight ranges (LP-1 #254; font swap #549 WS4 — Hanken Grotesk → Source Sans 3
// per ADR 0091: higher x/cap 0.736, USWDS/CSN civic pedigree). Source Sans 3
// ships 200–900; the app loads only its actual consumers 400–800: 800 = the
// landing hero verb stack (.jp-land-hero__stack-verb, förslag 3a) +
// .jp-pagehero__title, 700 = brand wordmark + stat numbers. Unused weights are
// deliberately not loaded — dead weight + an extra font-fetch against the CWV
// budget (CLAUDE.md §5 / §2.5 / ADR 0045). Mono carries 400–700. NOTE (#1054):
// mono 700's written justification was ".jp-land-top__stat__num (mono, live)" —
// false twice over: that rule had no consumer (removed in #1054) and it set
// --jp-font-sans, not mono. Whether mono 700 still has a consumer is an open
// perf-lane question; changing the weight list is a rendering/perf change-reason
// and is deliberately NOT made here. Mono has NO 800 consumer, so it is not loaded.
export const sourceSans3 = Source_Sans_3({
  subsets: ["latin"],
  weight: ["400", "500", "600", "700", "800"],
  variable: "--font-sans",
  display: "swap",
});

export const jetBrainsMono = JetBrains_Mono({
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
  variable: "--font-mono",
  display: "swap",
});

/** The className every document's `<html>` carries: both variables and the sans default. */
export const documentFontClassName = `${sourceSans3.variable} ${jetBrainsMono.variable} h-full font-sans`;
