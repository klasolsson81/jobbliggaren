import { useTranslations } from "next-intl";
import { JobAdCard } from "./job-ad-card";
import type { JobAdDto } from "@/lib/dto/job-ads";
import type { MatchGrade } from "@/lib/dto/job-ad-match";

interface JobAdListProps {
  jobAds: ReadonlyArray<JobAdDto>;
  /**
   * #293/#306 — NY = oläst (per-användar watermark). Set av annons-id:n med
   * `createdAt > lastSeenJobsAt`, beräknat i `JobbResults` mot den hämtade
   * watermarken. O(1)-lookup per kort (paritet med saved/applied-set:n).
   * Tomt/utelämnat = kall start / anon / ingen ny annons ⇒ ingen NY.
   */
  newIdSet?: ReadonlySet<string>;
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
  /**
   * #380 — nuvarande listans query-sträng (utan `?`), byggd i `JobbResults`.
   * Trådas oförändrat ner till varje `JobAdCard` så radlänken bär list-URL:ens
   * view-state in i modal-soft-naven (annars tappas filter/match-läget vid
   * öppna→stäng). Default tom = naken länk (gäst-/övriga ytor).
   */
  listQuery?: string;
}

export function JobAdList({
  jobAds,
  newIdSet,
  savedIdSet,
  appliedIdSet,
  matchGradeById,
  listQuery,
}: JobAdListProps) {
  // Synchronous next-intl translator — keeps JobAdList a non-async RSC.
  const t = useTranslations("jobads.ui");
  if (jobAds.length === 0) {
    // Ingen `role=status`/`aria-live` här — page.tsx har redan en live-region
    // på resultat-räknaren. Två live-regions samtidigt riskerar dubbel-
    // announcement (design-reviewer F2-P10 Minor 2). Empty-state-texten är
    // statiskt DOM-innehåll som läses upp vid navigation.
    return (
      <div className="jp-empty">
        <div className="jp-empty__title">{t("list.emptyTitle")}</div>
        {t("list.emptyBody")}
      </div>
    );
  }

  return (
    <ul className="jp-jobs" aria-label={t("list.ariaLabel")}>
      {jobAds.map((jobAd) => (
        <li key={jobAd.id}>
          <JobAdCard
            jobAd={jobAd}
            isNew={newIdSet?.has(jobAd.id) ?? false}
            isSaved={savedIdSet?.has(jobAd.id) ?? false}
            isApplied={appliedIdSet?.has(jobAd.id) ?? false}
            matchGrade={matchGradeById?.get(jobAd.id)}
            listQuery={listQuery}
          />
        </li>
      ))}
    </ul>
  );
}
