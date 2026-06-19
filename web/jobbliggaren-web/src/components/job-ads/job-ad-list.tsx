import { JobAdCard } from "./job-ad-card";
import type { JobAdDto } from "@/lib/dto/job-ads";
import type { MatchGrade } from "@/lib/dto/job-ad-match";

interface JobAdListProps {
  jobAds: ReadonlyArray<JobAdDto>;
  /**
   * PR5 (ADR 0063) — per-user-overlay-status. Set-storage så lookup är O(1)
   * per kort (`savedIdSet.has(jobAd.id)`). Tomma set:n = anonym/utan-auth
   * (chips visas inte).
   */
  savedIdSet?: ReadonlySet<string>;
  appliedIdSet?: ReadonlySet<string>;
  /**
   * F4-13 (ADR 0076) — graderad match-tagg per kort. Map<JobAdId, MatchGrade>
   * för O(1)-lookup (paritet med saved/applied-set:n). Saknad nyckel = ingen
   * positiv grad ⇒ ingen chip (POSITIVE-ONLY). Tom/utelämnad map = anonym
   * eller ingen match (chips visas inte).
   */
  matchGradeById?: ReadonlyMap<string, MatchGrade>;
}

export function JobAdList({
  jobAds,
  savedIdSet,
  appliedIdSet,
  matchGradeById,
}: JobAdListProps) {
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
          <JobAdCard
            jobAd={jobAd}
            isSaved={savedIdSet?.has(jobAd.id) ?? false}
            isApplied={appliedIdSet?.has(jobAd.id) ?? false}
            matchGrade={matchGradeById?.get(jobAd.id)}
          />
        </li>
      ))}
    </ul>
  );
}
