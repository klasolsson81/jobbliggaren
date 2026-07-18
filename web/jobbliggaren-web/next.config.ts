import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";
import { buildSecurityHeaders } from "./src/lib/security/security-headers";

// next-intl without i18n routing: the plugin wires the request config at
// `src/i18n/request.ts` (locale resolved from the `NEXT_LOCALE` cookie). See ADR 0078.
const withNextIntl = createNextIntlPlugin("./src/i18n/request.ts");

const nextConfig: NextConfig = {
  // Remove the `X-Powered-By: Next.js` fingerprint (information disclosure).
  poweredByHeader: false,

  // #748 (perf-audit epic #737, finding b7): rewrite `radix-ui` barrel imports
  // (`import { Dialog as DialogPrimitive } from "radix-ui"`) to direct per-module
  // imports at compile time. The unified barrel re-exports ~35 @radix-ui/react-*
  // namespaces; despite `sideEffects: false`, the webpack build does NOT prune
  // the unimported ones (measured: a route chunk carried Menubar/NavigationMenu/
  // Toast/Slider/ScrollArea/… that no code imports). radix-ui is absent from
  // Next's default optimizePackageImports list (which ships lucide-react etc.).
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
