import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  getResumesResultSchema,
  resumeDetailDtoSchema,
  templateCatalogDtoSchema,
  type GetResumesResult,
  type ResumeDetailDto,
  type TemplateCatalogDto,
} from "@/lib/dto/resumes";
import {
  parsedResumeDetailDtoSchema,
  cvReviewDtoSchema,
  cvImprovementDtoSchema,
  cvSectionSuggestionsDtoSchema,
  pendingParsedResumeResponseSchema,
  type ParsedResumeDetailDto,
  type CvReviewDto,
  type CvImprovementDto,
  type CvSectionSuggestionsDto,
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
    const res = await authedFetch(sessionId, `/api/v1/resumes?${params}`);
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
    const res = await authedFetch(
      sessionId,
      "/api/v1/resumes/parsed/latest-pending"
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
    const res = await authedFetch(
      sessionId,
      `/api/v1/resumes/parsed/${encodeURIComponent(id)}/occupations`
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
    const res = await authedFetch(
      sessionId,
      `/api/v1/resumes/${encodeURIComponent(id)}`
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
 * Hämtar den slutna, icke-PII-katalogen av malloptioner (Fas 4b PR-8b 8b.3,
 * CTO-bind Q2) — mallbyggarens pickers läser mall/accent/font/täthet-namn plus de
 * två BE-auktoritativa fakta FE inte får härleda: per-mall `atsSafe` (domänregeln,
 * P5) och accent-`hex` (den WCAG-vaktade paletten). Statisk referensdata, samma för
 * alla användare (backend cache:ar `private, max-age=3600`), auth-gated passthrough.
 * Speglar `getResumeById` (Zod vid ACL-gränsen, 401/429/fel → ApiResult). Ingen id
 * → ingen SSRF-guard behövs. Katalogen är ESSENTIELL för byggaren; en icke-ok
 * degraderas av sidan till ett felskal (byggaren kan inte renderas utan optioner).
 */
export async function getTemplateCatalog(): Promise<
  ApiResult<TemplateCatalogDto>
> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(
      sessionId,
      "/api/v1/resumes/template-catalog"
    );
    return await responseToResult(
      res,
      templateCatalogDtoSchema,
      "GET /api/v1/resumes/template-catalog"
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
    const res = await authedFetch(
      sessionId,
      `/api/v1/resumes/parsed/${encodeURIComponent(id)}`
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
 * Yrkesstyrda sektionsförslag för Slutför-guiden (8b.4a, ADR 0107). Egen läs-slice, inte
 * en del av /improvements: ett sektionsförslag är ingen ProposedChange.
 *
 * Läses server-side i RSC:n och skickas som prop till guiden — aldrig via `useEffect`
 * (CLAUDE.md §4/§5). Förslagen är rådgivande: misslyckas hämtningen renderar guiden sin
 * generiska panel precis som förut, för en trasig FÖRSLAGS-rad får aldrig blockera det som
 * faktiskt är uppgiften (att slutföra CV:t). Anroparen behandlar därför allt utom "ok" som
 * "inga förslag".
 */
export async function getCvSectionSuggestions(
  id: string
): Promise<ApiResult<CvSectionSuggestionsDto>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  if (!isValidId(id)) return { kind: "notFound" };

  try {
    const res = await authedFetch(
      sessionId,
      `/api/v1/resumes/parsed/${encodeURIComponent(id)}/section-suggestions`
    );
    return await responseToResult(
      res,
      cvSectionSuggestionsDtoSchema,
      `GET /api/v1/resumes/parsed/${id}/section-suggestions`,
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
    const res = await authedFetch(
      sessionId,
      `/api/v1/resumes/parsed/${encodeURIComponent(id)}/review?${params}`
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
 * Hämtar den deterministiska CV-granskningen för en BEFORDRAD, kanonisk Resume
 * (Fas 4b PR-4/PR-8.4) — samma rubrikmotor som `getCvReview` men mot den
 * kanoniska endpointen `GET /api/v1/resumes/{id}/review` (Resume-id, inte
 * parsedId). Svaret bär `userStatus`/`userStatusStaleAt`-overlayen (den bevarade
 * finding-statusledgern, D2(e)) + `isIgnorable` per kriterium (styleOnly-gaten)
 * som den parsade granskningen inte behöver. Evidensen är REDAN personnummer-
 * redigerad vid motorns choke point — server-only, ingen ofiltrerad fritext når
 * klienten. `profile` är `Ats`|`Visual` (case-sensitive backend-validator).
 */
export async function getResumeReview(
  id: string,
  profile: RenderProfile
): Promise<ApiResult<CvReviewDto>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  if (!isValidId(id)) return { kind: "notFound" };

  const params = new URLSearchParams({ profile });

  try {
    const res = await authedFetch(
      sessionId,
      `/api/v1/resumes/${encodeURIComponent(id)}/review?${params}`
    );
    return await responseToResult(
      res,
      cvReviewDtoSchema,
      `GET /api/v1/resumes/${id}/review`,
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
    const res = await authedFetch(
      sessionId,
      `/api/v1/resumes/parsed/${encodeURIComponent(id)}/improvements?${params}`
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
