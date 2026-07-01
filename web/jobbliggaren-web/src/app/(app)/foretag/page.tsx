import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getCompanyWatches } from "@/lib/api/company-follows";
import { assertNever } from "@/lib/dto/_helpers";
import { CompanyWatchList } from "@/components/company-follows/company-watch-list";

type PagesTranslator = Awaited<ReturnType<typeof getTranslations<"pages">>>;

/**
 * #311 #448 (ADR 0087 D2) — `/foretag`: the user's followed companies. A pure consumer of the existing
 * `GET /api/v1/me/company-watches` (no new backend). Parity with `/sparade` (RSC server-fetch +
 * discriminated-union result renderer + civic empty state).
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
    <div className="flex flex-col">
      <div>
        <h1 className="jp-h1">{t("foretag.title")}</h1>
        <p className="jp-lede">{t("foretag.lede")}</p>
      </div>

      <div className="mt-7">{renderResult(result, t)}</div>
    </div>
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
