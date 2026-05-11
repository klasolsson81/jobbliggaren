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
