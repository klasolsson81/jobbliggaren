import { z } from "zod";

/**
 * ADR 0079 STEG 6 — Zod-mirror av backend `MyMatchCountDto`
 * (`Jobbliggaren.Application.Matching.Queries.GetMyMatchCount`). ADR 0020
 * single-source. `GET /api/v1/me/match-count` → `{ count: int }`.
 *
 * `count` = antalet aktiva annonser som matchar profilen i headline-grad-setet
 * (Bra + Stark / Good + Strong, Klas 2026-06-24). Per konstruktion samma
 * TotalCount som `/jobb?matchGrades=Good&matchGrades=Strong` landar på.
 * `count === 0` betyder antingen inget angivet yrke ELLER inga matchningar just
 * nu (båda honest — aldrig en fejkad mock-siffra; jfr `GetMyMatchCountQueryHandler`).
 */
export const myMatchCountSchema = z.object({
  count: z.number().int().nonnegative(),
});
export type MyMatchCount = z.infer<typeof myMatchCountSchema>;

/**
 * Headline-grad-setet som counten räknar över (ENUM-NAMN, ADR 0042 Beslut B
 * URL-kontrakt). Load-bearing trust-invariant: detta MÅSTE vara IDENTISKT med
 * backend `GetMyMatchCountQueryHandler.HeadlineGrades` ([Good, Strong]) — annars
 * visar notisen N men `/jobb?matchGrades=...` ett annat tal. Notisens länk byggs
 * från denna konstant (aldrig hårdkodade strängar inline) så counten och länken
 * inte kan drifta isär. Topp ingår aldrig (Fast-bandet, honest by design,
 * ADR 0076 G3-OPT-A) — rubriken är grad-neutral ("matchar din profil").
 */
export const OVERSIKT_MATCH_GRADES: ReadonlyArray<string> = ["Good", "Strong"];
