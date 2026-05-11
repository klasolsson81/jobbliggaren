import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getApplicationById } from "@/lib/api/applications";
import { assertNever } from "@/lib/dto/_helpers";
import { ApplicationStatusBadge } from "@/components/applications/application-status-badge";
import { Button } from "@/components/ui/button";
import { TransitionForm } from "@/components/applications/transition-form";
import { AddNoteForm } from "@/components/applications/add-note-form";
import { AddFollowUpForm } from "@/components/applications/add-follow-up-form";
import { CHANNEL_LABELS, FOLLOW_UP_OUTCOME_LABELS } from "@/lib/applications/status";

interface Props {
  params: Promise<{ id: string }>;
}

export default async function AnsokningDetailPage({ params }: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const { id } = await params;
  const result = await getApplicationById(id);
  switch (result.kind) {
    case "ok":
      break;
    case "unauthorized":
      redirect("/logga-in");
    case "notFound":
      notFound();
    case "forbidden":
    case "error":
      return (
        <div className="flex flex-col gap-4">
          <h1 className="text-h1 font-medium text-text-primary">
            Kunde inte ladda ansökan
          </h1>
          <p className="text-body text-text-secondary">
            Ett tekniskt fel uppstod. Försök ladda om sidan eller gå tillbaka
            till ansökningslistan.
          </p>
          <div>
            <Button asChild variant="outline">
              <Link href="/ansokningar">Tillbaka till ansökningar</Link>
            </Button>
          </div>
        </div>
      );
    default:
      return assertNever(result);
  }

  const application = result.data;
  const createdAt = new Date(application.createdAt).toLocaleDateString("sv-SE");
  const updatedAt = new Date(application.updatedAt).toLocaleDateString("sv-SE");

  const sortedNotes = [...application.notes].sort(
    (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
  );
  const sortedFollowUps = [...application.followUps].sort(
    (a, b) => new Date(b.scheduledAt).getTime() - new Date(a.scheduledAt).getTime()
  );

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3">
        <Link
          href="/ansokningar"
          className="text-body-sm text-text-secondary hover:text-text-primary"
        >
          Ansökningar
        </Link>
        <span className="text-text-tertiary">/</span>
        <span className="text-body-sm text-text-secondary font-mono">{id.slice(0, 8)}</span>
      </div>

      <div className="flex items-start justify-between">
        <div className="flex flex-col gap-2">
          <ApplicationStatusBadge status={application.status} />
          <dl className="flex gap-4 text-body-sm text-text-secondary">
            <div className="flex gap-1">
              <dt>Skapad:</dt>
              <dd>{createdAt}</dd>
            </div>
            <div className="flex gap-1">
              <dt>Uppdaterad:</dt>
              <dd>{updatedAt}</dd>
            </div>
          </dl>
        </div>
      </div>

      {application.coverLetter && (
        <section aria-label="Personligt brev">
          <h2 className="mb-2 text-h3 font-medium text-text-primary">Personligt brev</h2>
          <div className="rounded-md border border-border bg-surface-secondary px-4 py-3">
            <p className="whitespace-pre-wrap text-body text-text-primary">
              {application.coverLetter}
            </p>
          </div>
        </section>
      )}

      <section aria-label="Byt status">
        <TransitionForm
          applicationId={id}
          currentStatus={application.status}
        />
      </section>

      <hr className="border-border" />

      <section aria-label="Uppföljningar">
        <h2 className="mb-4 text-h3 font-medium text-text-primary">Uppföljningar</h2>
        {sortedFollowUps.length === 0 ? (
          <p className="text-body-sm text-text-secondary">Inga uppföljningar registrerade.</p>
        ) : (
          <div className="mb-4 flex flex-col gap-2">
            {sortedFollowUps.map((fu) => (
              <div
                key={fu.id}
                className="rounded-md border border-border bg-card px-4 py-3 text-sm"
              >
                <div className="flex items-center justify-between">
                  <span className="font-medium text-text-primary">
                    {CHANNEL_LABELS[fu.channel] ?? fu.channel}
                  </span>
                  <span className="text-body-sm text-text-secondary">
                    {new Date(fu.scheduledAt).toLocaleDateString("sv-SE")}
                  </span>
                </div>
                <div className="mt-1 flex items-center gap-2 text-body-sm text-text-secondary">
                  <span>{FOLLOW_UP_OUTCOME_LABELS[fu.outcome] ?? fu.outcome}</span>
                  {fu.note && <span>— {fu.note}</span>}
                </div>
              </div>
            ))}
          </div>
        )}
        <AddFollowUpForm applicationId={id} />
      </section>

      <hr className="border-border" />

      <section aria-label="Noteringar">
        <h2 className="mb-4 text-h3 font-medium text-text-primary">Noteringar</h2>
        {sortedNotes.length === 0 ? (
          <p className="mb-4 text-body-sm text-text-secondary">Inga noteringar ännu.</p>
        ) : (
          <div className="mb-4 flex flex-col gap-2">
            {sortedNotes.map((note) => (
              <div
                key={note.id}
                className="rounded-md border border-border bg-card px-4 py-3"
              >
                <p className="text-body text-text-primary">{note.content}</p>
                <p className="mt-1 text-body-sm text-text-secondary">
                  {new Date(note.createdAt).toLocaleDateString("sv-SE")}
                </p>
              </div>
            ))}
          </div>
        )}
        <AddNoteForm applicationId={id} />
      </section>
    </div>
  );
}
