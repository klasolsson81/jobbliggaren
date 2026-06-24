import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  myMatchCountSchema,
  type MyMatchCount,
} from "@/lib/dto/match-count";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";

/**
 * ADR 0079 STEG 6 — hämtar den inloggade användarens live match-count för
 * Översikts notis. Konsumerar `GET /api/v1/me/match-count` (auth-gated,
 * MeListReadPolicy). Speglar `getMyProfile`/`getSavedJobAds` Result/fel-mönster
 * (ADR 0030): unauthorized / rateLimited / error.
 *
 * Counten är grad-filtrerad (Bra + Stark) över hela den aktiva korpusen och är
 * per konstruktion samma som TotalCount på `/jobb?matchGrades=Good&matchGrades=Strong`.
 * `count === 0` är ett ärligt svar (inget angivet yrke ELLER inga matchningar)
 * — Översikts notis renderar nollstate-copy, aldrig en mock-siffra.
 */
export async function getMatchCount(): Promise<ApiResult<MyMatchCount>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/me/match-count`, {
      headers: { Authorization: `Bearer ${sessionId}` },
      cache: "no-store",
    });
    return await responseToResult(
      res,
      myMatchCountSchema,
      "GET /api/v1/me/match-count"
    );
  } catch {
    return { kind: "error" };
  }
}
