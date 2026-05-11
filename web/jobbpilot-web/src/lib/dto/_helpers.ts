import { z } from "zod";

/**
 * Strukturerat fel vid DTO-validering. Innehåller context-info så caller
 * kan logga eller behandla som "backend nere"-state utan att exposing Zod-
 * detaljer mot UI.
 */
export class DtoParseError extends Error {
  constructor(
    message: string,
    public readonly context: string,
    public readonly cause?: unknown
  ) {
    super(message);
    this.name = "DtoParseError";
  }
}

/**
 * Anti-corruption-layer-gräns. Validerar `Response`-body mot Zod-schema.
 *
 * Vid mismatch: loggar strukturerad fel-info (context + Zod issues) och
 * kastar `DtoParseError`. Konsumenter förväntas wrappa i try-block och
 * mappa till sitt fel-tillstånd (null, kind:"error", etc.).
 *
 * Datum-fält valideras som `z.string()` på wire-nivå — konvertering till
 * `Date` är UI-formateringsansvar. Se ADR 0020.
 */
export async function parseResponse<T>(
  res: Response,
  schema: z.ZodType<T>,
  context: string
): Promise<T> {
  let raw: unknown;
  try {
    raw = await res.json();
  } catch (cause) {
    console.error("DTO parse failed: invalid JSON body", { context, cause });
    throw new DtoParseError("Invalid JSON body", context, cause);
  }

  const result = schema.safeParse(raw);
  if (!result.success) {
    console.error("DTO parse failed: shape mismatch", {
      context,
      issues: redactIssues(result.error.issues),
    });
    throw new DtoParseError("Shape mismatch", context, result.error);
  }

  return result.data;
}

/**
 * Tar bort `received`-fältet ur Zod-issues innan loggning. Zod v4 inkluderar
 * det faktiska värdet i `received` vid type-mismatch-issues — om backend råkar
 * returnera email/userId i fel fält skulle rå PII hamna i strukturerad logg
 * (CloudWatch). CLAUDE.md §5.1 förbjuder PII-loggning i klartext.
 *
 * `path`, `code`, `message`, `expected` behålls — de räcker för debug utan
 * att riskera PII-läckage.
 */
function redactIssues(
  issues: readonly z.core.$ZodIssue[]
): Array<Omit<z.core.$ZodIssue, "received">> {
  return issues.map((issue) => {
    if (!("received" in issue)) return issue;
    const copy: Record<string, unknown> = { ...issue };
    delete copy.received;
    return copy as Omit<z.core.$ZodIssue, "received">;
  });
}

/**
 * Schema-factory för backend `PagedResult<T>`. Ersätter hand-rullad
 * `isPagedResult<T>` från `lib/types/paged.ts` (TD-55) — item-validering
 * är nu default istället för opt-in.
 */
export function pagedResult<T extends z.ZodType>(item: T) {
  return z.object({
    items: z.array(item),
    totalCount: z.number().int().nonnegative(),
    page: z.number().int().positive(),
    pageSize: z.number().int().positive(),
  });
}

/**
 * Pagineringsschema med extra `totalPages`-fält (admin-audit-log-shape).
 * Backend serialiserar `totalPages` för vissa endpoints. Separat factory
 * för att inte tvinga in fältet överallt.
 */
export function pagedResultWithTotalPages<T extends z.ZodType>(item: T) {
  return z.object({
    items: z.array(item),
    totalCount: z.number().int().nonnegative(),
    page: z.number().int().positive(),
    pageSize: z.number().int().positive(),
    totalPages: z.number().int().nonnegative(),
  });
}

/**
 * Generisk discriminated union för frontend API-resultat. Se ADR 0030.
 *
 * Varje variant motsvarar en distinkt UI-state och en distinkt user-action.
 * `notFound` är endast applicabel på detail-endpoints (id-baserade GETs).
 */
export type ApiResult<T> =
  | { kind: "ok"; data: T }
  | { kind: "unauthorized" }
  | { kind: "forbidden" }
  | { kind: "notFound" }
  | { kind: "error" };

/**
 * Mappar `Response` + status-koder + DtoParseError till `ApiResult<T>`.
 *
 * - 200/2xx + valid shape → `{ kind: "ok", data }`
 * - 401 → `{ kind: "unauthorized" }`
 * - 403 → `{ kind: "forbidden" }`
 * - 404 + `includeNotFound: true` → `{ kind: "notFound" }`
 *   (list-endpoints ska låta 404 bli `error` — `notFound` saknar semantik där)
 * - Övriga !res.ok / network / JSON-fel / shape-mismatch → `{ kind: "error" }`
 *
 * Strukturerad fel-logging görs av underliggande `parseResponse` —
 * `responseToResult` är endast outcome-mapping-skikt.
 */
export async function responseToResult<T>(
  res: Response,
  schema: z.ZodType<T>,
  context: string,
  options?: { includeNotFound?: boolean }
): Promise<ApiResult<T>> {
  if (res.status === 401) return { kind: "unauthorized" };
  if (res.status === 403) return { kind: "forbidden" };
  if (res.status === 404 && options?.includeNotFound) {
    return { kind: "notFound" };
  }
  if (!res.ok) return { kind: "error" };

  try {
    const data = await parseResponse(res, schema, context);
    return { kind: "ok", data };
  } catch {
    return { kind: "error" };
  }
}

/**
 * Exhaustiveness-helper för switch-statements över ApiResult-kinds.
 * Glömd `case` blir TypeScript-fel vid `assertNever(result)` i `default`,
 * inte runtime-skyltning. Se ADR 0030 §4.
 */
export function assertNever(value: never): never {
  throw new Error(
    `Unreachable: unhandled discriminator value ${JSON.stringify(value)}`
  );
}
