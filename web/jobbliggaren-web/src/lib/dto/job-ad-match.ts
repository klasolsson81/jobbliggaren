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
 *
 * #300 PR-5 (ADR 0084): `Related` ("Relaterat yrke") är den femte rungen MELLAN
 * Basic och Good. Backend emitterar den nu (bakom `includeRelated`-flaggan, off
 * by default) — `Related` MÅSTE därför finnas i enumet, annars `.catch({})`:ar
 * batch-mappen HELA sidan till tomt så fort en related-graderad annons dyker upp
 * (samma page-wipe-fälla som Top). Ordningen i enumet spelar ingen roll (enumet
 * är bara accept-listan); ordinaliteten lever i `LIST_MATCH_GRADES`.
 */
export const matchGradeSchema = z.enum(["Strong", "Good", "Basic", "Top", "Related"]);
export type MatchGrade = z.infer<typeof matchGradeSchema>;

/**
 * Grad-taxonomin (issue #291 — SSOT-dokumentation av de fyra nivåerna + var de
 * lever). Det finns FYRA grader, men listfiltret kan bara erbjuda TRE.
 *
 * Nivåer (svag → stark), svenska labels från `match.grade.*`:
 *   Related = "Relaterat yrke" — ett yrke som LIKNAR ett du valt (ADR 0084
 *             substituerbarhet), inte ett du valt exakt. Rankas UNDER Basic.
 *   Basic   = "Grundmatch"  — yrket matchar, men inga bekräftade sekundärsignaler.
 *   Good    = "Bra match"   — yrket + minst en av region/anställning bekräftad.
 *   Strong  = "Stark match" — du möter annonsens ska-krav (requirement-backed).
 *   Top     = "Toppmatch"   — Stark PLUS att CV-kompetenser/meriterande överlappar.
 *
 * Varför filtret bara har Grund/Bra/Stark (Top UTESLUTET, honest by design):
 * listans grad-filter + sort kör FAST-BANDET (preferens-byggt: yrke + region +
 * anställning, DEK-fritt och cachebart, ADR 0045 300 ms-budget). Det bandet kan
 * INTE beräkna Toppmatch — Top kräver CV-kompetenser mot annonsens krav på den
 * FULLA, DEK-värmda per-kort-vägen. Backend-validatorn 400:ar därför `Top` som
 * filtervärde. Toppmatch syns som badge på kort + i modalen, aldrig som
 * filter-kryssruta.
 *
 * Samma vokabulär filter ↔ badge ↔ modal (issue #291 AC): `gradeFilter.grade.*`
 * (filtret) och `match.grade.*` (badge/modal, via `MatchChip`) bär IDENTISKA ord
 * för Basic/Good/Strong. Likheten pinnas av ett drift-guard-test
 * (`jobads-parity.test.ts`) så de aldrig glider isär igen (regressionen som
 * öppnade #291).
 *
 * Fast/Full-divergens (ADR 0076 §4 G3-OPT-A / #298-beslut (iii)): filtret
 * smalnar på Fast-bandet medan badgen visar den requirement-aware Fulla graden.
 * Det är två KOHERENTA men skilda axlar — att avmarkera en grad döljer därför
 * inte garanterat varje kort som visar just det ordet. Accepterat och
 * dokumenterat; `gradeFilter.help` lovar medvetet ALDRIG exakt göm (ingen
 * BE-ändring per (iii)).
 *
 * #300 PR-5 (ADR 0084, Accepted 2026-06-28) — den femte rungen `Related`
 * ("Relaterat yrke") är nu LANDAD och placeras ordinalt MELLAN Basic och Good
 * (den rankas svagast: ett liknande, inte exakt valt, yrke). `Related` ÄR
 * Fast-beräkningsbar (till skillnad från Top) och är därför en filtrerbar grad —
 * MEN bara bakom "Visa relaterade också"-toggle:n (`?relaterade=on`, off by
 * default). Den ingår alltså i `LIST_MATCH_GRADES` (SPOT för ordinaliteten +
 * filter-kryssrutornas ordning), men det SYNLIGA/effektiva grad-setet i filtret
 * inkluderar `Related` ENBART när toggle:n är på (jobb-match-grade-filter.tsx
 * härleder det). `gradeFilter.help` lovar fortfarande aldrig per-kort-exakthet.
 *
 * Wire-formen är enum-NAMN (svenska labels lever bara i UI). `as const` så
 * `LIST_MATCH_GRADES.includes` ger en bekväm typvakt i page-validatorn (drop
 * unknown/Top tyst). Ordningen är Goodhart-medvetet ordinal men listan
 * poängsätter aldrig — den filtrerar på namngivna kategorier.
 */
export const LIST_MATCH_GRADES = ["Basic", "Related", "Good", "Strong"] as const;
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
