import type { NextConfig } from "next";

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

export default nextConfig;
