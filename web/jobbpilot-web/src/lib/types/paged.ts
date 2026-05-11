/**
 * Wire-shape för backend `PagedResult<T>` (Application/Common/PagedResult.cs).
 *
 * Standardiseras här så alla paginerade endpoints konsumeras via en
 * gemensam type-guard — undviker duplikering av per-endpoint-guards som
 * var inkörsporten till typ-skew-buggen TD-55 stängde.
 */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/**
 * Lättviktig runtime-validering. `res.json()` är effektivt unknown och
 * CLAUDE.md §4.1 förbjuder any-baserad shape-cast.
 *
 * Per-item-validering är opt-in via `isItem`-parametern — utan den typas
 * `items` till `T[]` på tro (item-shape inte verifierad). Call-site bör
 * passera in en `isItem`-guard när skew-risken är hög.
 */
export function isPagedResult<T>(
  value: unknown,
  isItem?: (x: unknown) => x is T
): value is PagedResult<T> {
  if (value === null || typeof value !== "object") return false;
  const v = value as Record<string, unknown>;
  if (
    !Array.isArray(v.items) ||
    typeof v.totalCount !== "number" ||
    typeof v.page !== "number" ||
    typeof v.pageSize !== "number"
  ) {
    return false;
  }
  return isItem ? v.items.every(isItem) : true;
}
