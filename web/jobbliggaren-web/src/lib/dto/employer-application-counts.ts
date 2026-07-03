import { z } from "zod";

/**
 * #446 (#311) — per-arbetsgivare "tidigare ansökningar"-räknare för /jobb-korten.
 * Speglar backend `POST /api/v1/me/application-history/counts`-svaret: en
 * `countsByJobAdId`-map där NYCKELN är JobAdId (GUID) och VÄRDET är antalet av
 * den inloggade användarens EGNA tidigare (inskickade) ansökningar till annonsens
 * arbetsgivare (samma org.nr).
 *
 * POSITIVE-ONLY (paritet `jobAdMatchBatchSchema`): en annons finns i mappen ENBART
 * när räknaren är > 0 — frånvaro ⇒ ingen badge (FE behöver ingen "noll"-gren).
 * Nyckeln är JobAdId (icke-PII); org.nr färdas ALDRIG i svaret (server-side
 * GROUP-nyckel, ADR 0087 D8 / CLAUDE.md §5 — enskild firma = personnummer).
 *
 * `.default({}).catch({})` degraderar civilt (inga badges) vid anonymt/tomt svar
 * eller kontraktsdrift i stället för att krascha list-renderingen — samma mönster
 * som `getJobAdStatusBatch`/`getJobAdMatchTags`.
 */
export const employerApplicationCountBatchSchema = z.object({
  countsByJobAdId: z
    .record(z.string(), z.number().int().nonnegative())
    .default({})
    .catch({}),
});
export type EmployerApplicationCountBatch = z.infer<
  typeof employerApplicationCountBatchSchema
>;
