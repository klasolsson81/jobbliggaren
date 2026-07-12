import type { ReactNode } from "react";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getCompanyWatches, markFollowedAdsSeen } from "@/lib/api/company-follows";
import { getApplicationHistory } from "@/lib/api/application-history";
import { assertNever, type ApiResult } from "@/lib/dto/_helpers";
import { CompanyLookup } from "@/components/company-follows/company-lookup";
import { CompanyWatchList } from "@/components/company-follows/company-watch-list";
import { ApplicationHistoryList } from "@/components/application-history/application-history-list";

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
  const [watchResult, historyResult] = await Promise.all([
    getCompanyWatches(),
    getApplicationHistory(),
    markFollowedAdsSeen(),
  ]);

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
        {registryEnabled && <CompanyLookup />}

        <section className="jp-section scroll-mt-6">
          <h2 className="jp-section__title">{t("foretag.watchesHeading")}</h2>
          {renderSection(watchResult, t, t("foretag.loadErrorTitle"), (data) => (
            <CompanyWatchList items={data} />
          ))}
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
