import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../../../../messages/sv/pages.json";
import svJobads from "../../../../../../../messages/sv/jobads.json";
import type { JobAdDto } from "@/lib/dto/job-ads";
import BevakningAdsPage from "./page";

/**
 * `/foretag/smarta-bevakningar/[id]/annonser` — the per-card match mark (#1656 (a)).
 *
 * <para/> What is pinned is the THREE-STATE gate and the fetch discipline around it, because the
 * list renders identically in every arm and nothing else would notice a collapse: a stated
 * occupation → `/jobb`'s chip on the ad that earned one; a profile that states none → the nudge and
 * no fetch; a FAILED profile read → neither, since a nudge there would assert something the page
 * does not know. The batch is called exactly once per page, after the browse, with
 * `includeRelated === false` — this route has no `?relaterade=` axis.
 *
 * <para/> The chip is asserted on its rendered TEXT (the accessible name), never on a class. Its
 * absence is asserted against every grade label the messages file carries, derived from the file
 * rather than enumerated here, so a sixth rung could not slip past the negative arms.
 */

const browseCriterionAds = vi.fn();
const getCompanyWatchCriteria = vi.fn();
const getCriterionReference = vi.fn();
const getMyProfile = vi.fn();
const getJobAdMatchTags = vi.fn();

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: string) =>
    createTranslator({
      locale: "sv",
      messages: { pages: svPages, jobads: svJobads },
      namespace: namespace as
        | "pages"
        | "pages.foretag.criteria"
        | "jobads.ui.match"
        | undefined,
    }),
  getFormatter: async () => ({
    number: (n: number) => new Intl.NumberFormat("sv-SE").format(n),
  }),
}));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: async () => ({ userId: "u1" }),
}));

vi.mock("@/lib/api/company-criteria", () => ({
  browseCriterionAds: (...a: unknown[]) => browseCriterionAds(...a),
  getCompanyWatchCriteria: (...a: unknown[]) => getCompanyWatchCriteria(...a),
  getCriterionReference: (...a: unknown[]) => getCriterionReference(...a),
}));

vi.mock("@/lib/api/me", () => ({
  getMyProfile: () => getMyProfile(),
}));

vi.mock("@/lib/api/job-ad-match", () => ({
  getJobAdMatchTags: (...a: unknown[]) => getJobAdMatchTags(...a),
}));

vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
  notFound: () => {
    throw new Error("NEXT_NOT_FOUND");
  },
}));

// Chrome with dialog needs of its own; says nothing about what is measured here.
vi.mock("@/components/common/info-dialog", () => ({ InfoDialog: () => null }));

function ad(id: string, title: string): JobAdDto {
  return {
    id,
    title,
    companyName: "Volvo AB",
    url: "https://example.test/ad",
    source: "Platsbanken",
    status: "Active",
    publishedAt: "2026-09-01T10:00:00Z",
    expiresAt: null,
    createdAt: "2026-09-01T10:00:00Z",
  };
}

function okBrowse(items: JobAdDto[]) {
  return {
    kind: "ok" as const,
    data: {
      ads: { items, page: 1, pageSize: 20, totalCount: items.length },
      magnitude: { magnitude: items.length, saturated: false },
      // #1656 (b) — `null` is "the caller did not ask for the matching view", which is every arm in
      // this class. The member is always PRESENT on the wire (the schema declares it nullable, never
      // optional), so the fixture carries it rather than leaving it undefined.
      matching: null,
    },
  };
}

/** Every Swedish grade label the chip can render, read from the messages file. */
const GRADE_LABELS: string[] = Object.values(svJobads.ui.match.grade);

function expectNoChip() {
  for (const label of GRADE_LABELS) {
    expect(screen.queryByText(label)).toBeNull();
  }
}

async function renderPage() {
  render(
    await BevakningAdsPage({
      params: Promise.resolve({ id: "c1" }),
      searchParams: Promise.resolve({}),
    }),
  );
}

describe("BevakningAdsPage — the per-card match mark", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getCompanyWatchCriteria.mockResolvedValue({ kind: "error" });
    getCriterionReference.mockResolvedValue({ kind: "error" });
    // The batch's own degradation value, and what the handler emits for a page where no ad
    // earned a grade: an empty positive-only map.
    getJobAdMatchTags.mockResolvedValue({ entries: {} });
  });

  it("paints /jobb's chip on the ad that earned a grade, and asks once with includeRelated off", async () => {
    browseCriterionAds.mockResolvedValue(
      okBrowse([ad("a1", "Systemutvecklare"), ad("a2", "Ekonomiassistent")]),
    );
    getMyProfile.mockResolvedValue({
      kind: "ok",
      data: { hasStatedDesiredOccupation: true },
    });
    // Positive-only, keyed by ad id, one entry per ad that earned a grade — the shape
    // GetJobAdMatchBatchQueryHandler emits. a2 is absent, not "Basic".
    getJobAdMatchTags.mockResolvedValue({
      entries: {
        a1: {
          grade: "Good",
          ssykOverlap: "Match",
          titleSimilarity: "NotAssessed",
          regionFit: "Match",
          employmentFit: "NotAssessed",
          skillOverlap: "NotAssessed",
          mustHaveCoverage: "NotAssessed",
          niceToHaveCoverage: "NotAssessed",
        },
      },
    });

    await renderPage();

    expect(getJobAdMatchTags).toHaveBeenCalledTimes(1);
    expect(getJobAdMatchTags).toHaveBeenCalledWith(["a1", "a2"], false);

    // The chip lives inside the title heading, so the accessible name carries it — on a1 only.
    // Inline siblings concatenate without a separator, so the join is matched loosely; the
    // ORDER (title, then grade) and the exact bare name on a2 are what is pinned.
    expect(screen.getAllByText("Bra match")).toHaveLength(1);
    expect(
      screen.getByRole("heading", { name: /^Systemutvecklare\s*Bra match$/ }),
    ).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Ekonomiassistent" })).toBeInTheDocument();

    // No nudge when an occupation is stated.
    expect(screen.queryByText(/inte angett vilka yrken/)).toBeNull();
  });

  it("without a stated occupation renders the nudge with its CTA, no chip, and never asks", async () => {
    browseCriterionAds.mockResolvedValue(okBrowse([ad("a1", "Systemutvecklare")]));
    getMyProfile.mockResolvedValue({
      kind: "ok",
      data: { hasStatedDesiredOccupation: false },
    });

    await renderPage();

    expect(getJobAdMatchTags).not.toHaveBeenCalled();

    // /jobb's own sentence — "hur väl annonser matchar din profil" — never the follow dialog's
    // "för att se matchande annonser", which promises a set this page does not render.
    expect(
      screen.getByText(/för att se hur väl annonser matchar din profil/),
    ).toBeInTheDocument();
    expect(screen.queryByText(/för att se matchande annonser/)).toBeNull();
    expect(screen.getByRole("link", { name: "Ställ in matchning" })).toHaveAttribute(
      "href",
      "/installningar#matchning",
    );

    // The list is still there — the nudge sits above it, it does not replace it.
    expect(screen.getByRole("heading", { name: "Systemutvecklare" })).toBeInTheDocument();
    expectNoChip();
  });

  it("after a failed profile read renders neither the nudge nor a chip, and never asks", async () => {
    browseCriterionAds.mockResolvedValue(okBrowse([ad("a1", "Systemutvecklare")]));
    getMyProfile.mockResolvedValue({ kind: "error" });

    await renderPage();

    expect(getJobAdMatchTags).not.toHaveBeenCalled();
    // A nudge here would tell the user they have stated no occupation — which the page does not
    // know. Silence is the honest arm, and the list must survive it.
    expect(screen.queryByText(/inte angett vilka yrken/)).toBeNull();
    expect(screen.queryByRole("link", { name: "Ställ in matchning" })).toBeNull();
    expect(screen.getByRole("heading", { name: "Systemutvecklare" })).toBeInTheDocument();
    expectNoChip();
  });

  it("with a stated occupation but an empty batch renders the list bare, with no error surface", async () => {
    browseCriterionAds.mockResolvedValue(okBrowse([ad("a1", "Systemutvecklare")]));
    getMyProfile.mockResolvedValue({
      kind: "ok",
      data: { hasStatedDesiredOccupation: true },
    });
    // `getJobAdMatchTags` already collapses !ok / throw / no session to this value, so an empty
    // map is the ONE shape the page ever sees for "nothing to paint" — no error arm exists.
    getJobAdMatchTags.mockResolvedValue({ entries: {} });

    await renderPage();

    expect(getJobAdMatchTags).toHaveBeenCalledWith(["a1"], false);
    expect(screen.getByRole("heading", { name: "Systemutvecklare" })).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByText(/inte angett vilka yrken/)).toBeNull();
    expectNoChip();
  });

  it("on an empty page renders the empty state and never asks for grades", async () => {
    browseCriterionAds.mockResolvedValue(okBrowse([]));
    getMyProfile.mockResolvedValue({
      kind: "ok",
      data: { hasStatedDesiredOccupation: true },
    });

    await renderPage();

    expect(getJobAdMatchTags).not.toHaveBeenCalled();
    expect(screen.getByText("Inga aktiva annonser just nu.")).toBeInTheDocument();
    expect(screen.queryByText(/inte angett vilka yrken/)).toBeNull();
  });
});

/**
 * #1656 (b) — the `?visa=matchande` axis.
 *
 * <para/> What is pinned is that the axis reaches the BACKEND (the filter is a set question the
 * page cannot answer itself), that the headline then reads the personal count rather than the
 * pagination `totalCount`, that pagination carries the axis, and that the two INERT arms deliver
 * the unfiltered list with an explanation instead of an empty page or a false zero.
 *
 * <para/> The axis value is asserted literally. `baraMatchade` means a WIDER band on `/jobb`
 * (Grund and Relaterat included) than the `>= Good` this count is computed at, so the two names
 * must not converge.
 */
describe("BevakningAdsPage — the matching view", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getCompanyWatchCriteria.mockResolvedValue({ kind: "error" });
    getCriterionReference.mockResolvedValue({ kind: "error" });
    getJobAdMatchTags.mockResolvedValue({ entries: {} });
    getMyProfile.mockResolvedValue({
      kind: "ok",
      data: { hasStatedDesiredOccupation: true },
    });
  });

  function browseWith(items: JobAdDto[], matching: unknown) {
    return { ...okBrowse(items), data: { ...okBrowse(items).data, matching } };
  }

  async function renderWithVisa(visa: string | undefined, matching: unknown) {
    browseCriterionAds.mockResolvedValue(browseWith([ad("a1", "Systemutvecklare")], matching));
    render(
      await BevakningAdsPage({
        params: Promise.resolve({ id: "c1" }),
        searchParams: Promise.resolve(visa === undefined ? {} : { visa }),
      }),
    );
  }

  it("asks the backend for the matching set and heads the page with THAT count", async () => {
    await renderWithVisa("matchande", { count: 9, tooBroad: false });

    expect(browseCriterionAds).toHaveBeenCalledWith("c1", 1, true);
    // Nine, not the one row this page happens to hold: the headline is the whole matching set.
    expect(screen.getByText("9 matchande annonser")).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "Visa alla annonser" }),
    ).toHaveAttribute("href", "/foretag/smarta-bevakningar/c1/annonser");
  });

  it("does not filter when the axis is absent", async () => {
    await renderWithVisa(undefined, null);

    expect(browseCriterionAds).toHaveBeenCalledWith("c1", 1, false);
    expect(screen.queryByText(/matchande annonser/)).toBeNull();
  });

  it("degrades an unrecognised axis value to the whole list", async () => {
    // A filter nobody asked for must never appear. `baraMatchade` is specifically NOT this axis.
    await renderWithVisa("baraMatchade", null);

    expect(browseCriterionAds).toHaveBeenCalledWith("c1", 1, false);
  });

  it("delivers the unfiltered list with an explanation when the watch is too broad", async () => {
    await renderWithVisa("matchande", { count: null, tooBroad: true });

    expect(
      screen.getByText(/Bevakningen är för bred för att vi ska kunna räkna/),
    ).toBeInTheDocument();
    // The list is still there. An empty page here would say "nothing matches you", which is not
    // what a refusal means.
    expect(screen.getByText("Systemutvecklare")).toBeInTheDocument();
    expect(screen.queryByText(/matchande annonser$/)).toBeNull();
  });

  it("keeps the axis on every pagination href", async () => {
    browseCriterionAds.mockResolvedValue({
      kind: "ok" as const,
      data: {
        ads: {
          items: [ad("a1", "Systemutvecklare")],
          page: 1,
          pageSize: 20,
          totalCount: 40,
        },
        magnitude: { magnitude: 40, saturated: false },
        matching: { count: 40, tooBroad: false },
      },
    });
    render(
      await BevakningAdsPage({
        params: Promise.resolve({ id: "c1" }),
        searchParams: Promise.resolve({ visa: "matchande" }),
      }),
    );

    // Page 2 without the axis would quietly show more ads than page 1 promised.
    const next = screen.getAllByRole("link").find((a) => a.getAttribute("href")?.includes("page=2"));
    expect(next).toBeDefined();
    expect(next!.getAttribute("href")).toBe(
      "/foretag/smarta-bevakningar/c1/annonser?page=2&visa=matchande",
    );
  });
});
