/**
 * How the catalogue reads its one parameter, `sida` (ADR 0176 F7, F11). Every other parameter is ignored.
 *
 * - no `sida`: page 1;
 * - `sida=1`: a permanent redirect to the bare path, so page 1 has one address;
 * - `sida=n` for a whole number from 2: page n, which the API may still answer as past the end;
 * - anything else (a repeated `sida`, `0`, `02`, `abc`, a number past the safe integers): no such page, decided here
 *   without asking the API.
 */
export type CataloguePageParam =
  | { readonly kind: "page"; readonly page: number }
  | { readonly kind: "redirectToFirst" }
  | { readonly kind: "noSuchPage" };

const WHOLE_NUMBER_FROM_ONE = /^[1-9][0-9]*$/;

export function parseCataloguePageParam(sida: string | string[] | undefined): CataloguePageParam {
  if (sida === undefined) return { kind: "page", page: 1 };
  if (Array.isArray(sida) || !WHOLE_NUMBER_FROM_ONE.test(sida)) return { kind: "noSuchPage" };

  const page = Number(sida);
  if (!Number.isSafeInteger(page)) return { kind: "noSuchPage" };
  return page === 1 ? { kind: "redirectToFirst" } : { kind: "page", page };
}
