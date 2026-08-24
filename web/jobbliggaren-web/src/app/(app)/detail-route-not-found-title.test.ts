import { readFileSync, readdirSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { createTranslator } from "next-intl";
import type { Metadata } from "next";
import type { ApiResult } from "@/lib/dto/_helpers";
import { declaresOwnTitle } from "@/test/metadata-source";
import svPages from "../../../messages/sv/pages.json";
import svFallback from "../../../messages/sv/fallback.json";
import svMetadata from "../../../messages/sv/metadata.json";

/**
 * The `notFound` branch of every `(app)` detail route's `generateMetadata` (#1508,
 * design-reviewer Major on #1506).
 *
 * The defect: #1506 gave each detail route a title, and a route served that title even
 * when its record was gone — `Ansökan | Jobbliggaren` over a body reading "Sidan finns
 * inte". `(app)/not-found.tsx` cannot correct it, because its own metadata is inert
 * (the `notFound()` is thrown mid-stream, after the head has flushed), so the page's
 * title is the one that survives.
 *
 * Two halves, and the second is the one a later change is likelier to break: the branch
 * exists, AND it fires on `kind === "notFound"` and nothing else. Titling an `error`,
 * `rateLimited` or `unauthorized` result "Sidan finns inte" would assert something
 * false — the same defect class, pointed the other way.
 *
 * The route set is DERIVED, not listed. A hand-written list can only guard the direction
 * where a route disappears from it; the direction that matters more — a route added
 * later, inheriting the defect in silence — is invisible to a list, and
 * `document-title-coverage.test.ts` states the doctrine in the same directory: "a list
 * is the silent hole". `ROUTES` still enumerates, because each route needs its own
 * loader mocked, but the derivation below asserts the enumeration is the whole set.
 *
 * PREMISE (§5 `Tests:`). The mocked loaders stand in for the real ones, and
 * `{ kind: "notFound" }` is a value those real adapters emit on two paths rather than a
 * shape invented here: the pre-flight GUID guard returns it for a malformed id
 * (`if (!isValidId(id)) return { kind: "notFound" }`, present in all five adapters), and
 * `responseToResult(..., { includeNotFound: true })` maps the endpoint's genuine 404 —
 * and its 410 — to it. Every assertion below rests on the discriminator alone:
 * `generateMetadata` reads `result.kind` and nothing else on all five routes, so no `ok`
 * body is fabricated.
 */

const messages = { pages: svPages, fallback: svFallback, metadata: svMetadata };

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "pages" | "fallback" | "metadata") =>
    createTranslator({ locale: "sv", messages, namespace }),
  getFormatter: async () => ({
    number: (n: number) => new Intl.NumberFormat("sv-SE").format(n),
  }),
}));

vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
  notFound: () => {
    throw new Error("NEXT_NOT_FOUND");
  },
}));

const getApplicationById = vi.fn();
const getJobAd = vi.fn();
const getResumeById = vi.fn();
const getParsedResume = vi.fn();
const browseCriterionCompanies = vi.fn();

// Spread the real module and override one export: the pages import siblings from these
// same modules, and a bare factory would blank them.
vi.mock("@/lib/api/applications", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/applications")>()),
  getApplicationById: (...args: unknown[]) => getApplicationById(...args),
}));
vi.mock("@/lib/api/job-ads", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/job-ads")>()),
  getJobAd: (...args: unknown[]) => getJobAd(...args),
}));
vi.mock("@/lib/api/resumes", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/resumes")>()),
  getResumeById: (...args: unknown[]) => getResumeById(...args),
  getParsedResume: (...args: unknown[]) => getParsedResume(...args),
}));
vi.mock("@/lib/api/company-criteria", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/company-criteria")>()),
  browseCriterionCompanies: (...args: unknown[]) =>
    browseCriterionCompanies(...args),
}));

/**
 * Which document each `ApiResult` kind must title. `Record` over the union is the
 * guard: a kind added to `ApiResult` and left unanswered here fails `tsc` — a blocking
 * gate — so this table cannot quietly stop covering the union it is about.
 */
const TITLES: Record<ApiResult<unknown>["kind"], "the route" | "the absence"> = {
  ok: "the route",
  unauthorized: "the route",
  forbidden: "the route",
  notFound: "the absence",
  rateLimited: "the route",
  error: "the route",
};

/** The 404 title as shipped: `fallback.notFound.title` through `metadata.titleTemplate`. */
const ABSENCE_TITLE = svMetadata.titleTemplate.replace(
  "%s",
  () => svFallback.notFound.title,
);

const ID = "b3f1c0de-0000-4000-8000-000000000001";

type SearchParams = Record<string, string | undefined>;

interface DetailRoute {
  readonly path: string;
  readonly loader: ReturnType<typeof vi.fn>;
  readonly ownTitle: string;
  /** Arguments the route's existence read takes after the id; empty for four of five. */
  readonly extraLoaderArgs: readonly unknown[];
  readonly importer: () => Promise<{
    generateMetadata?: (props: {
      // Both dynamic segment names the table covers, rather than `{ id: string }`: the
      // fifth route's segment is `[parsedId]`, and the route is not the thing to bend
      // here. NOT `Record<string, string>` — a parameter type is contravariant, so the
      // index signature is not assignable to any route's narrower `Props` and `tsc`
      // rejects every importer. A sixth segment name is added here and nowhere else.
      params: Promise<{ id: string; parsedId: string }>;
      searchParams: Promise<SearchParams>;
    }) => Promise<Metadata>;
  }>;
}

const ROUTES: readonly DetailRoute[] = [
  {
    path: "/ansokningar/[id]",
    loader: getApplicationById,
    ownTitle: svPages.ansokningar.detail.meta.title,
    extraLoaderArgs: [],
    importer: () => import("./ansokningar/[id]/page"),
  },
  {
    path: "/jobb/[id]",
    loader: getJobAd,
    ownTitle: svPages.jobb.detail.meta.title,
    extraLoaderArgs: [],
    importer: () => import("./jobb/[id]/page"),
  },
  {
    path: "/cv/[id]/granska",
    loader: getResumeById,
    ownTitle: svPages.cv.granska.meta.title,
    extraLoaderArgs: [],
    importer: () => import("./cv/[id]/granska/page"),
  },
  {
    path: "/cv/granska/[parsedId]",
    loader: getParsedResume,
    ownTitle: svPages.cv.review.meta.title,
    extraLoaderArgs: [],
    importer: () => import("./cv/granska/[parsedId]/(view)/page"),
  },
  {
    path: "/foretag/smarta-bevakningar/[id]",
    loader: browseCriterionCompanies,
    ownTitle: svPages.foretag.smartaBevakningar.detail.meta.title,
    // The criterion browse is paginated, so its existence read takes the page too;
    // `parsePageParam(undefined)` is 1.
    extraLoaderArgs: [1],
    importer: () => import("./foretag/smarta-bevakningar/[id]/page"),
  },
];

const routeAt = (path: string): DetailRoute => {
  const route = ROUTES.find((candidate) => candidate.path === path);
  if (route === undefined) throw new Error(`no route ${path} in the table`);
  return route;
};

const resultFor = (kind: ApiResult<unknown>["kind"]) =>
  kind === "rateLimited" ? { kind, retryAfterSeconds: 60 } : { kind };

async function titleFor(
  route: DetailRoute,
  searchParams: SearchParams = {},
): Promise<Metadata["title"]> {
  const mod = await route.importer();
  expect(typeof mod.generateMetadata).toBe("function");
  const metadata = await mod.generateMetadata!({
    params: Promise.resolve({ id: ID, parsedId: ID }),
    searchParams: Promise.resolve(searchParams),
  });
  return metadata.title;
}

// --- the derivation -------------------------------------------------------------

const APP_DIR = dirname(fileURLToPath(import.meta.url));

/** A path segment starting with `@` is a parallel-route slot: no URL, no document. */
const isSlotSegment = (segment: string): boolean => segment.startsWith("@");

function pageFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) pageFiles(full, acc);
    else if (entry.name === "page.tsx") acc.push(full);
  }
  return acc;
}

/** `cv/granska/[parsedId]/(view)/page.tsx` → `/cv/granska/[parsedId]`. */
function routePath(file: string): string {
  const segments = file
    .split("/")
    .slice(0, -1)
    .filter((segment) => !/^\(.*\)$/.test(segment));
  return `/${segments.join("/")}`;
}

const allPages = pageFiles(APP_DIR)
  .map((file) => relative(APP_DIR, file).split(sep).join("/"))
  .filter((file) => !file.split("/").some(isSlotSegment));

/**
 * Every `(app)` page that CAN serve its own title over a "Sidan finns inte" body: its
 * render reaches `notFound()`, and its metadata export names a title of its own.
 *
 * Both halves are needed. The six retired CV routes reach `notFound()` too, but their
 * whole metadata is `notFoundMetadata()`, so they have no title of their own to serve
 * and are already correct. A page with a title but no `notFound()` never renders the
 * 404 body at all.
 */
const CANDIDATES = allPages
  .filter((file) => {
    const source = readFileSync(join(APP_DIR, file), "utf8");
    return /\bnotFound\(\)/.test(source) && declaresOwnTitle(source);
  })
  .map(routePath)
  .sort();

describe("(app) detail routes — the title resolves against the record's absence", () => {
  beforeEach(() => {
    for (const route of ROUTES) route.loader.mockReset();
  });

  it("states the composed 404 title a reader sees", () => {
    // In full, once: a copy change to either half is then visible here rather than only
    // inside the composition.
    expect(ABSENCE_TITLE).toBe("Sidan finns inte | Jobbliggaren");
  });

  it("covers every route that can serve its own title over a 404 body", () => {
    // Derived, not listed — a route added later inherits the defect in silence, and a
    // list cannot see that. `document-title-coverage.test.ts` would not catch it either:
    // it asks whether a page HAS a title, never whether that title survives the record
    // being gone.
    expect(
      allPages.length,
      "the walk found no pages, which would make the derivation vacuous"
    ).toBeGreaterThan(20);
    expect([...ROUTES].map((route) => route.path).sort()).toEqual(CANDIDATES);
  });

  it("counts a conditional delegation as an own title and an unconditional one as none", () => {
    // The control for the predicate that derives the set above, in both polarities.
    const conditional = [
      "export async function generateMetadata({ params }: Props): Promise<Metadata> {",
      "  const { id } = await params;",
      "  const result = await getApplicationById(id);",
      '  if (result.kind === "notFound") return notFoundMetadata();',
      "",
      '  const t = await getTranslations("pages");',
      '  return { title: t("ansokningar.detail.meta.title") };',
      "}",
    ].join("\n");
    const unconditional = [
      "export async function generateMetadata(): Promise<Metadata> {",
      "  return notFoundMetadata();",
      "}",
    ].join("\n");

    expect(declaresOwnTitle(conditional)).toBe(true);
    expect(declaresOwnTitle(unconditional)).toBe(false);
  });

  const cases = ROUTES.flatMap((route) =>
    (Object.keys(TITLES) as ApiResult<unknown>["kind"][]).map(
      (kind) => [route.path, kind, route] as const,
    ),
  );

  it.each(cases)("%s on %s titles the right document", async (_path, kind, route) => {
    route.loader.mockResolvedValue(resultFor(kind));

    const title = await titleFor(route);

    if (TITLES[kind] === "the absence") {
      // `absolute`, not a plain string: the root layout's `title.template` applies on
      // some 404 surfaces and not others, so the shared source opts out of it and ships
      // the composed string itself (`lib/metadata/not-found-title.ts`).
      expect(title).toEqual({ absolute: ABSENCE_TITLE });
    } else {
      expect(title).toBe(route.ownTitle);
    }
  });

  it.each(ROUTES.map((route) => [route.path, route] as const))(
    "%s asks the record's own authority, once",
    async (_path, route) => {
      // The branch is a data dependency. A version that returned the route title
      // without asking would satisfy every assertion above by never reaching the gate,
      // and a second call would be the extra round trip this PR measured that it does
      // NOT cost.
      route.loader.mockResolvedValue({ kind: "notFound" });

      await titleFor(route);

      expect(route.loader).toHaveBeenCalledTimes(1);
      expect(route.loader).toHaveBeenCalledWith(ID, ...route.extraLoaderArgs);
    },
  );

  it("asks the criterion browse for the page the reader asked for", () => {
    // `parsePageParam(undefined)` is 1, so every case above holds equally for the real
    // expression and for a literal `1` — the one route whose existence read takes more
    // than an id is pinned only where the two coincide. A literal would ask page 1 while
    // the page asks page 3: two backend calls where the measurement found one.
    const route = routeAt("/foretag/smarta-bevakningar/[id]");
    route.loader.mockResolvedValue({ kind: "notFound" });

    return titleFor(route, { page: "3" }).then(() => {
      expect(route.loader).toHaveBeenCalledWith(ID, 3);
    });
  });
});
