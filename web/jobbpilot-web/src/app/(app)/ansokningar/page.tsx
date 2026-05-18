import Link from "next/link";
import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getPipeline } from "@/lib/api/applications";
import { assertNever } from "@/lib/dto/_helpers";
import { ApplicationRow } from "@/components/applications/application-row";
import { getStatusLabel } from "@/lib/applications/status";
import { Button } from "@/components/ui/button";
import type { ApplicationStatus } from "@/lib/types/applications";

const PIPELINE_ORDER: ApplicationStatus[] = [
  "Draft",
  "Submitted",
  "Acknowledged",
  "InterviewScheduled",
  "Interviewing",
  "OfferReceived",
  "Accepted",
  "Rejected",
  "Withdrawn",
  "Ghosted",
];

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
  const nonEmpty = groups.filter((g) => g.count > 0);
  const total = groups.reduce((sum, g) => sum + g.count, 0);

  const sorted = [...nonEmpty].sort(
    (a, b) =>
      PIPELINE_ORDER.indexOf(a.status) - PIPELINE_ORDER.indexOf(b.status)
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
        <div className="mt-8 flex flex-col gap-8">
          {sorted.map((group) => (
            <section key={group.status} aria-label={getStatusLabel(group.status)}>
              <div className="mb-2 flex items-baseline gap-3 border-b border-border-strong pb-2">
                <h2 className="jp-h2">{getStatusLabel(group.status)}</h2>
                <span className="font-mono text-[13px] font-medium text-text-secondary">
                  {group.count}
                </span>
              </div>
              <div className="flex flex-col border-t border-border-default">
                {group.applications.map((app) => (
                  <ApplicationRow key={app.id} application={app} />
                ))}
              </div>
            </section>
          ))}
        </div>
      )}
    </div>
  );
}
