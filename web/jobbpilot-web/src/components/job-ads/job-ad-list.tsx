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
      <div className="jp-empty">
        <div className="jp-empty__title">Inga jobb hittades</div>
        Justera filtren eller töm sökrutan för att se fler annonser.
      </div>
    );
  }

  return (
    <ul className="jp-jobs" aria-label="Jobbannonser">
      {jobAds.map((jobAd) => (
        <li key={jobAd.id}>
          <JobAdCard jobAd={jobAd} />
        </li>
      ))}
    </ul>
  );
}
