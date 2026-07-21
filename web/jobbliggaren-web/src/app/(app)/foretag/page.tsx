import type { ReactNode } from "react";
import Link from "next/link";
import { redirect } from "next/navigation";
import { Search } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getCompanyWatches, markFollowedAdsSeen } from "@/lib/api/company-follows";
import { getCompanyWatchCriteria, getCriterionReference } from "@/lib/api/company-criteria";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
import { getApplicationHistory } from "@/lib/api/application-history";
import { assertNever, type ApiResult } from "@/lib/dto/_helpers";
import type { CriterionReference } from "@/lib/dto/company-criteria";
import { CompanyLookup } from "@/components/company-follows/company-lookup";
import { CompanyWatchList } from "@/components/company-follows/company-watch-list";
import { CriteriaSection } from "@/components/company-criteria/criteria-section";
import { ApplicationHistoryList } from "@/components/application-history/application-history-list";

// A degraded reference load must not fail the whole page (parity with the F4b taxonomy degradation):
// an empty tree makes the picker show civil "unavailable" notices and disables creating.
const EMPTY_CRITERION_REFERENCE: CriterionReference = {
  sniVersion: "",
  kommunVersion: "",
  sni: [],
  lan: [],
};

type PagesTranslator = Awaited<ReturnType<typeof getTranslations<"pages">>>;

/**
 * #311 #448 (ADR 0087 D2) — `/foretag`: the user's "Företag" hub. A pure consumer of existing backend
 * endpoints (no new backend, no write path). RSC server-fetch + discriminated-union result renderers +
 * civic empty states (data-fetch parity `/sparade`).
 *
 * Layout (#515, Klas live-review 2026-07-02): the v3-native page standard — `jp-pagehero` (the green
 * gradient plate, ADR 0068) + content in `jp-container jp-page`, route registered in `V3_NATIVE_ROUTES`.
 *
 * The route is the stable "företag" hub noun (senior-cto-advisor 2026-07-01, Variant B — a focused
 * page, NOT a tab scaffold). It composes ADDITIVE sections (senior-cto-advisor 2026-07-03, Fork 1A):
 *  - "Bevakade företag" — the followed-company list (`ListCompanyWatchesQuery`, #452 counters).
 *  - "Ansökningshistorik" — the caller's application history grouped by employer (#444's projection,
 *    now unblocked: #444 merged + #456 DPIA closed). Each employer group carries the historik-räknare
 *    (`ApplicationCount`) inline (Fork 2A — the flat #444 DTO IS the företagsdetalj-räknare + detalj, so
 *    no per-company route / no new BE query). org.nr is masked+flagged BE-side (ADR 0087 D8(c)).
 */
export default async function ForetagPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");

  // Both sections are pure read consumers — fetch in parallel (independent endpoints). The follow-rail
  // watermark advance (Bevakning F2 #801, RF-6=6B) rides the same parallel batch: visiting this hub is
  // the Klas-chosen "seen" trigger, so it resets the /oversikt "nya annonser från bevakade företag"-
  // count. It is AWAITED inside Promise.all (result ignored, not destructured) — deliberately NOT a
  // detached promise: a detached write could be killed when the RSC request scope closes and silently
  // lost, so awaiting it in-batch guarantees it completes. It cannot reject the batch (markFollowedAdsSeen
  // never throws — a failure just leaves the count un-reset this visit), and no seenThrough is sent (the
  // hub renders no individual hits to preserve → the backend advances to clock-now, the safe fallback).
  // F4b: the per-watch filter editor reuses the match-setup ort picker, which needs the taxonomy tree.
  // Fetched server-side alongside the rest (it is a per-deploy static snapshot, cached) and passed down;
  // on failure the picker degrades civilly to an empty region list rather than failing the page.
  // NOTE: markFollowedAdsSeen stays LAST. It is awaited for its side effect and its result is never
  // destructured, so anything placed after it would silently bind to the wrong promise.
  const [
    watchResult,
    historyResult,
    taxonomyResult,
    criteriaResult,
    referenceResult,
  ] = await Promise.all([
    getCompanyWatches(),
    getApplicationHistory(),
    getTaxonomyTree(),
    // #560 PR-3 — the "Smarta bevakningar" section: the user's criteria + the SCB reference tree the
    // create/edit picker renders (per-deploy static, cached). Both ride the same parallel batch.
    getCompanyWatchCriteria(),
    getCriterionReference(),
    markFollowedAdsSeen(),
  ]);

  const regions = taxonomyResult.kind === "ok" ? taxonomyResult.data.regions : [];
  const criterionReference =
    referenceResult.kind === "ok" ? referenceResult.data : EMPTY_CRITERION_REFERENCE;

  // #454 (ADR 0088, F1(a) — CTO-bind + Klas 2026-07-02): registry-uppslags-sektionen är
  // FEATURE-DARK i prod tills den riktiga SCB-adaptern aktiveras (en sökruta som alltid svarar
  // "inte aktiverat" är en död civic-kontroll). Explicit env vinner ("true"/"false"); utan env
  // följer den backends provider-gating — PÅ i development (Fake-providern), AV annars
  // (Null-providern). Aktiverings-PR:en tänder prod via COMPANY_REGISTRY_ENABLED=true.
  const registryEnabled =
    process.env.COMPANY_REGISTRY_ENABLED === "true" ||
    (process.env.COMPANY_REGISTRY_ENABLED !== "false" &&
      process.env.NODE_ENV === "development");

  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{t("foretag.title")}</h1>
            <p className="jp-pagehero__lede">{t("foretag.lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        {/* #560 PR-B — link to the general company-register search (name/branch/kommun/org.nr). */}
        <Link
          href="/foretag/sok"
          className="jp-btn jp-btn--primary mb-6 inline-flex w-fit items-center gap-2"
        >
          <Search size={16} aria-hidden="true" />
          {t("foretag.sok.hubLink")}
        </Link>

        {registryEnabled && <CompanyLookup />}

        <section className="jp-section scroll-mt-6">
          <h2 className="jp-section__title">{t("foretag.watchesHeading")}</h2>
          {/* #998 (S3) — a lede that names the notification mechanic ("nya annonser"),
              the one thing that distinguishes Bevakade företag from Smarta bevakningar
              (a browsing surface with no per-company notices). */}
          <p className="mt-1 mb-4 max-w-prose text-body-sm text-text-primary">
            {t("foretag.watchesLede")}
          </p>
          {renderSection(watchResult, t, t("foretag.loadErrorTitle"), (data) => (
            <CompanyWatchList items={data} regions={regions} />
          ))}
        </section>

        <section id="smarta-bevakningar" className="jp-section scroll-mt-6">
          <h2 className="jp-section__title">{t("foretag.criteria.heading")}</h2>
          {renderSection(
            criteriaResult,
            t,
            t("foretag.criteria.loadErrorTitle"),
            (data) => (
              <CriteriaSection items={data} reference={criterionReference} />
            )
          )}
        </section>

        <section id="ansokningshistorik" className="jp-section scroll-mt-6">
          <h2 className="jp-section__title">{t("foretag.historyHeading")}</h2>
          {renderSection(historyResult, t, t("foretag.historyLoadErrorTitle"), (data) => (
            <ApplicationHistoryList items={data} />
          ))}
        </section>
      </div>
    </>
  );
}

/**
 * Shared discriminated-union renderer for a `/foretag` section: `ok` → the section's own content;
 * `unauthorized` → login redirect; `rateLimited`/error → civic inline notices (parity across sections
 * so the two reads degrade identically). Keeps the two section reads DRY (ADR 0030 list semantics — a
 * collection endpoint never surfaces `notFound`; it collapses to the error notice).
 */
function renderSection<T>(
  result: ApiResult<T>,
  t: PagesTranslator,
  loadErrorTitle: string,
  renderOk: (data: T) => ReactNode
): ReactNode {
  switch (result.kind) {
    case "ok":
      return renderOk(result.data);
    case "unauthorized":
      redirect("/logga-in");
    case "rateLimited":
      return (
        <div
          role="alert"
          className="rounded-md border border-warning-700/30 bg-warning-50 px-6 py-4"
        >
          <p className="text-body font-medium text-warning-700">
            {t("common.rateLimitedTitle")}
          </p>
          <p className="mt-1 text-body-sm text-warning-700">
            {t("common.rateLimitedBody", { seconds: result.retryAfterSeconds })}
          </p>
        </div>
      );
    case "notFound":
    case "forbidden":
    case "error":
      return (
        <div className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700">
          <p className="text-body font-medium">{loadErrorTitle}</p>
          <p className="mt-1 text-body-sm">{t("common.errorBodyReload")}</p>
        </div>
      );
    default:
      return assertNever(result);
  }
}
