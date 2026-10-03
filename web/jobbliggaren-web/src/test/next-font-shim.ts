// Test shim for `next/font/google` (aliased in vitest.config.ts). Outside the
// Next compiler the real module has no loader behind it, so a component that
// imports `src/app/fonts.ts` — global-error.tsx since ADR 0148, and the root
// layout — would throw at import time. The shim returns the shape next/font
// returns (`variable`, `className`, `style`) with inert values.
type FontResult = { variable: string; className: string; style: { fontFamily: string } };

const result = (name: string): FontResult => ({
  variable: `${name}-variable`,
  className: `${name}-class`,
  style: { fontFamily: name },
});

export const Source_Sans_3 = (): FontResult => result("source-sans-3");
export const JetBrains_Mono = (): FontResult => result("jetbrains-mono");
