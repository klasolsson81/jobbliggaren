import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getCompanyWatches, markFollowedAdsSeen } from "@/lib/api/company-follows";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
import { CompanyLookup } from "@/components/company-follows/company-lookup";
import { CompanyWatchList } from "@/components/company-follows/company-watch-list";
import { ForetagPagehero } from "@/components/foretag/foretag-pagehero";
import { ForetagSubnav } from "@/components/foretag/foretag-subnav";
import { renderSection } from "@/components/foretag/foretag-section";

/**
 * `/foretag/bevakade` (S1 #996) — the Bevakade företag surface: the followed-company list plus the
 * org.nr lookup that lets you follow a new company (incl. a 0-ad company the by-job-ad path cannot
 * reach). This is the default landing of the /foretag hub (Klas 2026-07-21, "Bevakade först"): the
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
  //
  // The follow-rail watermark advance (Bevakning F2 #801, RF-6=6B) rides the same parallel batch:
  // visiting the Bevakade surface is the Klas-chosen "seen" trigger, so it resets the /oversikt "nya
  // annonser från bevakade företag"-count. Because `/foretag` redirects here (S1 #996), a plain hub
  // visit still advances it. It is AWAITED inside Promise.all (result ignored, not destructured) —
  // deliberately NOT a detached promise: a detached write could be killed when the RSC request scope
  // closes and silently lost, so awaiting it in-batch guarantees it completes. It cannot reject the
  // batch (markFollowedAdsSeen never throws — a failure just leaves the count un-reset this visit), and
  // no seenThrough is sent (this surface renders no individual hits to preserve → the backend advances
  // to clock-now, the safe fallback).
  // NOTE: markFollowedAdsSeen stays LAST. It is awaited for its side effect and its result is never
  // destructured, so anything placed after it would silently bind to the wrong promise.
  const [watchResult, taxonomyResult] = await Promise.all([
    getCompanyWatches(),
    getTaxonomyTree(),
    markFollowedAdsSeen(),
  ]);

  const regions = taxonomyResult.kind === "ok" ? taxonomyResult.data.regions : [];

  // #454 (ADR 0088, F1(a) — CTO-bind + Klas 2026-07-02): the registry-lookup card is FEATURE-DARK in
  // prod until the real SCB adapter is switched on (a search box that always answers "not enabled" is a
  // dead civic control). Explicit env wins ("true"/"false"); without env it follows the backend's
  // provider gating — ON in development (Fake provider), OFF otherwise (Null provider). The activation
  // PR lights prod via COMPANY_REGISTRY_ENABLED=true.
  const registryEnabled =
    process.env.COMPANY_REGISTRY_ENABLED === "true" ||
    (process.env.COMPANY_REGISTRY_ENABLED !== "false" &&
      process.env.NODE_ENV === "development");

  return (
    <>
      <ForetagPagehero
        title={t("foretag.watchesHeading")}
        lede={t("foretag.watchesLede")}
      />
      <div className="jp-container jp-page">
        <ForetagSubnav active="bevakade" />
        {registryEnabled && <CompanyLookup />}
        {renderSection(watchResult, t, t("foretag.loadErrorTitle"), (data) => (
          <CompanyWatchList items={data} regions={regions} />
        ))}
      </div>
    </>
  );
}
