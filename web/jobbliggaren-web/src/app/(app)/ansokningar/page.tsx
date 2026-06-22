import type { ReactNode } from "react";
import Link from "next/link";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Plus, Search } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getPipeline } from "@/lib/api/applications";
import { assertNever } from "@/lib/dto/_helpers";
import { ApplicationRow } from "@/components/applications/application-row";
import { ApplicationsPipeline } from "@/components/applications/applications-pipeline";
import type { ApplicationStatus } from "@/lib/types/applications";

export default async function AnsokningarPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
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

  // ApplicationRow förblir server-renderbar (CTO punkt 4). Den server-renderas
  // HÄR i RSC och passas in i client-ön som en serialiserbar ReactNode[]-
  // slot-map keyad på status. Renderad ReactNode är serialiserbar över
  // RSC→Client-gränsen — en render-prop-FUNKTION är det INTE (Next.js
  // use-client.md rad 50-57; render-prop-funktionen orsakade prod-incidenten
  // i commit eece124, nu reverterad). Client-ön slår upp slots per status och
  // anropar ingen funktion.
  const rowSlots = {} as Record<ApplicationStatus, ReactNode[]>;
  for (const group of groups) {
    rowSlots[group.status] = group.applications.map((application) => (
      <ApplicationRow key={application.id} application={application} />
    ));
  }

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
            <Link href="/ansokningar/ny" className="jp-btn jp-btn--primary">
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
              <Link href="/ansokningar/ny" className="jp-btn jp-btn--primary">
                <Plus size={14} aria-hidden="true" /> {t("ansokningar.emptyCreateFirst")}
              </Link>
              <Link href="/jobb" className="jp-btn jp-btn--ghost">
                <Search size={14} aria-hidden="true" /> {t("ansokningar.emptySearchFirst")}
              </Link>
            </div>
          </div>
        ) : (
          <ApplicationsPipeline groups={groups} rowSlots={rowSlots} />
        )}
      </div>
    </>
  );
}
