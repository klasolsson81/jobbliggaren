import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../../../../messages/sv/pages.json";
import svJobads from "../../../../../../../messages/sv/jobads.json";
import type { JobAdDto } from "@/lib/dto/job-ads";
import BevakningAdsPage from "./page";

/**
 * `/foretag/branschbevakningar/[id]/annonser` — the per-card match mark (#1656 (a)).
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

/**
 * #1681 part 2 — the criterion's identity, composed onto the ad-browse response. Every fixture here
 * carries it, because the page reads `criterion.label` unguarded: it is a required member of the
 * response schema, not something the page defends against.
 */
const CRITERION = {
  id: "c1",
  sniCodes: ["62010"],
  municipalityCodes: ["0180"],
  label: "IT i Stockholm",
};

function okBrowse(items: JobAdDto[]) {
  return {
    kind: "ok" as const,
    data: {
      criterion: CRITERION,
      ads: { items, page: 1, pageSize: 20, totalCount: items.length },
      // #1681 part 2 — the magnitude now carries its two no-number flags on the wire, always
      // present and false in the ordinary answerable case.
      magnitude: { magnitude: items.length, saturated: false, tooBroad: false, notMaterialised: false },
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

  // The CALL-SITE pin for `CriterionBreadth` on THIS page — the sibling of the one in
  // `[id]/page.test.tsx`, and for the same reason: the component's own test proves it counts, but
  // only a pin here proves this page renders it. Delete the element and the suite stays green
  // otherwise. `CRITERION` is one leaf and one kommun, so the honest reading is the singular pair.
  it("renders how wide the watch is, above the headline", async () => {
    browseCriterionAds.mockResolvedValue(okBrowse([ad("a1", "Systemutvecklare")]));
    getMyProfile.mockResolvedValue({
      kind: "ok",
      data: { hasStatedDesiredOccupation: true },
    });

    await renderPage();

    const breadth = document.querySelector(".jp-criterion-breadth");
    expect(breadth).not.toBeNull();
    expect(breadth?.textContent?.replace(/\s+/g, " ").trim()).toBe(
      "1 bransch · 1 kommun",
    );
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
    ).toHaveAttribute("href", "/foretag/branschbevakningar/c1/annonser");
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

    // The consequence is stated: the list is unfiltered, not empty and not filtered.
    expect(
      screen.getByText(/så alla aktiva annonser visas här/),
    ).toBeInTheDocument();
    // The list is still there. An empty page here would say "nothing matches you", which is not
    // what a refusal means.
    expect(screen.getByText("Systemutvecklare")).toBeInTheDocument();
    expect(screen.queryByText(/matchande annonser$/)).toBeNull();
  });

  it("says the list is unfiltered when no occupation is stated", async () => {
    // The other INERT arm. Without the consequence clause the user asked for a filtered list, got
    // an unfiltered one, and nothing on the page accounts for the difference.
    await renderWithVisa("matchande", { count: null, tooBroad: false });

    expect(
      screen.getByText(/så alla aktiva annonser visas här/),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ställ in matchning" })).toBeInTheDocument();
    expect(screen.getByText("Systemutvecklare")).toBeInTheDocument();
  });

  it("explains an EMPTY filtered list rather than reusing the unfiltered empty state", async () => {
    browseCriterionAds.mockResolvedValue({
      kind: "ok" as const,
      data: {
        criterion: CRITERION,
        ads: { items: [], page: 1, pageSize: 20, totalCount: 0 },
        magnitude: { magnitude: 5, saturated: false, tooBroad: false, notMaterialised: false },
        matching: { count: 0, tooBroad: false },
      },
    });
    render(
      await BevakningAdsPage({
        params: Promise.resolve({ id: "c1" }),
        searchParams: Promise.resolve({ visa: "matchande" }),
      }),
    );

    // "The watch has no ads" and "none of its ads match you" are different facts, and the criterion
    // here HAS five ads.
    expect(screen.getByText("Inga annonser matchar dig just nu.")).toBeInTheDocument();
    expect(screen.queryByText("Inga aktiva annonser just nu.")).toBeNull();
  });

  it("keeps the axis on every pagination href", async () => {
    browseCriterionAds.mockResolvedValue({
      kind: "ok" as const,
      data: {
        criterion: CRITERION,
        ads: {
          items: [ad("a1", "Systemutvecklare")],
          page: 1,
          pageSize: 20,
          totalCount: 40,
        },
        magnitude: { magnitude: 40, saturated: false, tooBroad: false, notMaterialised: false },
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
      "/foretag/branschbevakningar/c1/annonser?page=2&visa=matchande",
    );
  });
});

/**
 * #1681 part 2 (ADR 0139) — the heading, and the read this page no longer makes.
 *
 * <para/> Parity the parent route: the title used to be resolved out of `GET
 * /company-watch-criteria`, and part 2 made every row of that list carry a materialised ad count
 * plus a per-user graded matching count. Fetching twenty criteria's graded counts to render one
 * string is what the CTO ordered removed (2026-09-07); the criterion's identity now rides the ad
 * browse this page already makes.
 *
 * <para/> The REMOVAL is what is pinned, because nothing else would notice it coming back: re-adding
 * the call resolves the same title and leaves every other test green. So the spy for the function
 * that must not be called is asserted directly, with the rendered heading as its positive twin — a
 * "never called" alone would also pass on a page that renders no title at all.
 */
describe("BevakningAdsPage — the heading, and the list read that is gone", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getCriterionReference.mockResolvedValue({ kind: "error" });
    getJobAdMatchTags.mockResolvedValue({ entries: {} });
    getMyProfile.mockResolvedValue({ kind: "ok", data: { hasStatedDesiredOccupation: true } });
    browseCriterionAds.mockResolvedValue(okBrowse([ad("a1", "Systemutvecklare")]));
  });

  it("heads the page from the composed criterion and never reads the criteria list", async () => {
    await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: "IT i Stockholm" }),
    ).toBeInTheDocument();
    expect(getCompanyWatchCriteria).not.toHaveBeenCalled();

    // The control for that negative. A spy that records nothing would satisfy "not called" for the
    // wrong reason, so the sibling read on the SAME module mock is asserted to have registered.
    expect(browseCriterionAds).toHaveBeenCalledTimes(1);
  });

  it("falls back to the neutral title for an unnamed watch, still without the list read", async () => {
    browseCriterionAds.mockResolvedValue({
      ...okBrowse([ad("a1", "Systemutvecklare")]),
      data: {
        ...okBrowse([ad("a1", "Systemutvecklare")]).data,
        criterion: { ...CRITERION, label: null },
      },
    });

    await renderPage();

    expect(screen.getByRole("heading", { level: 1, name: "Branschbevakning" })).toBeInTheDocument();
    expect(getCompanyWatchCriteria).not.toHaveBeenCalled();
  });
});

/**
 * #1681 part 2 — the two states in which this page has no list to show, and the empty state it must
 * NOT fall through to.
 *
 * <para/> When the criterion is too broad to materialise, or has not been materialised for its
 * current predicate, the browse comes back with an empty page and a magnitude carrying no number
 * (`BrowseCriterionAdsQueryHandler` returns an empty `PagedResult` in exactly those two arms). The
 * defect this fixes is what the page then did with it: suppressing its OWN empty block was not
 * enough, because `JobAdList` renders an unconditional one of its own — "Inga jobb hittades", with
 * a body telling the reader to adjust filters and clear the search box, neither of which exists on
 * this route (its only axis is `?visa=`). That made the false zero stronger AND gave advice that
 * cannot be followed.
 *
 * <para/> So three things are pinned per state: the state's OWN sentence is rendered, no zero is
 * claimed anywhere, and neither the route's ordinary empty state nor `JobAdList`'s reaches the page.
 * Pagination is asserted absent too — with `totalCount: 0` it would also be absent from the
 * fall-through, so the discriminating assertions are the two empty blocks, and this one guards the
 * branch's other half rather than doing the work alone.
 */
describe("BevakningAdsPage — a watch with no answer has no list", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getCriterionReference.mockResolvedValue({ kind: "error" });
    getJobAdMatchTags.mockResolvedValue({ entries: {} });
    getMyProfile.mockResolvedValue({ kind: "ok", data: { hasStatedDesiredOccupation: true } });
  });

  /** The shape the handler returns in both unanswerable arms: an empty page, no number. */
  function unanswerable(flag: "tooBroad" | "notMaterialised", matching: unknown) {
    return {
      kind: "ok" as const,
      data: {
        criterion: CRITERION,
        ads: { items: [], page: 1, pageSize: 20, totalCount: 0 },
        magnitude: {
          magnitude: null,
          saturated: false,
          tooBroad: flag === "tooBroad",
          notMaterialised: flag === "notMaterialised",
        },
        matching,
      },
    };
  }

  /** Neither empty state may reach the page, and no pagination with them. */
  function expectNoListAndNoPagination() {
    // The route's own empty state — "the watch has no ads" — which is a zero this page has just
    // declined to claim.
    expect(screen.queryByText("Inga aktiva annonser just nu.")).toBeNull();
    // JobAdList's unconditional one, and the advice about controls this route does not have.
    expect(screen.queryByText("Inga jobb hittades")).toBeNull();
    expect(screen.queryByText(/Justera filtren eller töm sökrutan/)).toBeNull();
    // No ad rows and no pager.
    expect(screen.queryByRole("heading", { name: "Systemutvecklare" })).toBeNull();
    expect(screen.queryByText(/^Sida \d+ av/)).toBeNull();
  }

  it("refuses the too-broad watch in the heading and explains it once below, with a way out", async () => {
    browseCriterionAds.mockResolvedValue(
      unanswerable("tooBroad", { count: null, tooBroad: true, notMaterialised: false }),
    );

    await renderPage();

    // The h2 is a NOUN PHRASE, not an instruction: a screen reader navigating by heading should not
    // be read a two-sentence explanation (WCAG 2.4.6). The explanation is the block below it.
    expect(
      screen.getByRole("heading", { level: 2, name: "Antalet annonser kan inte räknas" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        "Bevakningen matchar fler företag än vi kan räkna annonser för, så det finns ingen lista att visa.",
      ),
    ).toBeInTheDocument();

    // The two ways on: back to the watch, and to the place the watch can be narrowed. The back
    // link appears TWICE by design — the route's standing back link at the top, and the primary
    // action inside the block — so both are asserted rather than one being picked arbitrarily.
    const back = screen.getAllByRole("link", { name: "Tillbaka till bevakningen" });
    expect(back).toHaveLength(2);
    for (const link of back) {
      expect(link).toHaveAttribute("href", "/foretag/branschbevakningar/c1");
    }
    expect(screen.getByRole("link", { name: "Ändra bevakningen" })).toHaveAttribute(
      "href",
      "/foretag/branschbevakningar",
    );

    // The consequence clause is gone with the list it described. "…så alla aktiva annonser visas
    // här" was true while the inert-filter arm fell through to a register-backed browse; there is
    // no list here now, so the sentence would contradict the page under it.
    expect(screen.queryByText(/så alla aktiva annonser visas här/)).toBeNull();
    expectNoListAndNoPagination();
  });

  it("says the numbers are not computed yet, and offers no advice that cannot work", async () => {
    browseCriterionAds.mockResolvedValue(
      unanswerable("notMaterialised", { count: null, tooBroad: false, notMaterialised: true }),
    );

    await renderPage();

    expect(
      screen.getByRole("heading", { level: 2, name: "Annonserna är inte framräknade än" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        "Bevakningen är inte framräknad än, så det finns inga annonser att visa här.",
      ),
    ).toBeInTheDocument();

    // Ignorance, not refusal: the way back is offered, the "narrow it" nudge is NOT — the watch is
    // not too broad and narrowing it would not make the number arrive sooner.
    expect(screen.getAllByRole("link", { name: "Tillbaka till bevakningen" })).toHaveLength(2);
    expect(screen.queryByRole("link", { name: "Ändra bevakningen" })).toBeNull();
    expect(screen.queryByText(/matchar fler företag/)).toBeNull();

    // #1681 — this sentence IS the block's body in the not-materialised state. It is written for
    // THIS surface (a page opened to see a list) where adsNotMaterialised talks about figures, so it
    // must be PRESENT here rather than absent (design-reviewer Minor B).
    expect(
      screen.getByText(/så det finns inga annonser att visa här/),
    ).toBeInTheDocument();
    expectNoListAndNoPagination();
  });

  it("renders the two unanswerable states as DIFFERENT sentences, never one shared non-answer", async () => {
    // The distinction is the whole reason both flags exist: a refusal the user can act on against
    // an ignorance that resolves itself. Collapsing them would send the not-materialised user off to
    // narrow a watch that is not too broad.
    browseCriterionAds.mockResolvedValue(
      unanswerable("tooBroad", { count: null, tooBroad: true, notMaterialised: false }),
    );
    await renderPage();
    const broadHeading = screen.getByRole("heading", { level: 2 }).textContent;

    cleanup();

    browseCriterionAds.mockResolvedValue(
      unanswerable("notMaterialised", { count: null, tooBroad: false, notMaterialised: true }),
    );
    await renderPage();
    const notYetHeading = screen.getByRole("heading", { level: 2 }).textContent;

    expect(broadHeading).not.toBe(notYetHeading);
    // And neither of them is a number — least of all a zero.
    expect(broadHeading).not.toMatch(/\d/);
    expect(notYetHeading).not.toMatch(/\d/);
  });
});
