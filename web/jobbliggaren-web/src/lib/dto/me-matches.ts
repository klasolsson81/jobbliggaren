import { z } from "zod";

/**
 * ADR 0080 Vag 4 PR-5 — Zod-mirror av de tre "Mina matchningar"-DTO:erna
 * (`Jobbliggaren.Application.Matching.Queries.GetMyMatches`/`GetMyNewMatchCount`).
 * ADR 0020 single-source: wire-formen valideras vid ACL-gränsen, datum hålls som
 * `z.string()` (UI-formatering är presentationsansvar, jfr `_helpers`).
 *
 * SKILD från `job-ad-match.ts` (REP/CCP): det är /jobb-listans batch-overlay
 * (fyra positiva grader, inkl. `Basic`), detta är den PERSISTERADE
 * bakgrundsmatchningen (NotifiableMatchGrade = Good/Strong/Top — `Basic` blir
 * aldrig en notifierbar match, ADR 0080 Beslut 6). Två olika koncept, två filer.
 */

/**
 * De TRE notifierbara graderna (backend `NotifiableMatchGrade`, serialiserad by
 * NAME). MÅSTE matcha enum:en atomiskt: `Basic` finns medvetet INTE här (en
 * `Basic`-träff persisteras aldrig som match). Ordinal Good → Strong → Top, men
 * INGEN siffra/poäng yttas någonsin (Goodhart-vakt, ADR 0071/0076/0080) — graden
 * är en namngiven kategori. Svenska labels (Bra/Stark/Topp) lever bara i UI.
 */
export const notifiableMatchGradeSchema = z.enum(["Good", "Strong", "Top"]);
export type NotifiableMatchGrade = z.infer<typeof notifiableMatchGradeSchema>;

/**
 * `GET /api/v1/me/new-match-count` → `{ count: int }`. Antalet
 * bakgrundsmatchningar nya sedan senaste besök (CreatedAt > LastSeenMatchesAt).
 * `count === 0` är honest (ingen användare/JobSeeker/inget nytt) — Översikts-
 * raden renderar då bara "0", aldrig en mock-siffra. Grad-neutral (aldrig en
 * magnitud).
 */
export const newMatchCountSchema = z.object({
  count: z.number().int().nonnegative(),
});
export type NewMatchCount = z.infer<typeof newMatchCountSchema>;

/**
 * En rad i den dedikerade "Mina matchningar"-vyn (`GET /api/v1/me/matches`).
 * `url` är annonsens externa länk (kan saknas → fall tillbaka till /jobb/{id}).
 * `isNew` speglar last-seen-vattenmärket VID HÄMTNING (även om öppningen av vyn
 * avancerar märket) så vyn kan markera vad som kommit sedan förra besöket.
 */
export const matchListItemSchema = z.object({
  jobAdId: z.string(),
  title: z.string(),
  company: z.string(),
  url: z.string().nullable(),
  grade: notifiableMatchGradeSchema,
  createdAt: z.string(),
  isNew: z.boolean(),
});
export type MatchListItem = z.infer<typeof matchListItemSchema>;

/** Hela list-svaret: nyast först, cap:at 50 av backend. */
export const matchListSchema = z.array(matchListItemSchema);
export type MatchList = z.infer<typeof matchListSchema>;
