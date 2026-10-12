import { z } from "zod";
import { isValidId } from "@/lib/validation/guid";
import { jobAdUrlSchema } from "./job-ads";
import { readableInstantSchema } from "./_helpers";

/**
 * The anonymous public job-ad read (`GET /api/v1/public/job-ads/{id}`, ADR 0160 D4), and nothing more
 * of it than the page renders.
 *
 * The wire carries twelve members. This schema names the seven the page uses and strips the rest, so a
 * field the backend adds later cannot reach the page by accident: the five concept ids stay unread on
 * purpose (ADR 0160 D7: labels are S3's and S6's), and there is no user state, contact block, organisation
 * number or raw payload on that wire to begin with. The backend pins its member set
 * (`PublicJobAdDtoContractTests`); this side pins what it reads.
 *
 * `url` goes through the same http(s) allow-list as the signed-in detail, because it ends up in an `href`.
 * An answer that does not parse is an error, never an empty page (`getPublicJobAd` throws on it).
 */
export const publicJobAdDtoSchema = z.object({
  id: z.string().refine(isValidId),
  title: z.string(),
  companyName: z.string(),
  description: z.string(),
  url: jobAdUrlSchema,
  publishedAt: readableInstantSchema,
  expiresAt: readableInstantSchema.nullable(),
});
export type PublicJobAdDto = z.infer<typeof publicJobAdDtoSchema>;
