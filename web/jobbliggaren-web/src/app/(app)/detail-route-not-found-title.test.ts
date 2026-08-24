import { describe, it, expect, vi, beforeEach } from "vitest";
import { createTranslator } from "next-intl";
import type { Metadata } from "next";
import type { ApiResult } from "@/lib/dto/_helpers";
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
 * PREMISE (§5 `Tests:`). The mocked loaders stand in for the real ones, and
 * `{ kind: "notFound" }` is a value those real adapters emit on two paths rather than a
 * shape invented here: the pre-flight GUID guard returns it for a malformed id
 * (`if (!isValidId(id)) return { kind: "notFound" }` in `lib/api/job-ads.ts`, and the
 * same guard in its siblings), and `responseToResult(..., { includeNotFound: true })`
 * maps the endpoint's genuine 404 to it. Every assertion below rests on the
 * discriminator alone — `generateMetadata` reads `result.kind` and nothing else on all
 * four routes — so no `ok` body is fabricated.
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

interface DetailRoute {
  readonly path: string;
  readonly loader: ReturnType<typeof vi.fn>;
  readonly ownTitle: string;
  /** Arguments the route's existence read takes after the id; empty for three of four. */
  readonly extraLoaderArgs: readonly unknown[];
  readonly importer: () => Promise<{
    generateMetadata?: (props: {
      params: Promise<{ id: string }>;
      searchParams: Promise<Record<string, string | undefined>>;
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
    path: "/foretag/smarta-bevakningar/[id]",
    loader: browseCriterionCompanies,
    ownTitle: svPages.foretag.smartaBevakningar.detail.meta.title,
    // The criterion browse is paginated, so its existence read takes the page too;
    // `parsePageParam(undefined)` is 1.
    extraLoaderArgs: [1],
    importer: () => import("./foretag/smarta-bevakningar/[id]/page"),
  },
];

const resultFor = (kind: ApiResult<unknown>["kind"]) =>
  kind === "rateLimited" ? { kind, retryAfterSeconds: 60 } : { kind };

async function titleFor(route: DetailRoute): Promise<Metadata["title"]> {
  const mod = await route.importer();
  expect(typeof mod.generateMetadata).toBe("function");
  const metadata = await mod.generateMetadata!({
    params: Promise.resolve({ id: ID }),
    searchParams: Promise.resolve({}),
  });
  return metadata.title;
}

describe("(app) detail routes — the title resolves against the record's absence", () => {
  beforeEach(() => {
    for (const route of ROUTES) route.loader.mockReset();
  });

  it("states the composed 404 title a reader sees", () => {
    // In full, once: a copy change to either half is then visible here rather than only
    // inside the composition.
    expect(ABSENCE_TITLE).toBe("Sidan finns inte | Jobbliggaren");
  });

  it("reaches every detail route the defect covered", () => {
    // Guards the table itself. Four routes gained a title in #1506 and each of them
    // served it over a missing record; a table that silently lost one would make the
    // assertions below pass while the surface regressed.
    expect(ROUTES.map((route) => route.path)).toEqual([
      "/ansokningar/[id]",
      "/jobb/[id]",
      "/cv/[id]/granska",
      "/foretag/smarta-bevakningar/[id]",
    ]);
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
      // and a second call would be the round trip this PR measured away.
      route.loader.mockResolvedValue({ kind: "notFound" });

      await titleFor(route);

      expect(route.loader).toHaveBeenCalledTimes(1);
      expect(route.loader).toHaveBeenCalledWith(ID, ...route.extraLoaderArgs);
    },
  );
});
