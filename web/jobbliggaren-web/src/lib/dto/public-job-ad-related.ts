import { z } from "zod";
import { publicJobAdSummarySchema } from "./public-job-ad-catalogue";

/**
 * The related ads under a public job page (`GET /api/v1/public/job-ads/{id}/related`, ADR 0182): one member, `items`,
 * whose rows are the catalogue's six-member row. Strict at both levels for the catalogue's reason: a member the backend
 * adds is a decision for ADR 0166 D10 trigger 15, so it must drop the section rather than slip into it unread.
 * `PublicJobAdRelatedBoundaryTests` holds the envelope to `RelatedPublicJobAdsDto`.
 */
export const publicJobAdRelatedSchema = z.object({ items: z.array(publicJobAdSummarySchema) }).strict();
export type PublicJobAdRelated = z.infer<typeof publicJobAdRelatedSchema>;
