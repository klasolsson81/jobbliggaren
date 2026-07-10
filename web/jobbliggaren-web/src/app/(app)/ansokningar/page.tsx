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
            {/* Sidåtgärderna ligger i plattan (Klas-beslut 2026-07-10): vita
                kontroller på gradienten — Ny ansökan = primär (vit-fylld),
                Statistik + Aktivitetsrapport = sekundära (vit kant, hero-ink
                via `.jp-pagehero .jp-btn--secondary`, samma vit-på-gradient-
                behandling som /cv-heron; löser G3-avläsningen "grön-
                genomskinlig" som en gång flyttade dem till den vita ytan).
                En-primary bibehållen. Hjälpen sitter som inline "?" (#408)
                tätt bunden till Aktivitetsrapport-knappen den förklarar (egen
                smal-gap-grupp så "?" inte läses som hjälp för primär-CTA:n,
                design-reviewer Minor 2026-07-10), med hero-ink-trigger så
                glyfen syns mot gradienten. */}
            <Link href="/statistik" className="jp-btn jp-btn--secondary">
              <BarChart3 size={16} aria-hidden="true" /> {t("ansokningar.statistics")}
            </Link>
            <span className="jp-pagehero__helpedctl">
              <Link href="/aktivitetsrapport" className="jp-btn jp-btn--secondary">
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
            <Link href="/ny-ansokan" className="jp-btn jp-btn--primary">
              <Plus size={16} aria-hidden="true" /> {t("ansokningar.newApplication")}
            </Link>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
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
