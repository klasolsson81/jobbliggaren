import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getRecentSearches } from "@/lib/api/recent-searches";
import { assertNever } from "@/lib/dto/_helpers";
import { RecentSearchList } from "@/components/recent-searches/recent-search-list";

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

  const result = await getRecentSearches();

  return (
    <div className="flex flex-col">
      <div>
        <h1 className="jp-h1">Senaste sökningar</h1>
        <p className="jp-lede">
          Sökningar du gjort under Jobb. Klicka för att köra om en sökning, eller
          ta bort den du inte behöver.
        </p>
      </div>

      <div className="mt-7">{renderResult(result)}</div>
    </div>
  );
}

function renderResult(result: Awaited<ReturnType<typeof getRecentSearches>>) {
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
            För många förfrågningar
          </p>
          <p className="mt-1 text-body-sm text-warning-700">
            Du har gjort för många förfrågningar på kort tid. Försök igen om{" "}
            {result.retryAfterSeconds} sekunder.
          </p>
        </div>
      );
    case "notFound":
    case "forbidden":
    case "error":
      return (
        <div className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700">
          <p className="text-body font-medium">
            Kunde inte ladda senaste sökningar
          </p>
          <p className="mt-1 text-body-sm">
            Ett tekniskt fel uppstod. Försök ladda om sidan om en stund.
          </p>
        </div>
      );
    default:
      return assertNever(result);
  }
}
