import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getSavedJobAds } from "@/lib/api/saved-job-ads";
import { assertNever } from "@/lib/dto/_helpers";
import { SavedJobAdList } from "@/components/saved-job-ads/saved-job-ad-list";

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

  const result = await getSavedJobAds();

  return (
    <div className="flex flex-col">
      <div>
        <h1 className="jp-h1">Sparade annonser</h1>
        <p className="jp-lede">
          Annonser du har sparat för att kunna återvända till. Öppna annonsen
          för att se detaljer eller ta bort bokmärket.
        </p>
      </div>

      <div className="mt-7">{renderResult(result)}</div>
    </div>
  );
}

function renderResult(result: Awaited<ReturnType<typeof getSavedJobAds>>) {
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
            Kunde inte ladda sparade annonser
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
