import type { CompanyWatch } from "@/lib/dto/company-follows";
import {
  buildCompanyJobsHref,
  isLinkableOrgNr,
} from "@/lib/job-ads/company-jobs-href";

/**
 * The numbers a surface states over ALL of a user's followed companies at once, and the two links
 * those numbers may carry. One derivation, read by every surface that sums the watches
 * (`CompanySummary` on the guest demo, the Bevakade företag card on `/oversikt`), so the
 * linkability rule and the "not assessed" silence cannot drift between them.
 */
export interface WatchSummary {
  /** Number of watches. */
  readonly count: number;
  /** Sum of `activeAdCount` over every watch. */
  readonly activeAds: number;
  /**
   * Sum of `matchingAdCount`, or `null` when at least one watch's count is not assessed. A null is
   * silence, never a zero: the DTO's null means "not assessed" (no stated occupation), and
   * rendering it as 0 would be a false measurement.
   */
  readonly matchingAds: number | null;
  /** Watches carrying a per-watch notification filter. */
  readonly filteredWatches: number;
  /** `/jobb` filtered to every watch's employer, scope "all" — or `null` when no link is honest. */
  readonly activeAdsHref: string | null;
  /** The same list, scope "matching" — or `null`. */
  readonly matchingAdsHref: string | null;
  /**
   * True when a link would otherwise have rendered but at least one watch cannot be linked, so
   * the surface owes the reader one sentence saying why the numbers carry no link.
   */
  readonly explainMissingLinks: boolean;
}

/**
 * @param surfaceCanLink `false` on a surface with no authenticated destination at all (the guest
 * demo): the ad links go to `/jobb`, an `(app)/` segment in PROTECTED_PREFIXES, so rendering
 * them there hands a visitor a link to `/logga-in`.
 */
export function summariseWatches(
  items: ReadonlyArray<CompanyWatch>,
  surfaceCanLink: boolean,
): WatchSummary {
  // The sum is exact because employer watches are disjoint BY CONSTRUCTION: the unique index
  // `ux_company_watches_user_orgnr_active` on (UserId, OrganizationNumber) yields one row per
  // employer and user, and an ad has one employer. The condition that breaks the invariant is a
  // `BrandGroup` watch — its row sums over its members and may therefore cover another row's
  // org.nr — and the DTO carries neither `targetType` nor `brandGroupId`, so the client cannot
  // detect such a row. That work lives in #1566.
  const activeAds = items.reduce((sum, w) => sum + w.activeAdCount, 0);

  // `some`, not `every`. The backend's SSYK gate is set once per request, so every watch is null
  // or none is — but should that gate ever break, the number goes silent rather than summing a
  // subset and silently under-counting. Do not simplify to `every`.
  const matchingNotAssessed = items.some((w) => w.matchingAdCount === null);
  const matchingAds = matchingNotAssessed
    ? null
    : items.reduce((sum, w) => sum + (w.matchingAdCount ?? 0), 0);

  const filteredWatches = items.filter((w) => w.filter !== null).length;

  // EVERY watch must be linkable or neither sum links. A masked sole-prop and a brand-group watch
  // both arrive with `organizationNumber: null`, and their ads would be missing from the
  // destination while the number beside the link still counted them — the count/click divergence
  // the route exists to avoid. Partial is worse than plain text here.
  const linkableOrgNrs = items.flatMap((w) =>
    !w.isProtectedIdentity &&
    w.organizationNumber &&
    isLinkableOrgNr(w.organizationNumber)
      ? [w.organizationNumber]
      : [],
  );
  const everyWatchLinkable = linkableOrgNrs.length === items.length;

  // A 0 is a negation, not a number, so it gets no link — parity with the watch row.
  const activeAdsHref =
    surfaceCanLink && everyWatchLinkable && activeAds > 0
      ? buildCompanyJobsHref(linkableOrgNrs, "all")
      : null;
  const matchingAdsHref =
    surfaceCanLink && everyWatchLinkable && matchingAds !== null && matchingAds > 0
      ? buildCompanyJobsHref(linkableOrgNrs, "matching")
      : null;

  // Owed only where a link would otherwise have rendered: an account whose watches have no ads at
  // all is not missing anything, so it stays quiet.
  const notLinkableCount = items.length - linkableOrgNrs.length;
  const explainMissingLinks =
    surfaceCanLink && notLinkableCount > 0 && (activeAds > 0 || (matchingAds ?? 0) > 0);

  return {
    count: items.length,
    activeAds,
    matchingAds,
    filteredWatches,
    activeAdsHref,
    matchingAdsHref,
    explainMissingLinks,
  };
}
