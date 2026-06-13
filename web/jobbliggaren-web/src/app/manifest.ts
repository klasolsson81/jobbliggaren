import type { MetadataRoute } from "next";

// Next.js 16 file convention: Web App Manifest. Klas-STOPP A val G + design-reviewer
// 2026-05-25-rekommendation: G1 navy splash (brand-igenkänning > sektor-konvention).
// Background_color vit (matchar landing-light), theme_color navy (matchar landing-hero
// + OG-image). Klas kan flippa till "#FFFFFF" om Android-status-bar-mörkning oönskad.
export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Jobbliggaren",
    short_name: "Jobbliggaren",
    description: "Den svenska jobbansökningshanteraren",
    start_url: "/",
    display: "standalone",
    background_color: "#FFFFFF",
    theme_color: "#0A2647",
    lang: "sv",
    icons: [
      {
        src: "/icon.svg",
        type: "image/svg+xml",
        sizes: "any",
      },
      {
        src: "/apple-icon",
        type: "image/png",
        sizes: "180x180",
      },
    ],
  };
}
