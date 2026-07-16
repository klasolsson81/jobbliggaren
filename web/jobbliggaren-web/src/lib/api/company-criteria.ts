import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  listCompanyWatchCriteriaResultSchema,
  criterionReferenceSchema,
  companyBrowseResponseSchema,
  criterionMagnitudeSchema,
  createCriterionResultSchema,
  type ListCompanyWatchCriteriaResult,
  type CriterionReference,
  type CompanyBrowseResponse,
  type CriterionMagnitude,
  type CriterionPredicateInput,
} from "@/lib/dto/company-criteria";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";
import { isValidId } from "@/lib/validation/guid";

/**
 * #560 PR-3 (CTO Fork G5) — server-only fetchers for the criteria-based company watches
 * (`/api/v1/me/company-watch-criteria`). Mirrors `lib/api/company-follows.ts`: `authedFetch` + the
 * `ApiResult<T>` discriminated union + Zod validation at the ACL boundary. Consumed by Server
 * Components / server actions / BFF route handlers, never a browser.
 */

const BASE = "/api/v1/me/company-watch-criteria";

// Mirror the taxonomy tree: the reference is per-deploy static (backend serves ETag +
// Cache-Control: private, max-age=3600), so a one-hour Data-Cache revalidate spares re-fetching
// ~100 kB on adjacent renders.
const REFERENCE_REVALIDATE_SECONDS = 3600;

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

/**
 * List the current user's criteria for the "Smarta bevakningar" section. Unpaginated (hard-capped at
 * 20 server-side). List semantics (ADR 0030): a 404 collapses to `error`, never `notFound`.
 */
export async function getCompanyWatchCriteria(): Promise<
  ApiResult<ListCompanyWatchCriteriaResult>
> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, BASE);
    return await responseToResult(
      res,
      listCompanyWatchCriteriaResultSchema,
      "GET /api/v1/me/company-watch-criteria",
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * Fetch the SCB reference tree the picker renders (the `getTaxonomyTree` precedent): a raw `fetch`
 * with a revalidate window rather than `authedFetch`'s forced no-store, because this response is
 * per-deploy static and identical across users. Consumed by Server Components and passed down to the
 * client picker.
 */
export async function getCriterionReference(): Promise<ApiResult<CriterionReference>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await fetch(`${env.BACKEND_URL}${BASE}/reference`, {
      headers: authHeaders(sessionId),
      next: { revalidate: REFERENCE_REVALIDATE_SECONDS },
    });
    return await responseToResult(
      res,
      criterionReferenceSchema,
      "GET /api/v1/me/company-watch-criteria/reference",
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * Browse the register companies a saved criterion matches (the criterion "run"). Detail-endpoint
 * semantics: a 404 (unknown id OR another user's id — never an enumeration oracle) is surfaced as
 * `notFound`. The response composes the paginated page and the honest magnitude.
 */
export async function browseCriterionCompanies(
  criterionId: string,
  page: number,
): Promise<ApiResult<CompanyBrowseResponse>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  // Allowlist-guard: reject a non-GUID before it reaches the backend URL (SSRF/path-injection).
  if (!isValidId(criterionId)) return { kind: "notFound" };

  const safePage = Number.isInteger(page) && page > 0 ? page : 1;

  try {
    const res = await authedFetch(
      sessionId,
      `${BASE}/${encodeURIComponent(criterionId)}/companies?page=${safePage}&pageSize=20`,
    );
    return await responseToResult(
      res,
      companyBrowseResponseSchema,
      "GET /api/v1/me/company-watch-criteria/{id}/companies",
      { includeNotFound: true },
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * The picker's live magnitude preview over an UNSAVED criterion. POST-as-read (the predicate is a
 * body, never a URL). A 400 (missing axis / unknown code) collapses to `error` — the hook then shows
 * a neutral placeholder, never a false 0.
 */
export async function previewCriterionCount(
  predicate: CriterionPredicateInput,
): Promise<ApiResult<CriterionMagnitude>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, `${BASE}/preview-count`, {
      method: "POST",
      body: JSON.stringify({
        criteria: {
          sniCodes: predicate.sniCodes,
          municipalityCodes: predicate.municipalityCodes,
        },
      }),
    });
    return await responseToResult(
      res,
      criterionMagnitudeSchema,
      "POST /api/v1/me/company-watch-criteria/preview-count",
    );
  } catch {
    return { kind: "error" };
  }
}

// ── Write path ──────────────────────────────────────────────────────────────

/**
 * The write outcome for create/update. Distinct from the read `ApiResult<T>`: it carries the two
 * user-relevant backend messages the UI surfaces verbatim — the 400 unknown-codes detail (which names
 * the offending codes; i18n cannot reproduce a dynamic value) and the 409 max-per-user message. Both
 * are DELIBERATE, controlled Swedish validator strings over public SCB codes (no PII, no stacktrace);
 * they are extracted from two bounded, known fields only and length-capped (see
 * `extractWriteMessage`). Everything else maps to a status-only i18n error in the action.
 */
export type CriterionWriteResult<T> =
  | { kind: "ok"; data: T }
  | { kind: "unauthorized" }
  | { kind: "validation"; message: string | null }
  | { kind: "conflict"; message: string | null }
  | { kind: "notFound" }
  | { kind: "rateLimited"; retryAfterSeconds: number }
  | { kind: "error" };

/** Create a criterion. 201 → the new id; 400 → validation; 409 → the max-per-user conflict. */
export async function createCriterion(
  predicate: CriterionPredicateInput,
  label: string | null,
): Promise<CriterionWriteResult<{ id: string }>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, BASE, {
      method: "POST",
      body: JSON.stringify({
        criteria: {
          sniCodes: predicate.sniCodes,
          municipalityCodes: predicate.municipalityCodes,
        },
        label,
      }),
    });

    if (res.status === 201) {
      const parsed = createCriterionResultSchema.safeParse(await res.json());
      return parsed.success
        ? { kind: "ok", data: { id: parsed.data.id } }
        : { kind: "error" };
    }
    return await mapWriteFailure(res);
  } catch {
    return { kind: "error" };
  }
}

/**
 * Update a criterion (PATCH partial). `label`/`criteria` are sent as given: a present `criteria`
 * replaces the WHOLE predicate; a present blank `label` clears the name. 204 → ok; 400 → validation;
 * 404 → not found.
 */
export async function updateCriterion(
  criterionId: string,
  body: {
    readonly label?: string | null;
    readonly criteria?: CriterionPredicateInput;
  },
): Promise<CriterionWriteResult<void>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  if (!isValidId(criterionId)) return { kind: "notFound" };

  try {
    const res = await authedFetch(sessionId, `${BASE}/${encodeURIComponent(criterionId)}`, {
      method: "PATCH",
      body: JSON.stringify({
        ...(body.label !== undefined ? { label: body.label } : {}),
        ...(body.criteria !== undefined
          ? {
              criteria: {
                sniCodes: body.criteria.sniCodes,
                municipalityCodes: body.criteria.municipalityCodes,
              },
            }
          : {}),
      }),
    });

    if (res.status === 204) return { kind: "ok", data: undefined };
    return await mapWriteFailure(res);
  } catch {
    return { kind: "error" };
  }
}

/**
 * Delete a criterion (HARD delete). 204 → ok. A repeat delete answers 404 (the row is gone) — surfaced
 * as `notFound` so the action can treat it as success-equivalent in UX. List semantics do not apply
 * (this is an id-addressed detail mutation).
 */
export async function deleteCriterion(criterionId: string): Promise<ApiResult<void>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  if (!isValidId(criterionId)) return { kind: "notFound" };

  try {
    const res = await authedFetch(sessionId, `${BASE}/${encodeURIComponent(criterionId)}`, {
      method: "DELETE",
    });
    if (res.status === 204) return { kind: "ok", data: undefined };
    if (res.status === 401) return { kind: "unauthorized" };
    if (res.status === 404) return { kind: "notFound" };
    return { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}

// ── Write-failure mapping ─────────────────────────────────────────────────────

/** Map a non-success write response to the write union, extracting the surfaceable message. */
async function mapWriteFailure(res: Response): Promise<CriterionWriteResult<never>> {
  if (res.status === 401) return { kind: "unauthorized" };
  if (res.status === 404) return { kind: "notFound" };
  if (res.status === 429) {
    return {
      kind: "rateLimited",
      retryAfterSeconds: parseRetryAfterHeader(res.headers.get("Retry-After")),
    };
  }
  if (res.status === 400) {
    return { kind: "validation", message: await extractWriteMessage(res) };
  }
  if (res.status === 409) {
    return { kind: "conflict", message: await extractWriteMessage(res) };
  }
  return { kind: "error" };
}

const MAX_SURFACED_MESSAGE_LENGTH = 300;

/**
 * Extract the ONE user-relevant Swedish message from a 400/409 body, from two bounded, known shapes:
 *
 * 1. the pipeline validator's `{ errors: { "Criteria.SniCodes": ["Okända SNI-koder: 99998."], ... } }`
 *    (the middleware `ValidationException` shape) — every string message joined; and
 * 2. a Result-side ProblemDetails `{ detail: "Du kan ha högst 20 bevakningar. ..." }` (the
 *    `DomainError.ToProblemResult()` shape for the max-per-user conflict and the domain-spec 400).
 *
 * Nothing else is read (never a raw stacktrace or an arbitrary field), and the result is length-
 * capped. Returns null when neither shape yields text — the action then uses a generic i18n string.
 */
async function extractWriteMessage(res: Response): Promise<string | null> {
  let body: unknown;
  try {
    body = await res.json();
  } catch {
    return null;
  }
  if (typeof body !== "object" || body === null) return null;

  const record = body as Record<string, unknown>;

  // Shape 1 — the validation `errors` dictionary.
  const errors = record.errors;
  if (typeof errors === "object" && errors !== null) {
    const messages: string[] = [];
    for (const value of Object.values(errors as Record<string, unknown>)) {
      if (Array.isArray(value)) {
        for (const item of value) if (typeof item === "string") messages.push(item);
      } else if (typeof value === "string") {
        messages.push(value);
      }
    }
    if (messages.length > 0) return cap(messages.join(" "));
  }

  // Shape 2 — the ProblemDetails `detail`.
  if (typeof record.detail === "string" && record.detail.trim().length > 0) {
    return cap(record.detail.trim());
  }

  return null;
}

function cap(message: string): string {
  return message.length > MAX_SURFACED_MESSAGE_LENGTH
    ? `${message.slice(0, MAX_SURFACED_MESSAGE_LENGTH)}…`
    : message;
}

const DEFAULT_RETRY_AFTER_SECONDS = 60;

function parseRetryAfterHeader(headerValue: string | null): number {
  if (!headerValue) return DEFAULT_RETRY_AFTER_SECONDS;
  const seconds = Number.parseInt(headerValue.trim(), 10);
  return Number.isFinite(seconds) && seconds > 0 ? seconds : DEFAULT_RETRY_AFTER_SECONDS;
}
