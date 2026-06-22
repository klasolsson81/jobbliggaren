import type { MetadataRoute } from "next";
import { getLocale, getTranslations } from "next-intl/server";

// Next.js 16 file convention: Web App Manifest. Background_color vit (matchar
// landing-light). theme_color = granskogsgrön #15603F (matchar grön-accent-identiteten
// ADR 0068 + sigill-logon, logo-översyn 2026-06-13) — ersätter tidigare navy.
// Manifest-konventionen får vara async i Next.js → description återanvänder
// `metadata.description` via next-intl (locale från cookie, samma som resten).
export default async function manifest(): Promise<MetadataRoute.Manifest> {
  const t = await getTranslations("metadata");
  const locale = await getLocale();
  return {
    name: "Jobbliggaren",
    short_name: "Jobbliggaren",
    description: t("description"),
    start_url: "/",
    display: "standalone",
    background_color: "#FFFFFF",
    theme_color: "#15603F",
    lang: locale,
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
