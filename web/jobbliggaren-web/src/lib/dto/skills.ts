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
 *
 * #277 (twin chips) — the backend now GROUPS skill options by shared exact-label
 * surface: an ESCO "C#" + an AF "C#, programmeringsspråk" twin collapse into ONE
 * group whose `memberConceptIds` carries BOTH ids (a singleton carries exactly
 * one member = itself). The FE renders ONE chip per group; confirming/keeping it
 * stores ALL member ids; removing it removes ALL member ids. The user-confirmed
 * set + the PUT payload stay a FLAT `string[]` of all member ids (grade-inert).
 * The schemas below are ADDITIVE — the legacy flat `{conceptId, label}` is kept
 * for any non-grouped reader, and `memberConceptIds` is OPTIONAL so a deploy-skew
 * (older BE response without the field) degrades gracefully to `[conceptId]`.
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

/**
 * #277 — the normalized FE-internal skill GROUP shape. One group = one chip:
 * `conceptId` is the canonical (preferred-first) member id used as the chip's
 * stable React key + the id stored when the group is added; `label` is the chip
 * text; `memberConceptIds` is the FLAT set of ALL ids the surface co-produces
 * (always includes the canonical). The save payload is the flat union of every
 * selected group's `memberConceptIds` — the BE-provided membership is consumed
 * VERBATIM (the one deterministic source; the FE never re-derives membership
 * from raw taxonomy).
 */
export interface SkillGroup {
  readonly conceptId: string;
  readonly label: string;
  readonly memberConceptIds: ReadonlyArray<string>;
}

/**
 * #277 — search / resolve-labels wire group (`SkillOptionGroupDto`):
 * `{ canonicalConceptId, label, memberConceptIds }`. `memberConceptIds` is
 * optional (graceful degradation): a missing/empty value from an older BE
 * response is treated as a single-member group `[canonicalConceptId]` so a
 * deploy-skew never drops a saved id or crashes the read.
 */
export const skillOptionGroupSchema = z
  .object({
    canonicalConceptId: conceptIdSchema,
    label: z.string().min(1),
    memberConceptIds: z.array(conceptIdSchema).optional(),
  })
  .transform(
    (g): SkillGroup => ({
      conceptId: g.canonicalConceptId,
      label: g.label,
      // Graceful: absent/empty members → singleton group of the canonical id.
      // The canonical is always present (the BE includes it; the dedupe keeps
      // it first even if a future BE omits it from the member list).
      memberConceptIds: normalizeMembers(g.canonicalConceptId, g.memberConceptIds),
    })
  );

export const skillOptionGroupsSchema = z.array(skillOptionGroupSchema);

/**
 * #277 — CV proposal wire group (`SkillProposalDto`):
 * `{ conceptId, label, memberConceptIds }` (here `conceptId` IS the canonical).
 * Same optional-members graceful rule as the search/resolve group.
 */
export const skillProposalGroupSchema = z
  .object({
    conceptId: conceptIdSchema,
    label: z.string().min(1),
    memberConceptIds: z.array(conceptIdSchema).optional(),
  })
  .transform(
    (g): SkillGroup => ({
      conceptId: g.conceptId,
      label: g.label,
      memberConceptIds: normalizeMembers(g.conceptId, g.memberConceptIds),
    })
  );

export const skillProposalGroupsSchema = z.array(skillProposalGroupSchema);

/**
 * Graceful members-normalizer (shared by both group schemas): the canonical id
 * is guaranteed to lead, members are deduped, order is otherwise preserved
 * (determinism). An absent/empty list → `[canonical]` (deploy-skew degradation:
 * an older BE response without `memberConceptIds` becomes a singleton group).
 */
function normalizeMembers(
  canonical: string,
  members: ReadonlyArray<string> | undefined
): ReadonlyArray<string> {
  const seen = new Set<string>([canonical]);
  const ordered: string[] = [canonical];
  for (const id of members ?? []) {
    if (!seen.has(id)) {
      seen.add(id);
      ordered.push(id);
    }
  }
  return ordered;
}
