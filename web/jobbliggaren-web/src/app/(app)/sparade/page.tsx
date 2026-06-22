import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getSavedJobAds } from "@/lib/api/saved-job-ads";
import { assertNever } from "@/lib/dto/_helpers";
import { SavedJobAdList } from "@/components/saved-job-ads/saved-job-ad-list";

type PagesTranslator = Awaited<ReturnType<typeof getTranslations<"pages">>>;

/**
 * F6 P5 Punkt 2 Del A — `/sparade`-sidan. Listar inloggad användares
 * bokmärkta annonser. Paritet `/sokningar` (ADR 0060 FE-arbetet).
 *
 * Tom-tillstånd ger kontext om var bokmärken skapas (i annonsdetaljen).
 * Borttagen JobAd renderas med fallback-rad ("Annonsen är borttagen" —
 * ADR 0048 Beslut c soft-delete-trail respekteras).
 */
export default async function SparadePage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const result = await getSavedJobAds();

  return (
    <div className="flex flex-col">
      <div>
        <h1 className="jp-h1">{t("sparade.title")}</h1>
        <p className="jp-lede">{t("sparade.lede")}</p>
      </div>

      <div className="mt-7">{renderResult(result, t)}</div>
    </div>
  );
}

function renderResult(
  result: Awaited<ReturnType<typeof getSavedJobAds>>,
  t: PagesTranslator
) {
  switch (result.kind) {
    case "ok":
      return <SavedJobAdList items={result.data} />;
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
            {t("sparade.loadErrorTitle")}
          </p>
          <p className="mt-1 text-body-sm">{t("common.errorBodyReload")}</p>
        </div>
      );
    default:
      return assertNever(result);
  }
}
