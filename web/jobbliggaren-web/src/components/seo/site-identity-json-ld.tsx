import { useTranslations } from "next-intl";
import { JsonLdScript } from "@/components/seo/json-ld-script";
import { OFFICIAL_PROFILES } from "@/lib/seo/official-profiles";
import { SITE_URL, absoluteUrl } from "@/lib/site-url";

/**
 * The site's identity for search engines: `WebSite` (the site name a result shows)
 * and `Organization` (name and logo). Rendered on the home page only, which is where
 * Google reads a site name (#2092).
 *
 * Only facts the site already states: the name is the one `og:site_name` carries, the
 * alternate name is the domain itself, and the logo is the 180 px touch icon
 * (`app/apple-icon.tsx`), a PNG on white above Google's 112 px minimum. `sameAs` lists
 * the project's own pages elsewhere (`OFFICIAL_PROFILES`). No address or rating,
 * because none is published.
 */
export function SiteIdentityJsonLd() {
  const t = useTranslations("metadata");
  const name = t("applicationName");

  return (
    <>
      <JsonLdScript
        data={{
          "@context": "https://schema.org",
          "@type": "WebSite",
          name,
          alternateName: new URL(SITE_URL).hostname,
          url: SITE_URL,
        }}
      />
      <JsonLdScript
        data={{
          "@context": "https://schema.org",
          "@type": "Organization",
          name,
          url: SITE_URL,
          logo: absoluteUrl("/apple-icon"),
          sameAs: [...OFFICIAL_PROFILES],
        }}
      />
    </>
  );
}
