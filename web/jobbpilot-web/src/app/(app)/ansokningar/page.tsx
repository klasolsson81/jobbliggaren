import Link from "next/link";
import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getPipeline } from "@/lib/api/applications";
import { assertNever } from "@/lib/dto/_helpers";
import { ApplicationRow } from "@/components/applications/application-row";
import { ApplicationsPipeline } from "@/components/applications/applications-pipeline";
import { Button } from "@/components/ui/button";
import type { ApplicationDto } from "@/lib/types/applications";

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

  // ApplicationRow förblir server-renderbar (CTO punkt 4). Den renderas här i
  // RSC och passas in i client-ön via render-prop — client-ön äger bara
  // kollaps-state och ankarnav, aldrig rad-utseendet.
  const renderRow = (application: ApplicationDto) => (
    <ApplicationRow key={application.id} application={application} />
  );

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
        <ApplicationsPipeline groups={groups} renderRow={renderRow} />
      )}
    </div>
  );
}
