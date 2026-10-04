import Link from "next/link";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { BarChart3, FileText, Plus, Search } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getPipeline } from "@/lib/api/applications";
import { readApplicationsView } from "@/lib/applications/view-preference";
import { assertNever } from "@/lib/dto/_helpers";
import { ApplicationLoadError } from "@/components/applications/application-load-error";
import { ApplicationsPipeline } from "@/components/applications/applications-pipeline";
import { InfoDialog } from "@/components/common/info-dialog";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("ansokningar.meta.title") };
}

export default async function AnsokningarPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const ta = await getTranslations("aktivitetsrapport");
  const result = await getPipeline();

  // The hero and "Ny ansökan" work without the list, so a failed read keeps them and shows
  // the error below them (#1827 M6), in the block the detail page uses.
  let content: React.ReactNode;
  switch (result.kind) {
    case "ok": {
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

      content =
        total === 0 ? (
          // "Ny ansökan" stands in the hero above, so the empty state offers the one
          // step it does not: finding a job (#1827 Minor 14, ADR 0068 G3).
          <div className="jp-empty">
            <div className="jp-empty__title">{t("ansokningar.emptyTitle")}</div>
            <div className="jp-empty__actions">
              <Link href="/jobb" className="jp-btn jp-btn--primary">
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
        );
      break;
    }
    case "unauthorized":
      redirect("/logga-in");
    case "rateLimited":
      content = (
        <ApplicationLoadError
          title={t("common.rateLimitedTitle")}
          body={t("common.rateLimitedBody", {
            seconds: result.retryAfterSeconds,
          })}
        />
      );
      break;
    case "notFound":
    case "forbidden":
    case "error":
      content = (
        <ApplicationLoadError
          title={t("ansokningar.loadErrorTitle")}
          body={t("common.errorBodyReload")}
        />
      );
      break;
    default:
      return assertNever(result);
  }

  return (
    <>
      {/* F6 P5 Punkt 6 — page-hero (HANDOVER-v4 §2.3). */}
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 tabIndex={-1} className="jp-pagehero__title">{t("ansokningar.title")}</h1>
            <p className="jp-pagehero__lede">{t("ansokningar.lede")}</p>
          </div>
          {/* Sidåtgärderna ligger i plattan (Klas-beslut 2026-07-10; #805
              punkt 7): två rader i asiden via den ansökningar-scopeade
              `--stacked`-modifiern (bas-`.jp-pagehero__aside` är DELAD
              och lämnas orörd). Rad 1 = primär "Ny ansökan" (ren vit fyllning) —
              den ENDA solida primären (ADR 0038). Rad 2 = verktygsraden Statistik
              + Aktivitetsrapport, nu VIT-fyllda läsbara kontroller (accent-50
              off-white, tema-pinnad i plattan) i stället för den tidigare
              genomskinliga vit-kanten Klas läste som "grön-genomskinlig" —
              subtilt underordnade den rena vita primären + egen rad = hierarki
              bevarad. Aktivitetsrapport-hjälpens "?" (#408) sitter INNE i
              knappens pill (`__helpedctl` = en enad vit yta) så glyfen läses som
              del av just den kontrollen. */}
          <div className="jp-pagehero__aside jp-pagehero__aside--stacked">
            <div className="jp-pagehero__btnrow">
              <Link href="/ny-ansokan" className="jp-btn jp-btn--primary">
                <Plus size={16} aria-hidden="true" /> {t("ansokningar.newApplication")}
              </Link>
            </div>
            <div className="jp-pagehero__btnrow">
              <Link href="/statistik" className="jp-btn jp-btn--secondary">
                <BarChart3 size={16} aria-hidden="true" /> {t("ansokningar.statistics")}
              </Link>
              <span className="jp-pagehero__helpedctl">
                <Link
                  href="/aktivitetsrapport"
                  className="jp-btn jp-btn--secondary jp-pagehero__helpedbtn"
                >
                  <FileText size={16} aria-hidden="true" />{" "}
                  {t("ansokningar.activityReport")}
                </Link>
                <InfoDialog
                  title={ta("info.title")}
                  paragraphs={[ta("info.p1"), ta("info.p2"), ta("info.p3")]}
                  ariaLabel={ta("info.whatIsThisAria")}
                  triggerClassName="jp-pagehero__help"
                />
              </span>
            </div>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">{content}</div>
    </>
  );
}
