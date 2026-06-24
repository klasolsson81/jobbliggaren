import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getMyMatches, markMatchesSeen } from "@/lib/api/me-matches";
import { assertNever } from "@/lib/dto/_helpers";
import { MatchList } from "@/components/matches/match-list";

type PagesTranslator = Awaited<ReturnType<typeof getTranslations<"pages">>>;

/**
 * ADR 0080 Vag 4 PR-5 — den dedikerade "Mina matchningar"-vyn. Auth-gated RSC,
 * paritet `/sparade` (list-page-mönstret). Per-user-data: ingen delad cache.
 *
 * MARK-SEEN ON OPEN (Klas-val: "views the matches surface", INTE varje page
 * load): vyn hämtar matchningarna FÖRST (så `isNew` speglar vattenmärket FÖRE
 * besöket) och avancerar SEDAN last-seen-vattenmärket via `markMatchesSeen()`.
 * Det anropet är icke-kritiskt och fire-and-forget: ett fel får aldrig blockera
 * renderingen (counten på Översikt nollställs då bara inte denna gång). `isNew`-
 * markeringen i listan och Översikts-counten delar samma vattenmärke ⇒
 * koherenta (samma fönster), per ADR 0080.
 */
export const dynamic = "force-dynamic";

export default async function MatchningarPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");

  // Hämta matchningarna (med pre-besök-`isNew`) FÖRE vattenmärket avanceras.
  const result = await getMyMatches();

  // Avancera vattenmärket (mark-seen). Icke-blockerande: vi väntar in svaret för
  // determinism men IGNORERAR utfallet — `markMatchesSeen` degraderar civilt och
  // kastar aldrig, så ett fel (rate-limit/nätverk) lämnar bara counten orörd.
  // En unauthorized HÄR driver ingen redirect (vyn renderas redan; nästa besök
  // korrigerar). Körs först när list-hämtningen lyckats (annars vore det
  // ohederligt att "se" matchningar vi inte kunde visa).
  if (result.kind === "ok") {
    await markMatchesSeen();
  }

  return (
    <div className="flex flex-col">
      <div>
        <h1 className="jp-h1">{t("matchningar.title")}</h1>
        <p className="jp-lede">{t("matchningar.lede")}</p>
      </div>

      <div className="mt-7">{renderResult(result, t)}</div>
    </div>
  );
}

function renderResult(
  result: Awaited<ReturnType<typeof getMyMatches>>,
  t: PagesTranslator
) {
  switch (result.kind) {
    case "ok":
      return <MatchList items={result.data} />;
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
          <p className="text-body font-medium">
            {t("matchningar.loadErrorTitle")}
          </p>
          <p className="mt-1 text-body-sm">{t("common.errorBodyReload")}</p>
        </div>
      );
    default:
      return assertNever(result);
  }
}
