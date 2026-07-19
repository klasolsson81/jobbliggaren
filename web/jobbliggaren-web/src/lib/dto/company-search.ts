import { z } from "zod";
import { pagedResult } from "@/lib/dto/_helpers";
import {
  companyBrowseSchema,
  criterionMagnitudeSchema,
} from "@/lib/dto/company-criteria";

/**
 * #560 PR-B — the general company-register SEARCH (`/foretag/sok`). Zod mirror of the backend
 * `CompanySearchResponse` served by `POST /api/v1/companies/search` (ADR 0020 single-source; backend
 * serialises camelCase). It reuses the criterion-browse row + magnitude shapes verbatim
 * (`companyBrowseSchema`, `criterionMagnitudeSchema`) — the same register rows, the same honest
 * magnitude ("10 000+" when saturated), never a new source of the same knowledge.
 *
 * The paginated page is validated with the PLAIN `pagedResult` (no `totalPages` requirement): the
 * pagination component consumes only page/pageSize/totalCount, and a tolerant schema neither breaks if
 * the backend omits `totalPages` nor fails on it if present (z.object ignores extra keys). As on the
 * criterion browse, `companies.totalCount` SATURATES at MaxServableRows (2000 at pageSize 20) and is a
 * pagination quantity ONLY — the honest headline number is `magnitude`.
 */
export const companySearchResponseSchema = z.object({
  companies: pagedResult(companyBrowseSchema),
  magnitude: criterionMagnitudeSchema,
});
export type CompanySearchResponse = z.infer<typeof companySearchResponseSchema>;

/**
 * The URL-driven search criteria the RSC page sends to the backend as a POST-as-read body. It carries
 * the three SHAREABLE axes (name prefix + SNI + kommun) and pagination — and, deliberately, NO
 * `organizationNumber` field. org.nr never enters this type: a sole-prop org.nr can equal a
 * personnummer (ADR 0087 D8(c)), so it never travels through the URL-reflected search. The org.nr
 * lookup is a separate BFF-only path (`searchCompanyByOrgNr`); keeping the two inputs disjoint at the
 * type level makes it impossible for the RSC search graph to forward an org.nr.
 */
export interface CompanySearchCriteria {
  readonly name?: string;
  readonly sniCodes: ReadonlyArray<string>;
  readonly municipalityCodes: ReadonlyArray<string>;
  readonly page: number;
  readonly pageSize: number;
}
