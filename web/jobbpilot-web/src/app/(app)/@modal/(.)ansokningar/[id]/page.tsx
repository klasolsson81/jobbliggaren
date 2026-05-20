import { notFound, redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getApplicationById } from "@/lib/api/applications";
import { ApplicationDetail } from "@/components/applications/application-detail";
import { ApplicationModalShell } from "@/components/applications/application-modal-shell";
import { WithdrawApplicationButton } from "@/components/applications/withdraw-application-button";
import { getAllowedTransitions } from "@/lib/applications/status";

interface PageProps {
  params: Promise<{ id: string }>;
}

/**
 * Intercepting Route för @modal-slotten. `(.)ansokningar/[id]` matchar
 * samma segment-nivå som slot-monteringspunkten `(app)` — `@modal` är en
 * slot, INTE ett route-segment, så `ansokningar` ligger en segment-nivå upp
 * trots två fil-nivåer (Next-docs Intercepting Routes §Convention + §Modals,
 * verifierat node_modules/next/dist/docs Next 16.2.x — "the `(..)`
 * convention is based on route segments, not the file-system … does not
 * consider `@slot` folders"). Identiskt mönster med F3
 * `@modal/(.)jobb/[id]` (ADR 0053).
 *
 * Soft-nav (radklick → Link /ansokningar/[id]) fångas här → modal.
 * Hard-nav / refresh / delad länk träffar `/ansokningar/[id]/page.tsx`
 * (fullsida). Samma `getApplicationById` + `ApplicationDetail` i båda
 * (ADR 0053, DRY).
 *
 * RSC: server-fetch här; endast modal-chromet (ApplicationModalShell) +
 * mutationsformulären är "use client". ApplicationDetail-trädet förblir
 * Server Component (passeras som children — serialiserbart RSC-träd, ingen
 * funktion över gränsen). WithdrawApplicationButton är en "use client"-ö
 * som passeras som footer-children (server-renderat träd, ej funktion-prop).
 */
export default async function InterceptedAnsokanModal({ params }: PageProps) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const { id } = await params;
  const result = await getApplicationById(id);

  switch (result.kind) {
    case "ok": {
      const application = result.data;
      const jobAd = application.jobAd ?? null;
      const shortId = application.id.slice(0, 8);
      const hasIdentity = jobAd != null;
      const title = hasIdentity
        ? jobAd.title
        : `Ansökan #${shortId}`;
      const subtitle = hasIdentity
        ? `${jobAd.company} · #${shortId}`
        : `#${shortId}`;
      const canWithdraw = getAllowedTransitions(
        application.status
      ).includes("Withdrawn");

      return (
        <ApplicationModalShell
          title={title}
          subtitle={subtitle}
          mono={!hasIdentity}
          footer={
            canWithdraw ? (
              <WithdrawApplicationButton
                applicationId={application.id}
                currentStatus={application.status}
              />
            ) : null
          }
        >
          <ApplicationDetail application={application} headless />
        </ApplicationModalShell>
      );
    }
    case "unauthorized":
      redirect("/logga-in");
    case "notFound":
      notFound();
    case "rateLimited":
      return (
        <ApplicationModalShell title="För många förfrågningar" subtitle="">
          <div className="jp-modal__body">
            <p className="text-body-sm text-text-secondary">
              Du har gjort för många förfrågningar på kort tid. Försök igen
              om {result.retryAfterSeconds} sekunder.
            </p>
          </div>
        </ApplicationModalShell>
      );
    case "forbidden":
    case "error":
      return (
        <ApplicationModalShell title="Kunde inte ladda ansökan" subtitle="">
          <div className="jp-modal__body">
            <p className="text-body-sm text-text-secondary">
              Ett tekniskt fel uppstod. Försök igen om en stund.
            </p>
          </div>
        </ApplicationModalShell>
      );
  }
}
