import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/site-url";
import { PROTECTED_PREFIXES } from "@/lib/auth/protected-routes";

/**
 * SEO foundation (#264) — `app/robots.ts` (Next.js App Router metadata route → /robots.txt).
 *
 * Allows crawling of the public marketing surface and the public job page and points at the sitemap; DISALLOWS the
 * authenticated app, the API, the admin surface, and the demo sandbox, and blocks the AI training crawlers.
 *
 * The authed `(app)` disallow entries are DERIVED from `PROTECTED_PREFIXES` — the single source of
 * truth, frozen to the `(app)` route group by `protected-routes.test.ts` (#513) — so this list can
 * never drift from the middleware gate.
 *
 * Boundary encoding (#583): each authed prefix P is emitted as BOTH `P$` (exact) and `P/` (subtree),
 * mirroring the middleware gate's segment-boundary match `pathname === P || pathname.startsWith(P + "/")`
 * (the `isProtectedPath` predicate, added in the middleware half, PR #598).
 * A bare `Disallow: /cv` uses prefix semantics and would ALSO shadow the PUBLIC `/cv-granskning`
 * marketing page (which the sitemap lists as canonical) — the #583 bug. `/cv$` + `/cv/` block `/cv`
 * and `/cv/...` while leaving `/cv-granskning` (and any future public sibling that shares an authed
 * prefix) crawlable. The `$` end-anchor and `/` boundary are honoured by Google/Bing/Yandex (RFC
 * 9309) and degrade safely: a `$`-unaware crawler treats `/cv$` as a literal no-op and still never
 * matches `/cv-granskning`.
 *
 * The robots-local entries are NOT `(app)` routes and are scoped to their subtree (trailing `/`) for
 * the same reason — a bare prefix could shadow a public sibling of the same latent class:
 *   - `/api/`   — the API surface.
 *   - `/gast/`  — the logged-out demo sandbox: mirrors the authed app shell with demo data, not
 *                 canonical content (avoids thin/duplicate indexing). No bare `/gast` page exists.
 *   - `/admin$` + `/admin/` — the admin surface; `/admin` is its overview page (#1973), so it is
 *                 boundary-encoded like an authed prefix.
 */
const ROBOTS_LOCAL_DISALLOW = ["/api/", "/gast/", "/admin$", "/admin/"] as const;

/**
 * Authed prefixes whose subtree is the public job page (ADR 0165, SEO epic #2090 S2): `/jobb/<uuid>` is answered
 * for anyone, while `/jobb` itself, the private list, stays blocked, with its query URLs. `/jobb/` is therefore
 * left out of the disallow list and the root `allow: "/"` covers it, since `(app)/jobb/` holds nothing but the
 * `[id]` page. No `Disallow` for the query form of the detail: robots.txt is not a canonicalisation tool, and
 * a shared or tracked link must keep its canonical readable (Google, "consolidate duplicate URLs").
 *
 * The internal route prefix of the page is deliberately not listed either: a blocked URL cannot show the 404 the
 * proxy gives it, and the line would only name the path.
 */
const PUBLIC_DETAIL_PREFIXES: readonly string[] = ["/jobb"];

/**
 * Training crawlers (Klas 2026-10-10): a vendor's training crawler or training opt-out token is blocked; its search
 * and user crawlers (Claude-SearchBot, Applebot, Amzn-SearchBot, Meta-WebIndexer and the like) and Google-Extended
 * are allowed by not being named. They get a group of their own, because a crawler uses one group only and `*` is
 * not merged into it. A later training crawler joins by the same rule, with its vendor page cited in an ADR 0165
 * amendment.
 */
const TRAINING_CRAWLERS = [
  "GPTBot",
  "CCBot",
  "ClaudeBot",
  "Applebot-Extended",
  "Amazonbot",
  "meta-externalagent",
] as const;

function disallowFor(prefix: string): string[] {
  return PUBLIC_DETAIL_PREFIXES.includes(prefix) ? [`${prefix}$`, `${prefix}?`] : [`${prefix}$`, `${prefix}/`];
}

export default function robots(): MetadataRoute.Robots {
  return {
    rules: [
      {
        userAgent: "*",
        allow: "/",
        disallow: [...ROBOTS_LOCAL_DISALLOW, ...PROTECTED_PREFIXES.flatMap(disallowFor)],
      },
      { userAgent: [...TRAINING_CRAWLERS], disallow: "/" },
    ],
    sitemap: `${SITE_URL}/sitemap.xml`,
  };
}
