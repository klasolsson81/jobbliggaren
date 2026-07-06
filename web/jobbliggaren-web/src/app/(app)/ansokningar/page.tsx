import Link from "next/link";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { BarChart3, FileText, Plus, Search } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getPipeline } from "@/lib/api/applications";
import { readApplicationsView } from "@/lib/applications/view-preference";
import { assertNever } from "@/lib/dto/_helpers";
import { ApplicationsPipeline } from "@/components/applications/applications-pipeline";
import { InfoDialog } from "@/components/common/info-dialog";

export default async function AnsokningarPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const ta = await getTranslations("aktivitetsrapport");
  const result = await getPipeline();
  switch (result.kind) {
    case "ok":
      break;
    case "unauthorized":
      redirect("/logga-in");
    case "rateLimited":
      return (
        <div className="jp-container jp-page">
          <div className="jp-page__title-block">
            <h1 className="jp-page__title">{t("common.rateLimitedTitle")}</h1>
            <p className="jp-page__lede">
              {t("common.rateLimitedBody", {
                seconds: result.retryAfterSeconds,
              })}
            </p>
          </div>
        </div>
      );
    case "notFound":
    case "forbidden":
    case "error":
      return (
        <div className="jp-container jp-page">
          <div className="jp-page__title-block">
            <h1 className="jp-page__title">
              {t("ansokningar.loadErrorTitle")}
            </h1>
            <p className="jp-page__lede">{t("common.errorBodyReload")}</p>
          </div>
        </div>
      );
    default:
      return assertNever(result);
  }

  const groups = result.data;
  const total = groups.reduce((sum, g) => sum + g.count, 0);

  // #630 PR 5 (ADR 0092 D2) — data-till-klient-pivot. Tidigare server-renderade
  // RSC:n varje ApplicationRow till en ReactNode[]-slot-map (`rowSlots`) och
  // passade den till ön (eece124-workaround). D2 supersederar det: ön får ren
  // SERIALISERBAR data (`groups: PipelineGroupDto[]`) och renderar raderna
  // själv (ApplicationRow är nu en klientkomponent). Enda som korsar
  // RSC→Client-gränsen är data + en referens-tidsstämpel — aldrig en funktion
  // eller ett renderat träd.
  //
  // "Nu" beräknas EN gång här (server) och passas som ISO-sträng (CTO-bind
  // #336-determinism bevarad — en referenspunkt per request, INTE new Date()
  // per rad i klienten → ingen hydrerings-drift, testbar med injicerat datum).
  // En primitiv sträng är entydigt serialiserbar; ön rekonstruerar Date en gång.
  const nowIso = new Date().toISOString();

  // #630 PR 8 (ADR 0092 D7) — vy-preferensen (Lista/Tavla) läses SSR ur cookien
  // så första-paint renderar rätt vy utan flash (ADR 0078-precedent, EJ
  // localStorage). Sidan är redan dynamisk (getServerSession/authedFetch) → ingen
  // ny render-kostnad. Ren serialiserbar sträng korsar RSC→Client-gränsen (D2).
  const initialView = await readApplicationsView();

  return (
    <>
      {/* F6 P5 Punkt 6 — page-hero (HANDOVER-v4 §2.3). */}
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{t("ansokningar.title")}</h1>
            <p className="jp-pagehero__lede">{t("ansokningar.lede")}</p>
          </div>
          <div className="jp-pagehero__aside">
            {/* G3 (Klas-fynd 2026-06-10): vit knapp i plattan, konsekvent
                med /jobb-bannerns vita kontroller (.jp-pagehero .jp-btn--
                primary = vit; ghost-på-gradient läste som grön). En-primary
                bibehållen: vit knapp i plattan vs grön i empty-kortet. */}
            <Link href="/ny-ansokan" className="jp-btn jp-btn--primary">
              <Plus size={16} aria-hidden="true" /> {t("ansokningar.newApplication")}
            </Link>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        {/* #316 — entry till AF-aktivitetsrapport-hjälpen. Sekundär knapp på
            den vita sid-ytan (jp-btn--secondary = solid yt-bakgrund, mörk ink,
            kant) i stället för ghost-på-gradient, som läste som
            grön-genomskinlig (Klas-fynd 2026-06-28; G3-precedens — sekundära
            knappar avgränsas inte rent mot hero-gradienten).
            #337 — "Vad är detta?" ligger nu direkt UNDER knappen den
            förklarar (text-only, ingen ikon), den kanoniska placerings-
            konventionen för InfoDialog. Knapp + förklaring staplas i en
            högerjusterad kolumn. */}
        <div className="mb-6 flex flex-wrap justify-end gap-3">
          <Link href="/statistik" className="jp-btn jp-btn--secondary">
            <BarChart3 size={16} aria-hidden="true" /> {t("ansokningar.statistics")}
          </Link>
          <div className="flex flex-col items-end gap-1.5">
            <Link
              href="/aktivitetsrapport"
              className="jp-btn jp-btn--secondary"
            >
              <FileText size={16} aria-hidden="true" />{" "}
              {t("ansokningar.activityReport")}
            </Link>
            <InfoDialog
              title={ta("info.title")}
              paragraphs={[ta("info.p1"), ta("info.p2"), ta("info.p3")]}
              showIcon={false}
            />
          </div>
        </div>
        {total === 0 ? (
          <div className="jp-empty">
            <div className="jp-empty__kicker">{t("ansokningar.emptyKicker")}</div>
            <div className="jp-empty__title">{t("ansokningar.emptyTitle")}</div>
            <p className="jp-empty__body">{t("ansokningar.emptyBody")}</p>
            <div className="jp-empty__actions">
              <Link href="/ny-ansokan" className="jp-btn jp-btn--primary">
                <Plus size={14} aria-hidden="true" /> {t("ansokningar.emptyCreateFirst")}
              </Link>
              <Link href="/jobb" className="jp-btn jp-btn--ghost">
                <Search size={14} aria-hidden="true" /> {t("ansokningar.emptySearchFirst")}
              </Link>
            </div>
          </div>
        ) : (
          <ApplicationsPipeline
            groups={groups}
            nowIso={nowIso}
            initialView={initialView}
          />
        )}
      </div>
    </>
  );
}
