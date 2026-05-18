import type { ReactNode } from "react";
import Link from "next/link";
import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getPipeline } from "@/lib/api/applications";
import { assertNever } from "@/lib/dto/_helpers";
import { ApplicationRow } from "@/components/applications/application-row";
import { ApplicationsPipeline } from "@/components/applications/applications-pipeline";
import { Button } from "@/components/ui/button";
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
        <div className="flex flex-col gap-4">
          <h1 className="text-h1 font-medium text-text-primary">
            För många förfrågningar
          </h1>
          <p className="text-body text-text-secondary">
            Du har gjort för många förfrågningar på kort tid. Försök igen om{" "}
            {result.retryAfterSeconds} sekunder.
          </p>
        </div>
      );
    case "notFound":
    case "forbidden":
    case "error":
      return (
        <div className="flex flex-col gap-4">
          <h1 className="text-h1 font-medium text-text-primary">
            Kunde inte ladda ansökningar
          </h1>
          <p className="text-body text-text-secondary">
            Ett tekniskt fel uppstod. Försök ladda om sidan om en stund.
          </p>
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
    <div className="flex flex-col">
      <div className="flex items-end justify-between">
        <div>
          <h1 className="jp-h1">Ansökningar</h1>
          <p className="jp-lede">
            Pipeline över alla ansökningar. Klicka på en rad för detaljer.
          </p>
        </div>
        <Button asChild>
          <Link href="/ansokningar/ny">Ny ansökan</Link>
        </Button>
      </div>

      {total === 0 ? (
        <div className="mt-7 border-y border-border-default px-1 py-12 text-center">
          <p className="text-body text-text-primary">Inga ansökningar</p>
          <p className="mt-1 text-body-sm text-text-secondary">
            Skapa din första ansökan för att komma igång.
          </p>
        </div>
      ) : (
        <ApplicationsPipeline groups={groups} rowSlots={rowSlots} />
      )}
    </div>
  );
}
