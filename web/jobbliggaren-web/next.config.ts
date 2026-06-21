import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";

// next-intl without i18n routing: the plugin wires the request config at
// `src/i18n/request.ts` (locale resolved from the `NEXT_LOCALE` cookie). See ADR 0078.
const withNextIntl = createNextIntlPlugin("./src/i18n/request.ts");

const nextConfig: NextConfig = {
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
