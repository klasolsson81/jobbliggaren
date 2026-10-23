import { z } from "zod";
import { isValidId } from "@/lib/validation/guid";
import { jobAdUrlSchema } from "./job-ads";
import { readableInstantSchema } from "./_helpers";

/**
 * The anonymous public job-ad read (`GET /api/v1/public/job-ads/{id}`, ADR 0160 D4), and nothing more
 * of it than the page renders.
 *
 * The wire carries fifteen members. This schema names the twelve the page uses and strips the rest, so
 * a field the backend adds later cannot reach the page by accident. Kommun, län and the occupation group
 * are read as their snapshot names only (ADR 0171; a name is null when the snapshot does not know the id),
 * and their concept ids stay unread: nothing on the page renders them. The group's name is read for the
 * facts row alone and is never part of the `JobPosting` (#2203). Unlike its siblings it is optional: the API
 * always sends it, but `job-posting-json-ld.test.ts` is held byte-unchanged as the evidence that the
 * `JobPosting` never carries it, and that file's fixture predates the member. A wire without it shows no
 * group. The employment-type and worktime-extent ids are read as plain strings and named by the locale
 * catalogue (`coded-taxonomy.ts`), never enum-validated, so an id the catalogue does not know (taxonomy
 * drift) parses and is left out of the page and its `JobPosting`, and never makes the page fail. There is
 * no user state, contact block, organisation number or raw payload on that wire to begin with. The backend
 * pins its member set (`PublicJobAdDtoContractTests`); this side pins what it reads.
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
  municipalityName: z.string().nullable(),
  regionName: z.string().nullable(),
  occupationGroupName: z.string().nullable().optional(),
  employmentTypeConceptId: z.string().nullable(),
  worktimeExtentConceptId: z.string().nullable(),
});
export type PublicJobAdDto = z.infer<typeof publicJobAdDtoSchema>;
