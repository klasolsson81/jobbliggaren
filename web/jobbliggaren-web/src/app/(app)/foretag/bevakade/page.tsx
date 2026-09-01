import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getCompanyWatches } from "@/lib/api/company-follows";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
import { CompanyWatchList } from "@/components/company-follows/company-watch-list";
import { ForetagPagehero } from "@/components/foretag/foretag-pagehero";
import { ForetagSubnav } from "@/components/foretag/foretag-subnav";
import { renderSection } from "@/components/foretag/foretag-section";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("foretag.bevakade.meta.title") };
}

/**
 * `/foretag/bevakade` (S1 #996) — the Bevakade företag surface: the followed-company list. (The org.nr
 * follow-lookup was removed here per Klas live-review 2026-07-22 — company search lives under Sök
 * företag; the follow-via-org.nr consolidation is S2 #997.) This is the default landing of the /foretag hub (Klas 2026-07-21, "Bevakade först"): the
 * `/foretag` root redirects here, and the /oversikt "nya annonser från bevakade företag"-notis links
 * here. It is its own NOTIFICATION surface — distinct from Smarta bevakningar (a browsing surface with
 * no per-company notices), ADR 0117.
 */
export default async function BevakadeForetagPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");

  // The followed-companies list is a pure read consumer; the taxonomy tree feeds the per-watch filter
  // editor (it reuses the match-setup ort picker), fetched server-side alongside it — a per-deploy
  // static snapshot, cached — and degrading civilly to an empty region list on failure rather than
  // failing the page.
  const [watchResult, taxonomyResult] = await Promise.all([
    getCompanyWatches(),
    getTaxonomyTree(),
  ]);

  const regions = taxonomyResult.kind === "ok" ? taxonomyResult.data.regions : [];

  return (
    <>
      <ForetagPagehero
        title={t("foretag.watchesHeading")}
        lede={t("foretag.watchesLede")}
      />
      <div className="jp-container jp-page">
        {/* #1576 - the new-ads surface is reached through the sub-nav's own entry, not a loose
            link under it: a fifth item that looked like a tab without being one read as broken
            wayfinding (design-reviewer Major 7). It still carries NO number - a count here would
            be a second read of a delta whose whole point is that it resets when followed. */}
        <ForetagSubnav active="bevakade" />
        {renderSection(watchResult, t, t("foretag.loadErrorTitle"), (data) => (
          <CompanyWatchList items={data} regions={regions} />
        ))}
      </div>
    </>
  );
}
