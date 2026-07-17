import type { JobAdSummaryDto } from "@/lib/dto/applications";

/**
 * #892 (CTO R1/R5): structural presentation facts for an application's ad
 * identity. An Erased ad rides the wire with the applicant's preserved
 * snapshot identity — or EMPTY identity when no snapshot exists (pre-#315) —
 * plus status "Erased". The FE renders from STRUCTURE (status + identity
 * presence): the "[raderad]" domain sentinel never reaches the wire, so no
 * component may literal-match it. Consumed by the list row, the board card,
 * the table row and the detail headers (the removed-ad marker is the other
 * half of the fix: restored real identity without a death signal would let
 * a dead ad look alive — R1).
 */
export function adIdentityOf(jobAd: JobAdSummaryDto | null | undefined): {
  /** True when the source ad is an Art. 17 tombstone (status "Erased"). */
  adRemoved: boolean;
  /** The renderable title, or null when absent (erased without snapshot). */
  title: string | null;
  /** The renderable company, or null when absent (erased without snapshot). */
  company: string | null;
} {
  if (jobAd == null) return { adRemoved: false, title: null, company: null };
  return {
    adRemoved: jobAd.status === "Erased",
    title: jobAd.title !== "" ? jobAd.title : null,
    company: jobAd.company !== "" ? jobAd.company : null,
  };
}
