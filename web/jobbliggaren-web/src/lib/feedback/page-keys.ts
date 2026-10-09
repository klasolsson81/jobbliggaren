/**
 * The pages feedback is collected for (#1979, ADR 0156 D1), and which signed-in route belongs to which.
 *
 * The keys are the backend's closed set (`FeedbackPage`), in the app's own order. A rating category can
 * only come from this list: a page passes its key as a literal, and the footer resolves a key from the
 * route's pattern, so a filter, a query string or an ad or CV id never makes a key of its own.
 */
export const FEEDBACK_PAGE_KEYS = [
  "overview",
  "jobs",
  "job-ad",
  "matches",
  "saved-ads",
  "saved-searches",
  "applications",
  "application",
  "new-application",
  "statistics",
  "activity-report",
  "followed-companies",
  "company-search",
  "industry-watches",
  "application-history",
  "cv",
  "cv-import",
  "cv-review",
  "my-pages",
] as const;

export type FeedbackPageKey = (typeof FEEDBACK_PAGE_KEYS)[number];

const PAGE_KEYS: ReadonlySet<string> = new Set(FEEDBACK_PAGE_KEYS);

export function isFeedbackPageKey(value: unknown): value is FeedbackPageKey {
  return typeof value === "string" && PAGE_KEYS.has(value);
}

/**
 * Every page route under `(app)`, written without its route groups. A route either carries a page key, or
 * is exempt with the reason it renders no product page. `page-feedback-coverage.test.ts` holds this list
 * equal to the page files on disk, so a new page cannot go unclassified.
 */
export type FeedbackRoute =
  | { readonly pattern: string; readonly key: FeedbackPageKey }
  | { readonly pattern: string; readonly exempt: string };

export const FEEDBACK_ROUTES: ReadonlyArray<FeedbackRoute> = [
  { pattern: "/oversikt", key: "overview" },
  { pattern: "/jobb", key: "jobs" },
  { pattern: "/jobb/[id]", key: "job-ad" },
  { pattern: "/matchningar", key: "matches" },
  { pattern: "/sparade", key: "saved-ads" },
  { pattern: "/sokningar", key: "saved-searches" },
  { pattern: "/ansokningar", key: "applications" },
  { pattern: "/ansokningar/[id]", key: "application" },
  { pattern: "/ny-ansokan", key: "new-application" },
  { pattern: "/statistik", key: "statistics" },
  { pattern: "/aktivitetsrapport", key: "activity-report" },
  { pattern: "/foretag", exempt: "Redirects to /foretag/bevakade." },
  { pattern: "/foretag/bevakade", key: "followed-companies" },
  { pattern: "/foretag/bevakade/nya", key: "followed-companies" },
  { pattern: "/foretag/sok", key: "company-search" },
  { pattern: "/foretag/branschbevakningar", key: "industry-watches" },
  { pattern: "/foretag/branschbevakningar/[id]", key: "industry-watches" },
  { pattern: "/foretag/branschbevakningar/[id]/annonser", key: "industry-watches" },
  { pattern: "/foretag/historik", key: "application-history" },
  { pattern: "/cv", key: "cv" },
  { pattern: "/cv/importera", key: "cv-import" },
  { pattern: "/cv/[id]", exempt: "The paused CV builder: notFound()." },
  { pattern: "/cv/[id]/mall", exempt: "The paused CV builder: notFound()." },
  { pattern: "/cv/[id]/granska", key: "cv-review" },
  { pattern: "/cv/ny", exempt: "The paused CV builder: notFound()." },
  { pattern: "/cv/slutfor/[parsedId]", exempt: "The paused CV builder: notFound()." },
  { pattern: "/cv/granska/[parsedId]", key: "cv-review" },
  { pattern: "/cv/granska/[parsedId]/forbattra", exempt: "The paused CV builder: notFound()." },
  { pattern: "/cv/granska/[parsedId]/komplettera", exempt: "The paused CV builder: notFound()." },
  { pattern: "/mina-sidor", key: "my-pages" },
  { pattern: "/mina-sidor/konto", key: "my-pages" },
  { pattern: "/mina-sidor/notiser", key: "my-pages" },
  { pattern: "/mina-sidor/sekretess", key: "my-pages" },
];

const isDynamic = (segment: string) => segment.startsWith("[") && segment.endsWith("]");

const segmentsOf = (path: string) => path.split("/").filter((segment) => segment.length > 0);

/**
 * The page key for a pathname, or null when the path is exempt or not a mapped page. A static segment
 * beats a dynamic one, so `/cv/importera` is the import page and not the paused `/cv/[id]`.
 */
export function feedbackPageKeyFor(pathname: string): FeedbackPageKey | null {
  const path = segmentsOf(pathname);
  let best: { route: FeedbackRoute; staticSegments: number } | null = null;
  for (const route of FEEDBACK_ROUTES) {
    const pattern = segmentsOf(route.pattern);
    if (pattern.length !== path.length) continue;
    let staticSegments = 0;
    let matches = true;
    for (let index = 0; index < pattern.length; index++) {
      const segment = pattern[index]!;
      if (isDynamic(segment)) continue;
      if (segment !== path[index]) {
        matches = false;
        break;
      }
      staticSegments++;
    }
    if (matches && (best === null || staticSegments > best.staticSegments)) best = { route, staticSegments };
  }
  return best !== null && "key" in best.route ? best.route.key : null;
}
