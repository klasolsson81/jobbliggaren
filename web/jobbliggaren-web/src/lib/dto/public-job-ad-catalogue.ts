import { z } from "zod";
import { isValidId } from "@/lib/validation/guid";
import { pagedResultWithTotalPages, readableInstantSchema } from "./_helpers";

/**
 * One row of the public catalogue (`GET /api/v1/public/job-ads/catalogue`, ADR 0176 F5): six members and no other.
 * Strict on purpose, unlike the job page's schema: a member the backend adds is a decision for ADR 0166 D10 trigger 10,
 * so it must stop the page rather than slip into it unread. `PublicJobAdCatalogueBoundaryTests` holds these keys equal
 * to `PublicJobAdSummaryDto`, so a drift fails a build and never a production page.
 */
export const publicJobAdSummarySchema = z
  .object({
    id: z.string().refine(isValidId),
    title: z.string(),
    companyName: z.string(),
    municipalityName: z.string().nullable(),
    regionName: z.string().nullable(),
    publishedAt: readableInstantSchema,
  })
  .strict();
export type PublicJobAdSummary = z.infer<typeof publicJobAdSummarySchema>;

/** One catalogue page: the five keys of `PagedResult<T>` and no stamp. `totalPages` is 0 on an empty corpus. */
export const publicJobAdCataloguePageSchema = pagedResultWithTotalPages(publicJobAdSummarySchema).strict();
export type PublicJobAdCataloguePage = z.infer<typeof publicJobAdCataloguePageSchema>;
