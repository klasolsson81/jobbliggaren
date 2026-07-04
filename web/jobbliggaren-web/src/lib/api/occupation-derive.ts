import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  occupationDerivationResultSchema,
  type OccupationDerivationResult,
} from "@/lib/dto/match-preferences";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";

/**
 * F4-12 PR-B (ADR 0076) — server-only BFF mot
 * `GET /api/v1/saved-searches/derive?title=<string>` (auth-gated, Bearer-
 * session, `SuggestPolicy`-rate-limit). Speglar `lib/api/taxonomy.ts`:
 * `ApiResult<T>`, Zod-validering vid ACL-gränsen, 401/429-mappning via
 * `responseToResult`, try/catch → `{kind:"error"}`.
 *
 * Härleder yrkesgrupp-kandidater ur en fritext-yrkestitel (deterministisk
 * taxonomi-lookup, ADR 0071 — INGEN AI). Resultatet är ENBART ett förslag
 * (propose-and-approve, ADR 0040 Beslut 4) som matchnings-kortet renderar
 * som en bekräfta-lista; ingenting skrivs förrän användaren sparar.
 *
 * Tom/blank titel → tom kandidatlista utan backend-rundtur (ingen DoS-yta,
 * symmetriskt med `resolveTaxonomyLabels`-tom-lista-grenen). Cache:
 * `no-store` — per-användar-/per-input-svar ska inte cachas.
 */
export async function deriveOccupations(
  title: string
): Promise<ApiResult<OccupationDerivationResult>> {
  const trimmed = title.trim();
  if (trimmed.length === 0) {
    return { kind: "ok", data: { title: "", candidates: [] } };
  }

  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  const params = new URLSearchParams({ title: trimmed });

  try {
    const res = await authedFetch(
      sessionId,
      `/api/v1/saved-searches/derive?${params.toString()}`
    );
    return await responseToResult(
      res,
      occupationDerivationResultSchema,
      "GET /api/v1/saved-searches/derive"
    );
  } catch {
    return { kind: "error" };
  }
}
