/**
 * The open junior IT list's address, `/junior` (ADR 0186), and how its three parameters are read. They carry the same
 * names and values as `/jobb`'s (`municipality`, `sortBy`, `page`), so the list can hand a visitor on to `/jobb` with
 * the same query (#2261).
 *
 * - `municipality`: one kommun concept id; absent or empty means every kommun.
 * - `sortBy`: `PublishedAtDesc` (the default) or `ExpiresAtAsc`.
 * - `page`: a whole number from 1; absent means 1.
 *
 * A value that cannot be one of these (a repeated key, an id with other characters, `page=0`, an unknown order) is no
 * such page, decided here without asking the API. Every other parameter is ignored.
 */
export const JUNIOR_IT_PATH = "/junior" as const;

export const JUNIOR_IT_ORDERS = ["PublishedAtDesc", "ExpiresAtAsc"] as const;
export type JuniorItOrder = (typeof JUNIOR_IT_ORDERS)[number];
export const DEFAULT_JUNIOR_IT_ORDER: JuniorItOrder = "PublishedAtDesc";

/** Mirrors the API validator: JobTech concept ids, at most 32 characters. */
const MUNICIPALITY_ID = /^[A-Za-z0-9_]{1,32}$/;
const WHOLE_NUMBER_FROM_ONE = /^[1-9][0-9]*$/;

export interface JuniorItQuery {
  readonly municipality: string | null;
  readonly sortBy: JuniorItOrder;
  readonly page: number;
}

export type JuniorItParams = { readonly kind: "query"; readonly query: JuniorItQuery } | { readonly kind: "noSuchPage" };

type Raw = string | string[] | undefined;

export function parseJuniorItParams(params: Readonly<Record<string, Raw>>): JuniorItParams {
  const { municipality, sortBy, page } = params;
  if (Array.isArray(municipality) || Array.isArray(sortBy) || Array.isArray(page)) return { kind: "noSuchPage" };

  let parsedMunicipality: string | null = null;
  if (municipality !== undefined && municipality !== "") {
    if (!MUNICIPALITY_ID.test(municipality)) return { kind: "noSuchPage" };
    parsedMunicipality = municipality;
  }

  let parsedSort: JuniorItOrder = DEFAULT_JUNIOR_IT_ORDER;
  if (sortBy !== undefined && sortBy !== "") {
    if (!isJuniorItOrder(sortBy)) return { kind: "noSuchPage" };
    parsedSort = sortBy;
  }

  let parsedPage = 1;
  if (page !== undefined) {
    if (!WHOLE_NUMBER_FROM_ONE.test(page)) return { kind: "noSuchPage" };
    parsedPage = Number(page);
    if (!Number.isSafeInteger(parsedPage)) return { kind: "noSuchPage" };
  }

  return { kind: "query", query: { municipality: parsedMunicipality, sortBy: parsedSort, page: parsedPage } };
}

function isJuniorItOrder(value: string): value is JuniorItOrder {
  return (JUNIOR_IT_ORDERS as readonly string[]).includes(value);
}

/**
 * The list's address for a query. A default is left out, and the keys are written in one fixed order, so one list
 * has one address.
 */
export function juniorItHref(query: JuniorItQuery): `/${string}` {
  const search = new URLSearchParams();
  if (query.municipality !== null) search.set("municipality", query.municipality);
  if (query.sortBy !== DEFAULT_JUNIOR_IT_ORDER) search.set("sortBy", query.sortBy);
  if (query.page > 1) search.set("page", String(query.page));
  const qs = search.toString();
  return qs === "" ? JUNIOR_IT_PATH : `${JUNIOR_IT_PATH}?${qs}`;
}
