import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  getResumesResultSchema,
  resumeDetailDtoSchema,
  type GetResumesResult,
  type ResumeDetailDto,
} from "@/lib/dto/resumes";
import {
  parsedResumeDetailDtoSchema,
  cvReviewDtoSchema,
  cvImprovementDtoSchema,
  pendingParsedResumeResponseSchema,
  type ParsedResumeDetailDto,
  type CvReviewDto,
  type CvImprovementDto,
  type RenderProfile,
  type PendingParsedResumeSummary,
} from "@/lib/dto/parsed-resume";
import {
  responseToResult,
  type ApiResult,
} from "@/lib/dto/_helpers";
import {
  parsedResumeOccupationsSchema,
  type OccupationCandidate,
} from "@/lib/dto/match-preferences";
import { isValidId } from "@/lib/validation/guid";

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

export async function getResumes(
  page = 1,
  pageSize = 20
): Promise<ApiResult<GetResumesResult>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/resumes?${params}`, {
      headers: authHeaders(sessionId),
      cache: "no-store",
    });
    return await responseToResult(
      res,
      getResumesResultSchema,
      "GET /api/v1/resumes"
    );
  } catch {
    return { kind: "error" };
  }
}

/** Onboarding-frikoppling (DEL 1, CTO-bind pending-card): det smala 3-variant-
 * resultatet för latest-pending-läsningen. `data: null` = användaren har inget
 * pending CV (HTTP 200 med `null`-body, inte 404). Alla andra utfall än ok/401
 * (forbidden/notFound/rateLimited/network/shape-mismatch) kollapsar civilt till
 * `error` — kortet utelämnas hellre än renderas trasigt. */
export type PendingParsedResumeResult =
  | { kind: "ok"; data: PendingParsedResumeSummary | null }
  | { kind: "unauthorized" }
  | { kind: "error" };

/**
 * Onboarding-frikoppling (DEL 1, CTO-bind pending-card) — server-only BFF mot
 * `GET /api/v1/resumes/parsed/latest-pending` (auth-gated, Bearer-session, samma
 * exponering som övriga `/api/v1/resumes/...`-läsningar). Speglar de andra BFF-
 * fetcharna här: Zod-validering vid ACL-gränsen, 401-mappning via
 * `responseToResult`, try/catch → `{kind:"error"}`.
 *
 * Returnerar den NON-PII-summeringen av användarens senaste PendingReview-
 * parsade CV (id + filnamn + tidpunkt) — INTE parse-innehållet, så detta får
 * läsas av /cv-listvyn (RSC) för att yta ett "slutför ditt CV"-kort. Backend
 * svarar 200 med literal `null` när inget pending CV finns (inte 404), så
 * schemat är nullable och `null` är ett legitimt ok-värde (inte ett fel).
 */
export async function getLatestPendingParsedResume(): Promise<PendingParsedResumeResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/parsed/latest-pending`,
      { headers: authHeaders(sessionId), cache: "no-store" }
    );
    const result = await responseToResult(
      res,
      pendingParsedResumeResponseSchema,
      "GET /api/v1/resumes/parsed/latest-pending"
    );
    // Smal-ner det delade ApiResult:et till det 3-variant-kontrakt callers vill ha:
    // 401 bärs vidare, allt annat icke-ok (forbidden/notFound/rateLimited/error)
    // kollapsar till error (kortet utelämnas hellre än renderas trasigt).
    if (result.kind === "ok") return { kind: "ok", data: result.data };
    if (result.kind === "unauthorized") return { kind: "unauthorized" };
    return { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}

/**
 * Fas 4 onboarding (CTO Variant B 2026-06-21) — the OWNER's non-PII SSYK occupation proposals
 * for a PendingReview parsed CV (`GET /api/v1/resumes/parsed/{id}/occupations`). The backend
 * PROJECTS the plain-jsonb proposals and never decrypts the CV-PII (the query is not
 * `IRequiresFieldEncryptionKey`), so this read carries no CV-PII. Lets the match-setup wizard
 * suggest occupations from a freshly-uploaded CV that has not yet been promoted to a Resume.
 * Owner-scoped + IDOR fail-closed lives in backend (unknown/cross-user/promoted → 404).
 * Maps the wire shape to the shared `OccupationCandidate` so the wizard toggles the right group.
 */
export async function getParsedResumeOccupations(
  id: string
): Promise<ApiResult<OccupationCandidate[]>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  // Allowlist-guard: reject non-GUID before it reaches the backend URL (SSRF barrier +
  // path-injection); a malformed id cannot exist → 404.
  if (!isValidId(id)) return { kind: "notFound" };

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/parsed/${encodeURIComponent(id)}/occupations`,
      { headers: authHeaders(sessionId), cache: "no-store" }
    );
    const result = await responseToResult(
      res,
      parsedResumeOccupationsSchema,
      `GET /api/v1/resumes/parsed/${id}/occupations`,
      { includeNotFound: true }
    );
    if (result.kind !== "ok") return result;
    return {
      kind: "ok",
      // exp-per-occ (ADR 0079-amendment PR-4): carry the CV-derived
      // `approximateYears` through to the shared `OccupationCandidate` so the
      // wizard can seed each occupation's year input. `0` and `null` are kept
      // distinct (a parsed sub-year role vs not stated) — never coalesced.
      data: result.data.map((p) => ({
        occupationGroupConceptId: p.conceptId,
        occupationGroupLabel: p.label,
        approximateYears: p.approximateYears,
      })),
    };
  } catch {
    return { kind: "error" };
  }
}

export async function getResumeById(
  id: string
): Promise<ApiResult<ResumeDetailDto>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  // Allowlist-guard: avvisa icke-GUID innan id:t når backend-URL:en (SSRF-
  // barrier + path-injektion-skydd). Malformat id kan ändå inte existera → 404.
  if (!isValidId(id)) return { kind: "notFound" };

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/${encodeURIComponent(id)}`,
      { headers: authHeaders(sessionId), cache: "no-store" }
    );
    return await responseToResult(
      res,
      resumeDetailDtoSchema,
      `GET /api/v1/resumes/${id}`,
      { includeNotFound: true }
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * Hämtar staging-artefakten för ett importerat CV (F4-8) — driver granska- och
 * gap-fill-vyerna (Fas 4 STEG B). Bär ägarens dekrypterade, löst tolkade CV-
 * innehåll (CV-PII): denna funktion är `server-only` och anropas bara från RSC/
 * server-actions, aldrig i klientbunten. Ägar-scope + IDOR fail-closed lever i
 * backend (okänd/främmande/befordrad → 404, ingen enumererings-orakel).
 */
export async function getParsedResume(
  id: string
): Promise<ApiResult<ParsedResumeDetailDto>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  if (!isValidId(id)) return { kind: "notFound" };

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/parsed/${encodeURIComponent(id)}`,
      { headers: authHeaders(sessionId), cache: "no-store" }
    );
    return await responseToResult(
      res,
      parsedResumeDetailDtoSchema,
      `GET /api/v1/resumes/parsed/${id}`,
      { includeNotFound: true }
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * Hämtar den deterministiska CV-granskningen (F4-9): PASS/WARN/FAIL + citerad
 * evidens per kriterium, ägar-scopat. Evidensen är REDAN personnummer-redigerad
 * vid motorns choke point (`CvReviewEngine`) — server-only, ingen ofiltrerad
 * fritext når klienten. `profile` är `Ats`|`Visual` (backend-validatorn är
 * case-sensitive); searchParam:n på granska-vyn bär det exakta värdet.
 */
export async function getCvReview(
  id: string,
  profile: RenderProfile
): Promise<ApiResult<CvReviewDto>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  if (!isValidId(id)) return { kind: "notFound" };

  const params = new URLSearchParams({ profile });

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/parsed/${encodeURIComponent(id)}/review?${params}`,
      { headers: authHeaders(sessionId), cache: "no-store" }
    );
    return await responseToResult(
      res,
      cvReviewDtoSchema,
      `GET /api/v1/resumes/parsed/${id}/review`,
      { includeNotFound: true }
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * Hämtar de deterministiska CV-förbättringsförslagen (F4-10, propose-and-approve):
 * före→efter-diffar + strukturella operationer med citerad evidens + proveniens,
 * ägar-scopat. Display-only i v1 — det finns inget apply-endpoint; en regelmotor
 * skriver aldrig om tyst (CLAUDE.md §5). Evidensen är REDAN personnummer-redigerad
 * vid motorns choke point — server-only, ingen ofiltrerad fritext når klienten.
 * `profile` är `Ats`|`Visual` (backend-validatorn är case-sensitive); searchParam:n
 * på förbättra-vyn bär det exakta värdet.
 */
export async function getCvImprovements(
  id: string,
  profile: RenderProfile
): Promise<ApiResult<CvImprovementDto>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  if (!isValidId(id)) return { kind: "notFound" };

  const params = new URLSearchParams({ profile });

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/parsed/${encodeURIComponent(id)}/improvements?${params}`,
      { headers: authHeaders(sessionId), cache: "no-store" }
    );
    return await responseToResult(
      res,
      cvImprovementDtoSchema,
      `GET /api/v1/resumes/parsed/${id}/improvements`,
      { includeNotFound: true }
    );
  } catch {
    return { kind: "error" };
  }
}
