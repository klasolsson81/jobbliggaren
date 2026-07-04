import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  matchListSchema,
  newMatchCountSchema,
  type MatchList,
  type NewMatchCount,
} from "@/lib/dto/me-matches";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";

/**
 * ADR 0080 Vag 4 PR-5 — antalet bakgrundsmatchningar NYA sedan senaste besök,
 * för Översikts "Nya matchningar"-rad. Konsumerar `GET /api/v1/me/new-match-count`
 * (auth-gated, MeListReadPolicy). Speglar `getMatchCount` Result/fel-mönstret
 * (ADR 0030): unauthorized / rateLimited / error.
 *
 * `count === 0` är ett honest svar (ingen ny match sedan förra besöket) —
 * Översikts-raden renderar 0, aldrig en mock-siffra.
 */
export async function getNewMatchCount(): Promise<ApiResult<NewMatchCount>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, "/api/v1/me/new-match-count");
    return await responseToResult(
      res,
      newMatchCountSchema,
      "GET /api/v1/me/new-match-count"
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * ADR 0080 Vag 4 PR-5 — användarens persisterade bakgrundsmatchningar för den
 * dedikerade "Mina matchningar"-vyn (`GET /api/v1/me/matches`, auth-gated,
 * MeListReadPolicy). Nyast först, cap:at 50 av backend. Speglar `getMatchCount`
 * Result-mönstret: en list-endpoint, så 404 mappas till `error` (default;
 * `includeNotFound` sätts inte).
 */
export async function getMyMatches(): Promise<ApiResult<MatchList>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, "/api/v1/me/matches");
    return await responseToResult(res, matchListSchema, "GET /api/v1/me/matches");
  } catch {
    return { kind: "error" };
  }
}

/**
 * ADR 0080 Vag 4 PR-5 — avancera last-seen-vattenmärket (markera matchningarna
 * sedda). Anropas när den dedikerade vyn ÖPPNAS (Klas-val: mark-seen on opening
 * the view). `POST /api/v1/me/matches/seen` → 204 (auth-gated, MeWritePolicy).
 *
 * Idempotent och icke-kritisk: ett fel får ALDRIG blockera vy-renderingen
 * (counten nollställs då bara inte denna gång) — degraderar civilt likt
 * `saveJobAd`/`unsaveJobAd`-mönstret. 204 → ok; allt annat → fel-kind.
 */
export async function markMatchesSeen(): Promise<ApiResult<void>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, "/api/v1/me/matches/seen", {
      method: "POST",
    });
    if (res.status === 204) return { kind: "ok", data: undefined };
    if (res.status === 401) return { kind: "unauthorized" };
    if (res.status === 403) return { kind: "forbidden" };
    return { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}
