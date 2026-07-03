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

/**
 * Epik #526 (ADR 0088) — Zod-mirror av backend `MatchCountPreviewDto`
 * (`Jobbliggaren.Application.JobAds.Queries.GetMatchCountPreview`). ADR 0020
 * single-source. `POST /api/v1/me/match-count-preview` → `{ count: int }`.
 *
 * Live sök-preview-räknaren i matchnings-setup-modalen: antalet aktiva annonser
 * som matchar utkastets sök-facetter (yrke/ort/anställningsform). En ren
 * hard-filter-count (samma `ApplyFilter`-SPOT som `/jobb`) — INTE grad-matchning.
 * MEDVETET åtskild från `myMatchCountSchema` (sparad grad-match) — de mäter olika
 * frågor (ADR 0088). Kompetenser ingår inte i counten (kvalitet, ej sökfacett).
 */
export const draftMatchCountSchema = z.object({
  count: z.number().int().nonnegative(),
});
export type DraftMatchCount = z.infer<typeof draftMatchCountSchema>;

/**
 * Utkastet som live-räknaren skickar. De fyra sökbara dimensionerna (aldrig
 * kompetenser — de gallrar inte counten). Alla listor obligatoriska (tom = inget
 * filter på den dimensionen).
 */
export interface DraftMatchCountRequest {
  readonly occupationGroups: ReadonlyArray<string>;
  readonly regions: ReadonlyArray<string>;
  readonly municipalities: ReadonlyArray<string>;
  readonly employmentTypes: ReadonlyArray<string>;
}
