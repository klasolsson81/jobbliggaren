import { z } from "zod";
import { ACCOUNT_SEARCH_STATUSES, type AccountSearchStatus } from "@/lib/dto/admin-accounts";

const instant = z.iso.datetime({ offset: true });

export interface AccountDirectoryFilters {
  readonly status?: AccountSearchStatus;
  readonly registeredFrom?: string;
  readonly registeredBefore?: string;
}

export function parseRegistrationBounds(from: unknown, before: unknown): Pick<AccountDirectoryFilters, "registeredFrom" | "registeredBefore"> | null {
  if (from === undefined && before === undefined) return {};
  const start = instant.safeParse(from);
  const end = instant.safeParse(before);
  return start.success && end.success && Date.parse(start.data) <= Date.parse(end.data)
    ? { registeredFrom: start.data, registeredBefore: end.data }
    : null;
}

export function parseAccountFilters(params: Readonly<Record<string, unknown>>): AccountDirectoryFilters | null {
  const bounds = parseRegistrationBounds(params.registeredFrom, params.registeredBefore);
  const status = params.status;
  if (bounds === null || (status !== undefined && !ACCOUNT_SEARCH_STATUSES.includes(status as AccountSearchStatus))) return null;
  return status === undefined ? bounds : { ...bounds, status: status as AccountSearchStatus };
}