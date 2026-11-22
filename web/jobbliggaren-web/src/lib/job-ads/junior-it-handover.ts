import { buildJobbHref, DEFAULT_SORT_BY, type JobbUrlState } from "./search-params";
import { JUNIOR_IT_PATH, juniorItHref, parseJuniorItParams } from "./junior-it-params";

/**
 * The two addresses a request for the open junior list can be sent on to (#2284): the list's own, and the search
 * page with the junior filter, for a visitor who is signed in.
 *
 * Both are built from the one parse the page itself uses, `parseJuniorItParams`, so the proxy never sends on a
 * request the page would answer with its 404, and only validated values travel: a kommun concept id, one of two
 * orders, a page number. Every other key the request carried is left behind.
 */
export interface JuniorItHandover {
  /** The list's canonical address for the query (`juniorItHref`). */
  readonly junior: `/${string}`;
  /**
   * `/jobb` with the junior filter, the list's kommun and its order (`buildJobbHref`). The page is not carried: the
   * two lists are not the same set (ADR 0186 A4), so page N of one is no page of the other.
   */
  readonly jobb: string;
}

/** Null unless the path is exactly the list's and the page would accept the query. */
export function juniorItHandover(url: Pick<URL, "pathname" | "searchParams">): JuniorItHandover | null {
  if (url.pathname !== JUNIOR_IT_PATH) return null;

  const params = parseJuniorItParams(asPageSearchParams(url.searchParams));
  if (params.kind !== "query") return null;

  const { municipality, sortBy } = params.query;
  return {
    junior: juniorItHref(params.query),
    jobb: buildJobbHref({
      ...NO_FILTER,
      municipality: municipality === null ? [] : [municipality],
      junior: true,
      sortBy,
    }),
  };
}

const NO_FILTER: JobbUrlState = {
  q: "",
  occupationGroup: [],
  occupationName: [],
  region: [],
  municipality: [],
  remote: false,
  junior: false,
  employmentType: [],
  worktimeExtent: [],
  matchGrades: [],
  sortBy: DEFAULT_SORT_BY,
};

/** The shape Next gives a page's `searchParams`: a key's one value, or all of them when it repeats. */
function asPageSearchParams(params: URLSearchParams): Record<string, string | string[]> {
  const shaped: Record<string, string | string[]> = {};
  for (const key of new Set(params.keys())) {
    const values = params.getAll(key);
    const [only] = values;
    shaped[key] = values.length === 1 && only !== undefined ? only : values;
  }
  return shaped;
}
