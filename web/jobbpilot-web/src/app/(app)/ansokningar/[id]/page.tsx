import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getApplicationById } from "@/lib/api/applications";
import { assertNever } from "@/lib/dto/_helpers";
import { Button } from "@/components/ui/button";
import { StatusEditCard } from "@/components/applications/status-edit-card";
import { JobInfoPanel } from "@/components/applications/job-info-panel";
import { AddNoteForm } from "@/components/applications/add-note-form";
import { AddFollowUpForm } from "@/components/applications/add-follow-up-form";
import { RecordFollowUpOutcomeForm } from "@/components/applications/record-follow-up-outcome-form";
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
          <div>
            <Button asChild variant="outline">
              <Link href="/ansokningar">Tillbaka till ansökningar</Link>
            </Button>
          </div>
        </div>
      );
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
  const jobAd = application.jobAd ?? null;
  const shortId = id.slice(0, 8);
  const title = jobAd ? jobAd.title : `Ansökan #${shortId}`;

  const sortedNotes = [...application.notes].sort(
    (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
  );
  const sortedFollowUps = [...application.followUps].sort(
    (a, b) => new Date(b.scheduledAt).getTime() - new Date(a.scheduledAt).getTime()
  );

  return (
    <div className="flex flex-col gap-6">
      <nav aria-label="Brödsmulor" className="flex items-center gap-2">
        <Link
          href="/ansokningar"
          className="text-body-sm text-text-secondary hover:text-text-primary"
        >
          Ansökningar
        </Link>
        <span aria-hidden="true" className="text-text-tertiary">
          /
        </span>
        <span
          className={
            jobAd
              ? "text-body-sm text-text-primary"
              : "font-mono text-body-sm text-text-primary"
          }
        >
          {title}
        </span>
      </nav>

      <header className="flex flex-col gap-1">
        <h1 className={jobAd ? "jp-h1" : "jp-h1 font-mono"}>{title}</h1>
        {jobAd && (
          <p className="text-body text-text-secondary">{jobAd.company}</p>
        )}
      </header>

      {jobAd ? (
        <div className="grid grid-cols-1 gap-6 md:grid-cols-2 md:gap-8">
          <JobInfoPanel jobAd={jobAd} coverLetter={application.coverLetter} />
          <StatusEditCard
            applicationId={id}
            currentStatus={application.status}
          />
        </div>
      ) : (
        <div className="flex flex-col gap-6">
          <p className="rounded-md border border-border-structural bg-surface-primary px-4 py-3 text-body-sm text-text-secondary">
            Ingen kopplad annons — manuellt skapad ansökan.
          </p>
          <StatusEditCard
            applicationId={id}
            currentStatus={application.status}
          />
          {application.coverLetter && (
            <section
              aria-labelledby="cover-letter-title"
              className="rounded-md border border-border-structural bg-surface-primary"
            >
              <div className="border-b border-border-default px-4 py-3">
                <h2
                  id="cover-letter-title"
                  className="text-h3 font-semibold text-text-primary"
                >
                  Personligt brev
                </h2>
              </div>
              <div className="px-4 py-4">
                <p className="max-w-[68ch] whitespace-pre-wrap text-body text-text-primary">
                  {application.coverLetter}
                </p>
              </div>
            </section>
          )}
        </div>
      )}

      <section
        aria-labelledby="followups-title"
        className="rounded-md border border-border-structural bg-surface-primary"
      >
        <div className="border-b border-border-default px-4 py-3">
          <h2
            id="followups-title"
            className="text-h3 font-semibold text-text-primary"
          >
            Uppföljningar
          </h2>
        </div>

        <div className="flex flex-col gap-4 px-4 py-4">
          {sortedFollowUps.length === 0 ? (
            <p className="text-body-sm text-text-secondary">
              Inga uppföljningar registrerade.
            </p>
          ) : (
            <ul className="flex flex-col gap-3">
              {sortedFollowUps.map((fu) => {
                const recorded = fu.outcome !== "Pending";
                return (
                  <li
                    key={fu.id}
                    className="rounded-md border border-border-default px-4 py-3"
                  >
                    <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
                      <span className="font-medium text-text-primary">
                        {CHANNEL_LABELS[fu.channel] ?? fu.channel}
                      </span>
                      <span className="font-mono text-body-sm text-text-secondary">
                        {new Date(fu.scheduledAt).toLocaleDateString("sv-SE")}
                      </span>
                    </div>

                    <dl className="mt-2 flex flex-col gap-1 text-body-sm">
                      <div className="flex gap-2">
                        <dt className="text-text-secondary">Utfall:</dt>
                        <dd className="text-text-primary">
                          {FOLLOW_UP_OUTCOME_LABELS[fu.outcome] ?? fu.outcome}
                          {recorded && fu.outcomeAt && (
                            <span className="ml-1 font-mono text-text-secondary">
                              (
                              {new Date(fu.outcomeAt).toLocaleDateString(
                                "sv-SE"
                              )}
                              )
                            </span>
                          )}
                        </dd>
                      </div>
                      {fu.note && (
                        <div className="flex gap-2">
                          <dt className="text-text-secondary">Anteckning:</dt>
                          <dd className="text-text-primary">{fu.note}</dd>
                        </div>
                      )}
                    </dl>

                    {fu.outcome === "Pending" && (
                      <RecordFollowUpOutcomeForm
                        applicationId={id}
                        followUpId={fu.id}
                      />
                    )}
                  </li>
                );
              })}
            </ul>
          )}
        </div>

        <div className="border-t border-border-default px-4 py-4">
          <h3 className="mb-3 text-body font-medium text-text-primary">
            Lägg till uppföljning
          </h3>
          <AddFollowUpForm applicationId={id} />
        </div>
      </section>

      <section
        aria-labelledby="notes-title"
        className="rounded-md border border-border-structural bg-surface-primary"
      >
        <div className="border-b border-border-default px-4 py-3">
          <h2
            id="notes-title"
            className="text-h3 font-semibold text-text-primary"
          >
            Noteringar
          </h2>
        </div>

        <div className="flex flex-col gap-4 px-4 py-4">
          {sortedNotes.length === 0 ? (
            <p className="text-body-sm text-text-secondary">
              Inga noteringar ännu.
            </p>
          ) : (
            <ul className="flex flex-col gap-3">
              {sortedNotes.map((note) => (
                <li
                  key={note.id}
                  className="rounded-md border border-border-default px-4 py-3"
                >
                  <p className="text-body text-text-primary">{note.content}</p>
                  <p className="mt-1 font-mono text-body-sm text-text-secondary">
                    {new Date(note.createdAt).toLocaleDateString("sv-SE")}
                  </p>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="border-t border-border-default px-4 py-4">
          <h3 className="mb-3 text-body font-medium text-text-primary">
            Lägg till notering
          </h3>
          <AddNoteForm applicationId={id} />
        </div>
      </section>
    </div>
  );
}
