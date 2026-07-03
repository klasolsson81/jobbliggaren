import { z } from "zod";

/**
 * ADR 0079 STEG 6, HARMONISERAD 2026-07-03 (Klas "samma siffra"; CTO-bind H2) —
 * Zod-mirror av backend `MyMatchCountDto`
 * (`Jobbliggaren.Application.Matching.Queries.GetMyMatchCount`). ADR 0020
 * single-source. `GET /api/v1/me/match-count` → `{ count: int }`.
 *
 * `count` = antalet aktiva annonser som matchar den SPARADE matchningens
 * sök-facetter (yrke ∧ ort ∧ anställningsform som hårda filter) — samma
 * `ApplyFilter`-SPOT som setup-modalens live-räknare, INGA grad-band. Per
 * konstruktion samma TotalCount som notisens facett-länk landar på (/jobb med
 * de sparade facetterna, inga matchGrades). Grad-bandet lever kvar som badges/
 * sort på /jobb — inte i den här siffran. `count === 0` betyder antingen inget
 * angivet yrke ELLER inga annonser för valen just nu (båda honest — aldrig en
 * fejkad mock-siffra; jfr `GetMyMatchCountQueryHandler`).
 */
export const myMatchCountSchema = z.object({
  count: z.number().int().nonnegative(),
});
export type MyMatchCount = z.infer<typeof myMatchCountSchema>;

/**
 * Epik #526 (ADR 0089) — Zod-mirror av backend `MatchCountPreviewDto`
 * (`Jobbliggaren.Application.JobAds.Queries.GetMatchCountPreview`). ADR 0020
 * single-source. `POST /api/v1/me/match-count-preview` → `{ count: int }`.
 *
 * Live sök-preview-räknaren i matchnings-setup-modalen: antalet aktiva annonser
 * som matchar utkastets sök-facetter (yrke/ort/anställningsform). En ren
 * hard-filter-count (samma `ApplyFilter`-SPOT som `/jobb`). Sedan H2-
 * harmoniseringen (2026-07-03) mäter den sparade notis-counten SAMMA fråga över
 * den sparade profilen — talen är per konstruktion identiska för samma val.
 * Kompetenser ingår inte i counten (kvalitet, ej sökfacett).
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
