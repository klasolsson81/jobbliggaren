import { z } from "zod";
import { CANONICAL_GUID_REGEX } from "@/lib/validation/guid";

/**
 * One job-sitemap part from `GET /api/v1/public/job-ads/listed-ids` (ADR 0174): ids and nothing else, each in the
 * canonical lower-case form, because each becomes a `loc` that must equal the page's canonical. The architecture test
 * `ListedJobAdSnapshotBoundaryTests` holds the key set equal to the API's DTO.
 */
export const publicJobAdIdsSchema = z.strictObject({
  ids: z.array(z.string().regex(CANONICAL_GUID_REGEX)),
});

export type PublicJobAdIds = z.infer<typeof publicJobAdIdsSchema>;
