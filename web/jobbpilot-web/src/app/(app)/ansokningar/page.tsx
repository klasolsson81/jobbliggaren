import type { ReactNode } from "react";
import Link from "next/link";
import { redirect } from "next/navigation";
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
            <h1 className="jp-page__title">För många förfrågningar</h1>
            <p className="jp-page__lede">
              Du har gjort för många förfrågningar på kort tid. Försök igen
              om {result.retryAfterSeconds} sekunder.
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
            <h1 className="jp-page__title">Kunde inte ladda ansökningar</h1>
            <p className="jp-page__lede">
              Ett tekniskt fel uppstod. Försök ladda om sidan om en stund.
            </p>
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
            <h1 className="jp-pagehero__title">Mina ansökningar</h1>
            <p className="jp-pagehero__lede">
              Pipeline över alla ansökningar. Klicka på en rad för detaljer.
            </p>
          </div>
          <div className="jp-pagehero__aside">
            {/* G3 (Klas-fynd 2026-06-10): vit knapp i plattan, konsekvent
                med /jobb-bannerns vita kontroller (.jp-pagehero .jp-btn--
                primary = vit; ghost-på-gradient läste som grön). En-primary
                bibehållen: vit knapp i plattan vs grön i empty-kortet. */}
            <Link href="/ansokningar/ny" className="jp-btn jp-btn--primary">
              <Plus size={16} aria-hidden="true" /> Ny ansökan
            </Link>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        {total === 0 ? (
          <div className="jp-empty">
            <div className="jp-empty__kicker">Pipeline</div>
            <div className="jp-empty__title">Inga ansökningar ännu</div>
            <p className="jp-empty__body">
              Så fort du registrerar din första ansökan hamnar den här.
              Spåra status från utkast till svar utan att tappa en enda ansökan.
            </p>
            <div className="jp-empty__actions">
              <Link href="/ansokningar/ny" className="jp-btn jp-btn--primary">
                <Plus size={14} aria-hidden="true" /> Skapa första ansökan
              </Link>
              <Link href="/jobb" className="jp-btn jp-btn--ghost">
                <Search size={14} aria-hidden="true" /> Sök annonser först
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
