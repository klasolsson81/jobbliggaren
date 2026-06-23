import { z } from "zod";

/**
 * STEG 3 / ADR 0079 (Beslut 1) — skill DTOs for the CV-seeded, editable,
 * trusted match-preference skill chips. Two endpoints share one wire shape
 * (`{conceptId, label}`), so they share one Zod schema:
 *
 *  - `GET /api/v1/resumes/parsed/{id}/skills` → `SkillProposalDto[]`: the
 *    OWNER's CV-resolved skill proposals for a freshly-uploaded `parsed_resume`
 *    (propose-and-approve, ADR 0040 Beslut 4 — never written until the user
 *    saves). Owner-scoped + IDOR fail-closed in backend (unknown/cross-user/
 *    promoted → 404).
 *  - `GET /api/v1/job-ads/taxonomy/skills/search?q=<query>` → `SkillOptionDto[]`:
 *    the skill typeahead used by the "add" affordance (the flat 20k skill
 *    vocabulary has NO hierarchy, so a cascade does not apply — search instead).
 *
 * The skill taxonomy is NOT shipped to the FE as a tree, so unlike the
 * occupation/region dimensions there is no client-side label lookup for a
 * stale/seeded concept-id. Consumers therefore keep a `conceptId → label` map
 * built from proposals + search results, and fall back to the id when a label
 * is unknown (same graceful fallback as `labelsForSelected`).
 *
 * conceptId-format (1–32 chars, [A-Za-z0-9_-]) mirrors the backend
 * `SearchCriteria`/validator pattern (defense-in-depth; backend is the
 * authoritative barrier). `label` is non-empty.
 */

const conceptIdSchema = z.string().regex(/^[A-Za-z0-9_-]{1,32}$/);

export const skillOptionSchema = z.object({
  conceptId: conceptIdSchema,
  label: z.string().min(1),
});
export type SkillOption = z.infer<typeof skillOptionSchema>;

export const skillOptionsSchema = z.array(skillOptionSchema);

// `SkillProposalDto` and `SkillOptionDto` are wire-identical; one type alias
// keeps consumers honest that the two endpoints return the same shape.
export type SkillProposal = SkillOption;
export const skillProposalsSchema = skillOptionsSchema;
