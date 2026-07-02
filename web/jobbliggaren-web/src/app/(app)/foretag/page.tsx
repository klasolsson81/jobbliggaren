import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getCompanyWatches } from "@/lib/api/company-follows";
import { assertNever } from "@/lib/dto/_helpers";
import { CompanyWatchList } from "@/components/company-follows/company-watch-list";

type PagesTranslator = Awaited<ReturnType<typeof getTranslations<"pages">>>;

/**
 * #311 #448 (ADR 0087 D2) — `/foretag`: the user's followed companies. A pure consumer of the existing
 * `GET /api/v1/me/company-watches` (no new backend). RSC server-fetch + discriminated-union result
 * renderer + civic empty state (data-fetch parity `/sparade`).
 *
 * Layout (#515, Klas live-review 2026-07-02): the v3-native page standard — `jp-pagehero` (the green
 * gradient plate, ADR 0068) + content in `jp-container jp-page`, with the route registered in
 * `V3_NATIVE_ROUTES` (app-shell opts it out of the transitional width container). The initial #448
 * delivery mirrored `/sparade`'s legacy header by mistake; /sparade + /sokningar + /matchningar remain
 * legacy with their own removal trigger (out of #515's scope).
 *
 * The route is the stable "företag" hub noun (senior-cto-advisor 2026-07-01, Variant B). Today it
 * hosts only the followed-company list; the företagsdetalj (historik-räknare) + ansökningshistorik
 * tabs of #448 are deferred behind #444 (missing history projection) and #456 (Art. 35 DPIA gate) —
 * the page evolves additively into the full hub when those unblock. #448 stays open with that
 * checklist.
 */
export default async function ForetagPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const result = await getCompanyWatches();

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

      <div className="jp-container jp-page">{renderResult(result, t)}</div>
    </>
  );
}

function renderResult(
  result: Awaited<ReturnType<typeof getCompanyWatches>>,
  t: PagesTranslator
) {
  switch (result.kind) {
    case "ok":
      return <CompanyWatchList items={result.data} />;
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
          <p className="text-body font-medium">{t("foretag.loadErrorTitle")}</p>
          <p className="mt-1 text-body-sm">{t("common.errorBodyReload")}</p>
        </div>
      );
    default:
      return assertNever(result);
  }
}
