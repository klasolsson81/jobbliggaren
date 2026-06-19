import { z } from "zod";

/**
 * F4-13 (ADR 0076) — graderad match-tagg per /jobb-kort. Speglar backend
 * `POST /api/v1/me/job-ad-match-tags`-svaret: en `entries`-map där NYCKELN är
 * JobAdId (GUID) och VÄRDET är annonsens graderade verdict. En annons finns i
 * mappen ENBART om den tjänade in en positiv tagg (yrket matchade) — frånvaro
 * ⇒ ingen tagg (POSITIVE-ONLY, design-reviewer 2026-06-19 villkor 7).
 *
 * Enum:arna serialiseras by NAME (sträng) på denna endpoint (verifierat live) —
 * därför `z.enum([...strängar])`, inte int-mappning (kontrast mot
 * `match-preferences.ts` derive-svaret som skickar int och därför utelämnar
 * fältet). Goodhart-vakt (CTO-linje + ADR 0076): INGEN siffra/procent finns i
 * kontraktet — endast en namngiven grad + fyra ordinala delverdikt. FE visar
 * bara graden; delverdikten bär förklaringslagret som först ytas i F4-16.
 */

/** De tre POSITIVA graderna. Mappas 1:1 mot `.jp-matchchip --high/--mid/--low`. */
export const matchGradeSchema = z.enum(["Strong", "Good", "Basic"]);
export type MatchGrade = z.infer<typeof matchGradeSchema>;

/**
 * Ordinalt delverdikt per matchnings-dimension. `NotAssessed` = dimensionen
 * kunde inte bedömas v1 (markeras ärligt, mis-rapporteras aldrig — CLAUDE.md §5
 * CV/matching-regeln). Konsumeras inte visuellt i F4-13-listan (förklarings-
 * lagret är F4-16); parsas för kontrakts-trohet och framtida bruk.
 */
export const matchVerdictSchema = z.enum([
  "Match",
  "Partial",
  "NoMatch",
  "NotAssessed",
]);
export type MatchVerdict = z.infer<typeof matchVerdictSchema>;

/** En annons graderade verdict (grad + de fyra delverdikten). */
export const jobAdMatchEntrySchema = z.object({
  grade: matchGradeSchema,
  ssykOverlap: matchVerdictSchema,
  titleSimilarity: matchVerdictSchema,
  regionFit: matchVerdictSchema,
  employmentFit: matchVerdictSchema,
});
export type JobAdMatchEntry = z.infer<typeof jobAdMatchEntrySchema>;

/**
 * Batch-svaret. `entries` defaultar till `{}` (anonym/tom batch) och `.catch`:ar
 * till `{}` vid kontraktsdrift — degraderar civilt (inga taggar visas) i stället
 * för att krascha list-renderingen, paritet med `getJobAdStatusBatch`-mönstret.
 */
export const jobAdMatchBatchSchema = z.object({
  entries: z
    .record(z.string(), jobAdMatchEntrySchema)
    .default({})
    .catch({}),
});
export type JobAdMatchBatch = z.infer<typeof jobAdMatchBatchSchema>;
