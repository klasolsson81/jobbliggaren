import type { ReactNode } from "react";
import Link from "next/link";
import { redirect } from "next/navigation";
import { Plus } from "lucide-react";
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
    <div className="jp-container jp-page">
      <div
        className="jp-page__title-block"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "flex-end",
          gap: 16,
          flexWrap: "wrap",
        }}
      >
        <div>
          <h1 className="jp-page__title">Mina ansökningar</h1>
          <p className="jp-page__lede">
            Pipeline över alla ansökningar. Klicka på en rad för detaljer.
          </p>
        </div>
        <Link href="/ansokningar/ny" className="jp-btn jp-btn--primary">
          <Plus size={16} aria-hidden="true" /> Ny ansökan
        </Link>
      </div>

      {total === 0 ? (
        <div className="jp-empty">
          <div className="jp-empty__title">Inga ansökningar</div>
          Skapa din första ansökan för att komma igång.
        </div>
      ) : (
        <ApplicationsPipeline groups={groups} rowSlots={rowSlots} />
      )}
    </div>
  );
}
