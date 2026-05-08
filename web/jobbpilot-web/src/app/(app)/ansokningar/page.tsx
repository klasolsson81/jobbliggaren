import Link from "next/link";
import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getPipeline } from "@/lib/api/applications";
import { ApplicationCard } from "@/components/applications/application-card";
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

  const groups = await getPipeline();
  const nonEmpty = groups.filter((g) => g.count > 0);
  const total = groups.reduce((sum, g) => sum + g.count, 0);

  const sorted = [...nonEmpty].sort(
    (a, b) =>
      PIPELINE_ORDER.indexOf(a.status) - PIPELINE_ORDER.indexOf(b.status)
  );

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-h1 font-medium text-text-primary">Ansökningar</h1>
        <Button asChild size="sm">
          <Link href="/ansokningar/ny">Ny ansökan</Link>
        </Button>
      </div>

      {total === 0 ? (
        <div className="rounded-md border border-border bg-surface-secondary px-6 py-10 text-center">
          <p className="text-body text-text-secondary">Inga ansökningar</p>
          <p className="mt-1 text-body-sm text-text-tertiary">
            Skapa din första ansökan för att komma igång.
          </p>
        </div>
      ) : (
        <div className="flex flex-col gap-8">
          {sorted.map((group) => (
            <section key={group.status} aria-label={getStatusLabel(group.status)}>
              <div className="mb-3 flex items-center gap-2">
                <h2 className="text-h3 font-medium text-text-primary">
                  {getStatusLabel(group.status)}
                </h2>
                <span className="inline-flex items-center rounded-pill bg-surface-tertiary px-2 py-0.5 text-xs font-medium text-text-secondary">
                  {group.count}
                </span>
              </div>
              <div className="flex flex-col gap-2">
                {group.applications.map((app) => (
                  <ApplicationCard key={app.id} application={app} />
                ))}
              </div>
            </section>
          ))}
        </div>
      )}
    </div>
  );
}
