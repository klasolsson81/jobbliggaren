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

/**
 * De FYRA positiva graderna. `Top` är den golden-rungen F4-16 (ADR 0076
 * Amendment (b) §5) tänder VISUELLT: occupation + region + anställning bekräftade
 * OCH CV-kompetenser överlappar annonsens krav. Mappas 1:1 mot
 * `.jp-matchchip --high/--mid/--low/--top`.
 *
 * KRITISKT (F4-16): batch-backend EMITTERAR nu "Top". `jobAdMatchBatchSchema`
 * `.record` `.catch({})`:ar HELA mappen till tomt vid okänd grad — så denna
 * widening MÅSTE skeppa atomiskt med backend, annars blankas ALLA taggar på
 * sidan (CTO D2 page-wipe-fällan). Backend serialiserar by NAME.
 */
export const matchGradeSchema = z.enum(["Strong", "Good", "Basic", "Top"]);
export type MatchGrade = z.infer<typeof matchGradeSchema>;

/**
 * STEG 5 (grade-filter, 2026-06-23) — de grader /jobb-listfiltret kan filtrera
 * på. EXAKT `Basic` | `Good` | `Strong` — `Top` är medvetet UTESLUTET: listans
 * grade-filter är Fast-bandet och kan inte beräkna Toppmatch (honest by design;
 * backend-validatorn 400:ar `Top`). Wire-formen är enum-NAMN (svenska labels
 * Grund/Bra/Stark lever bara i UI). Ordningen är Goodhart-medvetet ordinal
 * (Grund → Bra → Stark) men listan poängsätter aldrig — den filtrerar på
 * namngivna kategorier. `as const` så `LIST_MATCH_GRADES.includes` ger en
 * bekväm typvakt i page-validatorn (drop unknown/Top tyst).
 */
export const LIST_MATCH_GRADES = ["Basic", "Good", "Strong"] as const;
export type ListMatchGrade = (typeof LIST_MATCH_GRADES)[number];

/** Typvakt: är strängen en av de tre filtrerbara graderna (ej `Top`)? */
export function isListMatchGrade(value: string): value is ListMatchGrade {
  return (LIST_MATCH_GRADES as ReadonlyArray<string>).includes(value);
}

/**
 * Ordinalt delverdikt per matchnings-dimension. `NotAssessed` = CV-sidan saknas
 * (inget CV) → kunde inte bedömas. `Vacuous` (ADR 0076 amendment 2026-06-20) =
 * ad-sidan saknar termer av den här sorten MEN CV finns ("annonsen anger inga") —
 * skilt från `NotAssessed`, och bärande för den requirement-aware graden (en
 * annons utan skallkrav är gate-öppen). Mis-rapporteras aldrig (CLAUDE.md §5).
 *
 * KRITISKT: modal-detalj-DTO:n (`matchDimensionDetailSchema`) parsar `verdict`
 * STRIKT — `Vacuous` MÅSTE finnas här atomiskt med backend som emitterar det,
 * annars kastar `jobAdMatchDetailSchema.parse` och modal-hämtningen failar.
 * (Batch-entryt strippar tyst de tre Full-verdikten, så batch-taggen påverkas ej.)
 */
export const matchVerdictSchema = z.enum([
  "Match",
  "Partial",
  "NoMatch",
  "NotAssessed",
  "Vacuous",
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

/**
 * F4-16 (ADR 0076 Amendment (b) §3, CTO D3) — detalj-altitud för EN annons,
 * konsumerad av modal/fullsida-matchningssektionen. Skild DTO från batch:en
 * (REP/CCP/CRP): batch:en utelämnar matched/missing-strängarna medvetet
 * (list-altitud), detaljen bär dem (förklaringslagret). En rad per
 * matchnings-dimension: ordinalt verdict + bevis (matched = "Du har:",
 * missing = "Annonsen efterfrågar även:").
 *
 * matched/missing är Display-labels för JobTech-koncept-id:n + Snowball-stems —
 * INTE rå CV-prosa (CTO D3 (d), security-auditor-bekräftad). Goodhart-vakt
 * (ADR 0053 Beslut 5 + ADR 0076): INGEN siffra/procent/mätare i kontraktet.
 */
export const matchDimensionDetailSchema = z.object({
  verdict: matchVerdictSchema,
  matched: z.array(z.string()),
  missing: z.array(z.string()),
});
export type MatchDimensionDetail = z.infer<typeof matchDimensionDetailSchema>;

/**
 * Modal-/fullsida-detaljsvaret. `grade` är `null` när annonsen inte tjänar in
 * en positiv tagg (yrket matchade inte) — raderna finns ändå (ärlig
 * nedbrytning). Hela svaret kan vara `null` (200 med `null`-body =
 * "ingen matchnings-sektion": anonym / ingen träffdata).
 */
export const jobAdMatchDetailSchema = z.object({
  grade: matchGradeSchema.nullable(),
  ssykOverlap: matchDimensionDetailSchema,
  titleSimilarity: matchDimensionDetailSchema,
  regionFit: matchDimensionDetailSchema,
  employmentFit: matchDimensionDetailSchema,
  skillOverlap: matchDimensionDetailSchema,
  mustHaveCoverage: matchDimensionDetailSchema,
  niceToHaveCoverage: matchDimensionDetailSchema,
});
export type JobAdMatchDetail = z.infer<typeof jobAdMatchDetailSchema>;
