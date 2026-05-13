import { JobAdCard } from "./job-ad-card";
import type { JobAdDto } from "@/lib/dto/job-ads";

interface JobAdListProps {
  jobAds: ReadonlyArray<JobAdDto>;
}

export function JobAdList({ jobAds }: JobAdListProps) {
  if (jobAds.length === 0) {
    // Ingen `role=status`/`aria-live` här — page.tsx har redan en live-region
    // på resultat-räknaren. Två live-regions samtidigt riskerar dubbel-
    // announcement (design-reviewer F2-P10 Minor 2). Empty-state-texten är
    // statiskt DOM-innehåll som läses upp vid navigation.
    return (
      <div className="rounded-md border border-border bg-surface-secondary px-6 py-10 text-center">
        <p className="text-body text-text-secondary">Inga jobb hittades</p>
        <p className="mt-1 text-body-sm text-text-secondary">
          Justera filtren eller töm sökrutan för att se fler annonser.
        </p>
      </div>
    );
  }

  return (
    <ul className="flex flex-col gap-3" aria-label="Jobbannonser">
      {jobAds.map((jobAd) => (
        <li key={jobAd.id}>
          <JobAdCard jobAd={jobAd} />
        </li>
      ))}
    </ul>
  );
}
