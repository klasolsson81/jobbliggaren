import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  myMatchCountSchema,
  type MyMatchCount,
  draftMatchCountSchema,
  type DraftMatchCount,
  type DraftMatchCountRequest,
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
    const res = await authedFetch(sessionId, "/api/v1/me/match-count");
    return await responseToResult(
      res,
      myMatchCountSchema,
      "GET /api/v1/me/match-count"
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * Epik #526 (ADR 0089) — live sök-preview-count för matchnings-setup-modalen.
 * Konsumerar `POST /api/v1/me/match-count-preview` (auth-gated,
 * MatchCountPreviewPolicy) med utkastets sök-facetter i body:n. Speglar
 * `getMatchCount`s Result/fel-mönster; till skillnad från den GET-baserade
 * sparade counten är detta en POST (utkastet är en komplex body).
 *
 * Talet är per konstruktion samma som `/jobb`-sökningens TotalCount för samma
 * facetter (delad `ApplyFilter`-SPOT). Bär aldrig per-användar-data — bara
 * publika annons-count:en över utkastets filter.
 */
export async function getDraftMatchCount(
  body: DraftMatchCountRequest
): Promise<ApiResult<DraftMatchCount>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(
      sessionId,
      "/api/v1/me/match-count-preview",
      {
        method: "POST",
        body: JSON.stringify({
          occupationGroups: body.occupationGroups,
          regions: body.regions,
          municipalities: body.municipalities,
          employmentTypes: body.employmentTypes,
        }),
      }
    );
    return await responseToResult(
      res,
      draftMatchCountSchema,
      "POST /api/v1/me/match-count-preview"
    );
  } catch {
    return { kind: "error" };
  }
}
