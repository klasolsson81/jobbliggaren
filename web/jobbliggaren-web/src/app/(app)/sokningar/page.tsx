import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getRecentSearches } from "@/lib/api/recent-searches";
import { assertNever } from "@/lib/dto/_helpers";
import { RecentSearchList } from "@/components/recent-searches/recent-search-list";

type PagesTranslator = Awaited<ReturnType<typeof getTranslations<"pages">>>;

/**
 * ADR 0060 — Senaste sökningar (auto-fångade). Tidigare SavedSearch-listrender
 * (ADR 0039) ersatt här; backend-domänen behålls dolt per amendment 2026-05-20.
 *
 * GDPR Art. 13-information om data-insamling och retention är dokumenterad
 * i privacy-policy (Klas-uppgift per ADR 0060 Mekanik-not 6). Tom-tillstånd
 * ger kort kontext om var datan kommer ifrån.
 */
export default async function SokningarPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const result = await getRecentSearches();

  return (
    <div className="flex flex-col">
      <div>
        <h1 className="jp-h1">{t("sokningar.title")}</h1>
        <p className="jp-lede">{t("sokningar.lede")}</p>
      </div>

      <div className="mt-7">{renderResult(result, t)}</div>
    </div>
  );
}

function renderResult(
  result: Awaited<ReturnType<typeof getRecentSearches>>,
  t: PagesTranslator
) {
  switch (result.kind) {
    case "ok":
      return <RecentSearchList items={result.data} />;
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
            {t("common.rateLimitedBody", {
              seconds: result.retryAfterSeconds,
            })}
          </p>
        </div>
      );
    case "notFound":
    case "forbidden":
    case "error":
      return (
        <div className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700">
          <p className="text-body font-medium">
            {t("sokningar.loadErrorTitle")}
          </p>
          <p className="mt-1 text-body-sm">{t("common.errorBodyReload")}</p>
        </div>
      );
    default:
      return assertNever(result);
  }
}
