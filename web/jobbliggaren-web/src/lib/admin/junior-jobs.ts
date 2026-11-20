/**
 * `/admin/juniorjobb`'s tabs, its URL and its view model (#2264, ADR 0187): the listed Data/IT ads the junior
 * rules left unclear, the ones on /junior now, and the ones an administrator has decided. The page is driven
 * by its URL, so every tab and page is a link; each value here is from a closed set or a bounded number.
 */

/** The route; the decision's Server Action revalidates it. */
export const JUNIOR_JOBS_ROUTE = "/admin/juniorjobb";

/** The backend's page size (`ListJuniorJobAdsQuery.PageSize`). */
export const JUNIOR_JOBS_PAGE_SIZE = 25;

/** The backend's last page (`ListJuniorJobAdsQuery.MaxPage`): a larger one is a 400, so it reads as the first. */
const MAX_PAGE = 1000;

export type AdminJuniorTab = "unclear" | "shown" | "decided";

/** The tabs in the order the page shows them. */
export const JUNIOR_TABS: ReadonlyArray<AdminJuniorTab> = ["unclear", "shown", "decided"];

export type AdminJuniorVerdict = "junior" | "notJunior" | "unclear";

export type AdminJuniorDecision = "junior" | "notJunior";

/** What a button asks for: a decision, or null to hand the ad back to the rules. */
export type AdminJuniorChoice = AdminJuniorDecision | null;

/** One ad: what the ad itself shows and what the rules and an administrator said. Never the ad's text. */
export interface AdminJuniorJobRow {
  readonly id: string;
  readonly title: string;
  readonly company: string;
  readonly municipality: string | null;
  readonly publishedAt: string;
  readonly expiresAt: string | null;
  readonly verdict: AdminJuniorVerdict;
  readonly ruleId: string;
  readonly decision: AdminJuniorDecision | null;
}

export interface AdminJuniorJobsPage {
  readonly items: ReadonlyArray<AdminJuniorJobRow>;
  readonly page: number;
  readonly totalPages: number;
  readonly totalCount: number;
  /** The listed ads in each tab, read at the same instant as the page. */
  readonly counts: Readonly<Record<AdminJuniorTab, number>>;
}

/**
 * The rules' ids the page names in words: the rule file's own rules, its fallbacks and the reading's
 * (`junior-rules.data-it.v1.json`, pinned against it by `junior-jobs.test.ts`). Any other id reads as an
 * unknown rule and is never shown as it came.
 */
export const JUNIOR_RULE_IDS = [
  "reading-2026-11-19",
  "title-thesis",
  "title-senior",
  "title-junior",
  "years-3-plus",
  "text-mentor-only",
  "text-junior-and-senior",
  "text-new-graduate",
  "text-early-career",
  "text-no-experience-needed",
  "text-seek-junior",
  "title-student",
  "flag-no-experience",
  "candidate-only",
  "no-signal",
  "match-timeout",
] as const;

export type JuniorRuleId = (typeof JUNIOR_RULE_IDS)[number];

const RULE_IDS: ReadonlySet<string> = new Set(JUNIOR_RULE_IDS);

export function isJuniorRuleId(value: string): value is JuniorRuleId {
  return RULE_IDS.has(value);
}

/**
 * The choices a row offers in a tab. A decided row offers the other decision first and the hand-back
 * second, so the button pressed to change a decision stays where it was and keeps focus.
 */
export function juniorChoices(tab: AdminJuniorTab, decision: AdminJuniorDecision | null): ReadonlyArray<AdminJuniorChoice> {
  switch (tab) {
    case "unclear":
      return ["junior", "notJunior"];
    case "shown":
      return decision === null ? ["notJunior"] : ["notJunior", null];
    case "decided":
      if (decision === "junior") return ["notJunior", null];
      if (decision === "notJunior") return ["junior", null];
      return ["junior", "notJunior"];
  }
}

export function isJuniorChoice(value: unknown): value is AdminJuniorChoice {
  return value === null || value === "junior" || value === "notJunior";
}

/** An ad id's shape: checked before an id becomes a path segment. */
const AD_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isJuniorJobAdId(value: unknown): value is string {
  return typeof value === "string" && AD_ID.test(value);
}

/** The ProblemDetails title the decision answers with when the ad is not a listed, assessed ad. */
export const JUNIOR_ERRORS = { notFound: "JobAd.NotFound" } as const;

// ── The URL ─────────────────────────────────────────────────────────────────────────────────

/** The URL's tab values, in Swedish like the route. */
const TAB_SLUGS: Readonly<Record<AdminJuniorTab, string>> = {
  unclear: "att-avgora",
  shown: "visas",
  decided: "beslutade",
};

/** What the URL asks for. A value it leaves out, or gives in a form not read, is the default. */
export interface AdminJuniorJobsQuery {
  readonly tab: AdminJuniorTab;
  readonly page: number;
}

export const DEFAULT_JUNIOR_JOBS_QUERY: AdminJuniorJobsQuery = { tab: "unclear", page: 1 };

export type JuniorJobsSearchParams = Readonly<Record<string, string | string[] | undefined>>;

function first(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

function pageFrom(raw: string | undefined): number {
  if (raw === undefined || !/^\d{1,4}$/.test(raw)) return 1;
  const page = Number.parseInt(raw, 10);
  return page >= 1 && page <= MAX_PAGE ? page : 1;
}

/** Reads `flik` and `sida`. */
export function parseJuniorJobsQuery(params: JuniorJobsSearchParams): AdminJuniorJobsQuery {
  const slug = first(params.flik);
  return {
    tab: JUNIOR_TABS.find((tab) => TAB_SLUGS[tab] === slug) ?? DEFAULT_JUNIOR_JOBS_QUERY.tab,
    page: pageFrom(first(params.sida)),
  };
}

/** The URL of the query; a default is left out, so the bare route is the first page of Att avgöra. */
export function juniorJobsHref(basePath: string, query: AdminJuniorJobsQuery): string {
  const params = new URLSearchParams();
  if (query.tab !== DEFAULT_JUNIOR_JOBS_QUERY.tab) params.set("flik", TAB_SLUGS[query.tab]);
  if (query.page > 1) params.set("sida", String(query.page));
  const search = params.toString();
  return search === "" ? basePath : `${basePath}?${search}`;
}

/** A tab starts from its first page. */
export function withTab(tab: AdminJuniorTab): AdminJuniorJobsQuery {
  return { tab, page: 1 };
}

export function withPage(query: AdminJuniorJobsQuery, page: number): AdminJuniorJobsQuery {
  return { ...query, page };
}
