import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";
import {
  skillProposalGroupsSchema,
  skillOptionGroupsSchema,
  type SkillGroup,
} from "@/lib/dto/skills";
import { isValidId } from "@/lib/validation/guid";

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

/**
 * STEG 3 / ADR 0079 (Beslut 1) — the OWNER's CV-resolved skill proposals for a
 * PendingReview parsed CV (`GET /api/v1/resumes/parsed/{id}/skills`). Mirrors
 * `getParsedResumeOccupations`: server-only, auth-gated, owner-scoped + IDOR
 * fail-closed in backend (unknown/cross-user/promoted → 404, no enumeration
 * oracle). Lets the match-preference UI suggest skills from a freshly-uploaded
 * CV that has not yet been promoted to a `Resume`. The wire shape is already
 * `{conceptId, label}`, so no boundary remap is needed (unlike the occupation
 * proposal endpoint, whose wire shape differs from `OccupationCandidate`).
 *
 * The proposal is never written (propose-and-approve, ADR 0040/0071) — the user
 * confirms by keeping the chips and saving. Bearer session stays server-side.
 *
 * #277 — the wire shape is now a GROUP (`SkillProposalDto`:
 * `{conceptId, label, memberConceptIds}`), so an ESCO + AF twin-pair proposal
 * resolves to ONE `SkillGroup` chip carrying both member ids. The Zod transform
 * degrades a missing `memberConceptIds` to a singleton group (deploy-skew safe).
 */
export async function getParsedResumeSkills(
  id: string
): Promise<ApiResult<SkillGroup[]>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  // Allowlist-guard: reject non-GUID before it reaches the backend URL (SSRF
  // barrier + path-injection); a malformed id cannot exist → 404.
  if (!isValidId(id)) return { kind: "notFound" };

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/parsed/${encodeURIComponent(id)}/skills`,
      { headers: authHeaders(sessionId), cache: "no-store" }
    );
    return await responseToResult(
      res,
      skillProposalGroupsSchema,
      `GET /api/v1/resumes/parsed/${id}/skills`,
      { includeNotFound: true }
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * STEG 3 / ADR 0079 (Beslut 1) — skill typeahead against
 * `GET /api/v1/job-ads/taxonomy/skills/search?q=<query>` (auth-gated, Bearer
 * session, rate-limited; capped 20 server-side). The "add a skill" affordance:
 * the flat 20k skill vocabulary has NO hierarchy, so an option list from a
 * search box replaces the occupation cascade. Mirrors `deriveOccupations` /
 * `resolveTaxonomyLabels`: `ApiResult<T>`, Zod-validation at the ACL boundary,
 * 401/429 mapping via `responseToResult`, try/catch → `{kind:"error"}`.
 *
 * Blank/short query → empty list with no backend round-trip (no DoS surface,
 * symmetric with the `resolveTaxonomyLabels` empty-list branch); the backend
 * also returns [] for blank/short q. Cache: `no-store` — per-query response,
 * not cacheable.
 *
 * #277 — the wire shape is now a GROUP (`SkillOptionGroupDto`:
 * `{canonicalConceptId, label, memberConceptIds}`), so a search for "C#" yields
 * ONE addable `SkillGroup` carrying BOTH the ESCO + AF member ids. Adding the
 * chip stores all member ids on the flat full-replace save.
 */
export async function searchSkills(
  query: string
): Promise<ApiResult<SkillGroup[]>> {
  const trimmed = query.trim();
  // Min 2 chars: matches the component's debounce gate and the backend's
  // blank/short-query contract; avoids a useless round-trip on a single keypress.
  if (trimmed.length < 2) return { kind: "ok", data: [] };

  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  const params = new URLSearchParams({ q: trimmed });

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/job-ads/taxonomy/skills/search?${params.toString()}`,
      { headers: authHeaders(sessionId), cache: "no-store" }
    );
    return await responseToResult(
      res,
      skillOptionGroupsSchema,
      "GET /api/v1/job-ads/taxonomy/skills/search"
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * STEG 3 / ADR 0079 + ADR 0047 — reverse-lookup: skill-concept-id-lista →
 * `{conceptId, label}`. Mirrors `resolveTaxonomyLabels` (the occupation/region
 * reverse-lookup) so a returning settings user sees saved skill chips by NAME on
 * a COLD load instead of raw concept-ids (raw tokens must never reach a read
 * surface). The flat 20k skill vocabulary is never shipped to the FE as a tree,
 * so there is no client-side label lookup for a persisted id — the settings page
 * (a Server Component) resolves the labels here and seeds the chip label store.
 *
 * `GET /api/v1/job-ads/taxonomy/skills/labels?ids=<id>&ids=<id>...` →
 * `SkillOptionDto[]` (auth-gated via the group, rate-limited). Unknown ids are
 * DROPPED server-side (no enumeration oracle), so the result may be shorter than
 * the input; the consumer keeps the graceful id-fallback for any unresolved id.
 *
 * Empty id-list → empty list with no backend round-trip (no DoS surface,
 * symmetric with `resolveTaxonomyLabels`). Cache: `no-store` — the response
 * varies per ids and per auth, so it is not cacheable.
 *
 * #277 — the response is now GROUPED by shared exact-label surface
 * (`SkillOptionGroupDto`), so a saved twin-pair (both the ESCO + AF "C#" ids in
 * the user's PreferredSkills) resolves to ONE `SkillGroup` chip carrying both
 * member ids on cold load. No saved id is dropped (the BE groups only the KNOWN
 * ids; the consumer keeps the id-fallback for any unresolved id).
 */
export async function resolveSkillLabels(
  conceptIds: ReadonlyArray<string>
): Promise<ApiResult<SkillGroup[]>> {
  if (conceptIds.length === 0) return { kind: "ok", data: [] };

  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  const params = new URLSearchParams();
  // Repeated key per element (`?ids=a&ids=b`) — backend binds string[].
  for (const id of conceptIds) params.append("ids", id);

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/job-ads/taxonomy/skills/labels?${params.toString()}`,
      { headers: authHeaders(sessionId), cache: "no-store" }
    );
    return await responseToResult(
      res,
      skillOptionGroupsSchema,
      "GET /api/v1/job-ads/taxonomy/skills/labels"
    );
  } catch {
    return { kind: "error" };
  }
}
