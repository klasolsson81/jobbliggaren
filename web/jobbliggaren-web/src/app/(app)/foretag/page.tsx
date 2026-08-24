import { redirect } from "next/navigation";
import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

/**
 * The hub root IS a served document, measured — not a 3xx. `redirect()` runs after
 * the `(app)` layout has begun streaming, so Next cannot answer 3xx and instead
 * answers 200 with a meta-refresh document (the same mechanism `src/proxy.ts`
 * documents for the org.nr wash). Measured 2026-08-24 in a production build:
 * `/foretag` returns 200 and 151 385 bytes carrying
 * `<meta http-equiv="refresh" content="1;url=/foretag/bevakade">`. A visitor sees
 * that document for about a second, so it needs a title of its own like any other.
 */
export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("foretag.meta.title") };
}

/**
 * `/foretag` (S1 #996) — the hub root redirects to the default surface, Bevakade företag (Klas
 * 2026-07-21, "Bevakade först"). The six sections that used to live here are split into focused
 * sub-pages — bevakade / sok / smarta-bevakningar / historik — sharing a persistent sub-nav
 * (`ForetagSubnav`). The top-nav "Företag" item still lands here, and this redirect preserves the
 * follow-rail watermark-on-hub-visit semantic (the Bevakade surface advances it, #801). Auth is
 * enforced by the target surface.
 */
export default function ForetagPage() {
  redirect("/foretag/bevakade");
}
