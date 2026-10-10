import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { LandingHeroSection } from "@/components/landing/landing-hero-section";
import { LandingFeatures } from "@/components/landing/landing-features";
import { SiteIdentityJsonLd } from "@/components/seo/site-identity-json-ld";
import { absoluteUrl } from "@/lib/site-url";

/**
 * The home page is the brand's search result (#2092): a title the root template
 * completes with the brand, a description of what the site does, and its own
 * canonical. The root layout declares no canonical, so no other route inherits it.
 */
export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("landing");
  return {
    title: t("meta.title"),
    description: t("meta.description"),
    alternates: { canonical: absoluteUrl("/") },
  };
}

/**
 * Landing route (`/`) — "Liggaren" redesign (epic #267, LP-4 / #257).
 *
 * Composes the two sections inside the skip-link target:
 *  - <LandingHeroSection/>: the plate hero with the account card
 *  - <LandingFeatures/>: the six feature cells
 *
 * The chrome (SiteHeader with live stats, SiteFooter) is the LAYOUT's since
 * #1477, so that a throw here is caught by (marketing)/error.tsx with the frame
 * still standing. SiteHeader renders the surface's <SkipLink/>, so this page
 * must not render a second one — it owns the `#main` target instead.
 */
export default function LandingPage() {
  return (
    <main id="main" tabIndex={-1} className="flex-1 focus:outline-none">
      <LandingHeroSection />
      <LandingFeatures />
      <SiteIdentityJsonLd />
    </main>
  );
}
