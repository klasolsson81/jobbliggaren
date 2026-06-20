import { notFound, redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getJobAd } from "@/lib/api/job-ads";
import { isJobAdSaved } from "@/lib/api/saved-job-ads";
import { hasAppliedJobAd } from "@/lib/api/job-ad-status";
import { getJobAdMatchDetail } from "@/lib/api/job-ad-match";
import { JobAdDetail } from "@/components/job-ads/job-ad-detail";
import { JobAdModalShell } from "@/components/job-ads/job-ad-modal-shell";

interface PageProps {
  params: Promise<{ id: string }>;
}

/**
 * Intercepting Route för @modal-slotten. `(.)jobb/[id]` matchar samma
 * segment-nivå som slot-monteringspunkten `(app)` — `@modal` är en slot,
 * INTE ett route-segment, så `jobb` ligger en segment-nivå upp trots två
 * fil-nivåer (Next-docs Intercepting Routes §Convention + §Modals,
 * verifierat node_modules/next/dist/docs Next 16.2.x).
 *
 * Soft-nav (radklick → Link /jobb/[id]) fångas här → modal. Hard-nav /
 * refresh / delad länk träffar `/jobb/[id]/page.tsx` (fullsida). Samma
 * `getJobAd` + `JobAdDetail` i båda (ADR 0053, DRY).
 *
 * RSC: server-fetch här; endast modal-chromet (JobAdModalShell) är
 * "use client". JobAdDetail-trädet förblir Server Component (passeras
 * som children — serialiserbart RSC-träd, ingen funktion över gränsen).
 */
export default async function InterceptedJobbModal({ params }: PageProps) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const { id } = await params;
  const result = await getJobAd(id);

  switch (result.kind) {
    case "ok": {
      // F4-16 — matchnings-detalj parallellt med Spara/Har-ansökt (ingen
      // waterfall). Degraderar civilt till null (ingen sektion) vid fel.
      const [initialSaved, initialApplied, match] = await Promise.all([
        isJobAdSaved(id),
        hasAppliedJobAd(id),
        getJobAdMatchDetail(id),
      ]);
      return (
        <JobAdModalShell
          title={result.data.title}
          company={result.data.companyName}
        >
          <JobAdDetail
            jobAd={result.data}
            headless
            initialSaved={initialSaved}
            initialApplied={initialApplied}
            match={match}
          />
        </JobAdModalShell>
      );
    }
    case "unauthorized":
      redirect("/logga-in");
    case "notFound":
      notFound();
    case "rateLimited":
      return (
        <JobAdModalShell title="För många förfrågningar" company="">
          <div className="jp-modal__body">
            <p className="text-body-sm text-text-secondary">
              Du har gjort för många förfrågningar på kort tid. Försök igen om{" "}
              {result.retryAfterSeconds} sekunder.
            </p>
          </div>
          <div className="jp-modal__foot">
            <span className="jp-modal__foot__spacer" />
          </div>
        </JobAdModalShell>
      );
    case "forbidden":
    case "error":
      return (
        <JobAdModalShell title="Kunde inte ladda annonsen" company="">
          <div className="jp-modal__body">
            <p className="text-body-sm text-text-secondary">
              Ett tekniskt fel uppstod. Försök igen om en stund.
            </p>
          </div>
          <div className="jp-modal__foot">
            <span className="jp-modal__foot__spacer" />
          </div>
        </JobAdModalShell>
      );
  }
}
