import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/site-url";

/**
 * SEO foundation (#264) — `app/sitemap.ts` (Next.js App Router metadata route → /sitemap.xml).
 *
 * Lists the canonical PUBLIC pages only: the marketing surface (the `(marketing)` landing +
 * `(marketing-inner)` content pages). Deliberately excluded:
 * - the authenticated app (`(app)` — middleware-protected; never indexed, see robots.ts),
 * - the `/gast` demo sandbox (not canonical content),
 * - the auth utility pages (`/logga-in`, `/registrera`) — added when open registration ships
 *   (#267); a closed-beta/waitlist entry is not a canonical SEO page yet.
 *
 * Explicit list (App Router sitemaps are static by design): add new public marketing pages here
 * as they ship. `lastModified` is intentionally omitted (kept deterministic + churn-free; valid
 * per the sitemap protocol). The landing gets the highest priority + a tighter change frequency.
 */
const MARKETING_PATHS = [
  "", // the landing page (/)
  "/om",
  "/kontakt",
  "/integritet",
  "/cookies",
  "/villkor",
  "/tips",
  "/vanliga-fragor",
] as const;

export default function sitemap(): MetadataRoute.Sitemap {
  return MARKETING_PATHS.map((path) => ({
    url: `${SITE_URL}${path}`,
    changeFrequency: path === "" ? "weekly" : "monthly",
    priority: path === "" ? 1 : 0.7,
  }));
}
