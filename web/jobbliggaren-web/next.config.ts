import path from "node:path";
import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";
import { buildSecurityHeaders } from "./src/lib/security/security-headers";

// next-intl without i18n routing: the plugin wires the request config at
// `src/i18n/request.ts` (locale resolved from the `NEXT_LOCALE` cookie). See ADR 0078.
const withNextIntl = createNextIntlPlugin("./src/i18n/request.ts");

const nextConfig: NextConfig = {
  // Remove the `X-Powered-By: Next.js` fingerprint (information disclosure).
  poweredByHeader: false,

  // #1046: the repo carries two lockfiles (repo root + this app), so Next's root
  // inference walks up and selects the repo root, then warns that it may be wrong.
  // Pin it to this app instead. Restored from the form `63ea6683` removed when it
  // opted out of Turbopack.
  //
  // Two clarifications, both measured rather than assumed. The multi-lockfile warning
  // is NOT Turbopack-specific — `find-root.js` emits it bundler-independently, and the
  // webpack branch points at `outputFileTracingRoot` instead; it simply was not visible
  // while we were on the opt-out path. And this pin is not cosmetic: Next's config
  // resolution also sets `outputFileTracingRoot` from it, so it becomes load-bearing the
  // day `output: "standalone"` lands (TD-106, the FE container that ADR 0050 Beslut 3
  // decides but nothing builds yet).
  turbopack: {
    root: path.resolve(__dirname),
  },

  // #748 (perf-audit epic #737, finding b7): rewrite `radix-ui` barrel imports
  // (`import { Dialog as DialogPrimitive } from "radix-ui"`) to direct per-module
  // imports at compile time. The unified barrel re-exports ~35 @radix-ui/react-*
  // namespaces.
  //
  // The finding that put this here was measured **on the webpack path, 2026-07-10**:
  // despite `sideEffects: false` a route chunk carried Menubar/NavigationMenu/Toast/
  // Slider/ScrollArea that no code imports. The durable reason it stays is bundler-
  // independent: `radix-ui` is absent from Next's default optimizePackageImports list
  // (which ships lucide-react, date-fns, lodash-es …), so nothing prunes the barrel
  // for us either way.
  //
  // #1046 re-measured across the bundler flip and found delivered JS within 45 B per
  // route — the move off webpack did not regress what #748 achieved. Note the shape of
  // that comparison: it was with-option-on-webpack vs with-option-on-Turbopack. Whether
  // Turbopack would prune the barrel *without* the option was not measured, so do not
  // read this as proof the option is still load-bearing.
  experimental: {
    optimizePackageImports: ["radix-ui"],
  },

  // Browser security headers on every response — issue #591 (epic #485).
  // Policy + rationale live in `src/lib/security/security-headers.ts`
  // (senior-cto-advisor bind, Approach A). Env branch resolves once at config
  // load: `next dev` → development, `next build`/`next start` → production.
  async headers() {
    const isDev = process.env.NODE_ENV === "development";
    return [
      {
        source: "/(.*)",
        headers: buildSecurityHeaders(isDev).map((h) => ({ ...h })),
      },
    ];
  },

  // F6 Prompt 2 (ADR 0057) — /mig → /installningar permanent redirect.
  // Status 308 (permanent + method-preserving) så bokmärken och externa
  // länkar mot gamla routen pekas korrekt utan att tappa POST/PUT-metod.
  // Next.js `permanent: true` ⇔ HTTP 308.
  async redirects() {
    return [
      {
        source: "/mig",
        destination: "/installningar",
        permanent: true,
      },
      {
        source: "/mig/:path*",
        destination: "/installningar/:path*",
        permanent: true,
      },
    ];
  },
};

export default withNextIntl(nextConfig);
