import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../../../messages/sv/pages.json";
import svJobads from "../../../../../../messages/sv/jobads.json";
import BevakningBrowsePage from "./page";

const browseCriterionCompanies = vi.fn();
const getCompanyWatchCriteria = vi.fn();
const getCriterionReference = vi.fn();
const getCriterionAdCount = vi.fn();

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: string) =>
    createTranslator({
      locale: "sv",
      messages: { pages: svPages, jobads: svJobads },
      namespace: namespace as
        | "pages"
        | "pages.foretag.criteria"
        | "jobads.companyWatches"
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
  browseCriterionCompanies: (...a: unknown[]) => browseCriterionCompanies(...a),
  getCompanyWatchCriteria: (...a: unknown[]) => getCompanyWatchCriteria(...a),
  getCriterionReference: (...a: unknown[]) => getCriterionReference(...a),
  getCriterionAdCount: (...a: unknown[]) => getCriterionAdCount(...a),
}));

vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
  notFound: () => {
    throw new Error("NEXT_NOT_FOUND");
  },
}));

vi.mock("@/lib/actions/company-follows", () => ({
  followCompanyAction: vi.fn(),
  unfollowCompanyAction: vi.fn(),
}));

const COMPANY = {
  organizationNumber: "5592804784",
  isProtectedIdentity: false,
  name: "Acme Bygg AB",
  seatMunicipalityCode: "0180",
  seatMunicipalityName: "Stockholm",
  sniCodes: [],
};

/**
 * #1149 — the CALL-SITE pin for `showTotalCount` on the criterion browse.
 *
 * `job-ad-pagination.test.tsx` proves the prop suppresses the total. It cannot prove this page
 * passes it, and this page is the surface where the omission is worst: its `totalCount` saturates
 * at `CompanyBrowseCriteria.MaxServableRows` exactly as `/foretag/sok`'s does, so a criterion
 * matching more companies than the cap would put a false "(2 000 träffar totalt)" directly beneath
 * a headline that honestly says "10 000+". Deleting the prop restores that with the suite green
 * unless something pins it here.
 */
describe("BevakningBrowsePage — the pager states no total", () => {
  beforeEach(() => {
    browseCriterionCompanies.mockReset();
    getCompanyWatchCriteria.mockReset();
    getCriterionReference.mockReset();
    getCompanyWatchCriteria.mockResolvedValue({ kind: "error" });
    getCriterionReference.mockResolvedValue({ kind: "error" });
    getCriterionAdCount.mockReset();
    getCriterionAdCount.mockResolvedValue({ kind: "error" });
  });

  it("renders the honest magnitude but never the saturated pagination total", async () => {
    // The shape the defect lives in: a criterion whose match set exceeds every cap. The magnitude
    // saturates at the product ceiling and says so with "+"; the pagination count saturates at the
    // servable cap and cannot say so at all.
    browseCriterionCompanies.mockResolvedValue({
      kind: "ok",
      data: {
        companies: { items: [COMPANY], page: 1, pageSize: 20, totalCount: 2000 },
        magnitude: { magnitude: 10000, saturated: true },
      },
    });

    render(
      await BevakningBrowsePage({
        params: Promise.resolve({ id: "c1" }),
        searchParams: Promise.resolve({}),
      }),
    );

    // The honest number is still there, suffix included.
    expect(
      screen.getByText("10 000+ företag matchar din bevakning"),
    ).toBeInTheDocument();

    // The page position survives — navigation, not a completeness claim.
    expect(screen.getByText("Sida 1 av 100")).toBeInTheDocument();

    // The ceiling must not be rendered as a total.
    expect(screen.queryByText(/träffar totalt/)).toBeNull();
    expect(screen.queryByText(/2\s?000\s+träffar/)).toBeNull();
  });

  // #1559 — the ad line has three arms and only one of them is a link. The link arm is what Klas
  // asked for; the other two exist so the surface never offers an empty page and never renders a
  // number the read did not produce (#859: a rendered magnitude is true or absent).
  const okBrowse = {
    kind: "ok",
    data: {
      companies: { items: [COMPANY], page: 1, pageSize: 20, totalCount: 1 },
      magnitude: { magnitude: 1, saturated: false },
    },
  };

  it("renders the ad count as a LINK to the ads when there are any", async () => {
    browseCriterionCompanies.mockResolvedValue(okBrowse);
    getCriterionAdCount.mockResolvedValue({
      kind: "ok",
      data: {
        ads: { magnitude: 167, saturated: false },
        matching: { count: null, tooBroad: false },
      },
    });

    render(
      await BevakningBrowsePage({
        params: Promise.resolve({ id: "c1" }),
        searchParams: Promise.resolve({}),
      }),
    );

    const link = screen.getByRole("link", {
      name: "167 aktiva annonser från dessa företag",
    });
    expect(link).toHaveAttribute("href", "/foretag/smarta-bevakningar/c1/annonser");
  });

  it("states zero without offering a link to an empty page", async () => {
    browseCriterionCompanies.mockResolvedValue(okBrowse);
    getCriterionAdCount.mockResolvedValue({
      kind: "ok",
      data: {
        ads: { magnitude: 0, saturated: false },
        matching: { count: null, tooBroad: false },
      },
    });

    render(
      await BevakningBrowsePage({
        params: Promise.resolve({ id: "c1" }),
        searchParams: Promise.resolve({}),
      }),
    );

    expect(
      screen.getByText("Inga aktiva annonser från dessa företag just nu."),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: /aktiva annonser från dessa företag/ }),
    ).toBeNull();
  });

  it("says the number cannot be shown rather than rendering a false 0", async () => {
    // The degraded arm. A `0` here would read as "this watch has no ads", which is a statement the
    // failed read did not support — the same defect as a wrong number.
    browseCriterionCompanies.mockResolvedValue(okBrowse);
    getCriterionAdCount.mockResolvedValue({ kind: "error" });

    render(
      await BevakningBrowsePage({
        params: Promise.resolve({ id: "c1" }),
        searchParams: Promise.resolve({}),
      }),
    );

    expect(
      screen.getByText(
        "Antalet annonser kan inte visas just nu. Ladda om sidan om en stund.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText(/^0 aktiva annonser/)).toBeNull();
  });
});

/**
 * #1656 (b) — the PERSONAL count on the criterion detail page.
 *
 * <para/> Klas's condition (2026-09-05) is that this works "på samma sätt som vanlig
 * företagsbevakning", so what is pinned is the FOUR-STATE gate that surface has, plus the one thing
 * this surface adds: a watch too broad to grade. Three of the four render no number, and the whole
 * point is that none of them renders a ZERO instead — a `0` would say "nothing matches you" where
 * the truth is "nothing was measured".
 *
 * <para/> The count is asserted on its rendered TEXT and its link TARGET, never on a class name: a
 * number that renders without reaching the matching view is the count-to-landing divergence this
 * arm exists to close (#1407, #1471).
 */
describe("BevakningBrowsePage — the personal match count", () => {
  const okBrowse = {
    kind: "ok",
    data: {
      companies: { items: [COMPANY], page: 1, pageSize: 20, totalCount: 1 },
      magnitude: { magnitude: 1, saturated: false },
    },
  };

  beforeEach(() => {
    browseCriterionCompanies.mockReset();
    getCompanyWatchCriteria.mockReset();
    getCriterionReference.mockReset();
    getCriterionAdCount.mockReset();
    browseCriterionCompanies.mockResolvedValue(okBrowse);
    getCompanyWatchCriteria.mockResolvedValue({ kind: "ok", data: [] });
    getCriterionReference.mockResolvedValue({ kind: "error" });
  });

  async function renderWith(matching: unknown) {
    getCriterionAdCount.mockResolvedValue({
      kind: "ok",
      data: { ads: { magnitude: 12, saturated: false }, matching },
    });
    render(
      await BevakningBrowsePage({
        params: Promise.resolve({ id: "c1" }),
        searchParams: Promise.resolve({}),
      }),
    );
  }

  it("renders the count as a link to the MATCHING view, not the whole ad list", async () => {
    await renderWith({ count: 9, tooBroad: false });

    const link = screen.getByRole("link", { name: /9 matchande annonser/ });
    // The axis is what makes the number true at its destination. Without it the link lands on all
    // twelve ads while the sentence beside it promises nine.
    expect(link).toHaveAttribute(
      "href",
      "/foretag/smarta-bevakningar/c1/annonser?visa=matchande",
    );
    expect(screen.getByText("9 matchande annonser just nu")).toBeInTheDocument();
  });

  it("states a zero without offering a link to an empty list", async () => {
    await renderWith({ count: 0, tooBroad: false });

    expect(screen.getByText("Inga matchande annonser just nu")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /matchande annonser/ })).toBeNull();
  });

  it("nudges instead of claiming zero when no occupation is stated", async () => {
    await renderWith({ count: null, tooBroad: false });

    expect(
      screen.getByText(/Du har inte angett vilka yrken du söker inom/),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ställ in matchning" })).toBeInTheDocument();
    expect(screen.queryByText(/Inga matchande annonser/)).toBeNull();
  });

  it("refuses the question for a watch too broad to grade, and renders no number", async () => {
    await renderWith({ count: null, tooBroad: true });

    expect(
      screen.getByText(/Bevakningen är för bred för att vi ska kunna räkna/),
    ).toBeInTheDocument();
    // Neither of the other two no-number arms, and above all not a zero: this watch was not
    // measured, its owner has not failed to state an occupation, and nothing matched zero ads.
    expect(screen.queryByText(/Inga matchande annonser/)).toBeNull();
    expect(screen.queryByText(/Du har inte angett vilka yrken/)).toBeNull();
    expect(screen.queryByRole("link", { name: /matchande annonser/ })).toBeNull();
  });

  it("says nothing about matching when the ad-count read degraded", async () => {
    // Both numbers arrive in one response, so a failed read leaves nothing to say that the
    // "cannot be shown" line does not already say. Silence beats a second error sentence.
    getCriterionAdCount.mockResolvedValue({ kind: "error" });
    render(
      await BevakningBrowsePage({
        params: Promise.resolve({ id: "c1" }),
        searchParams: Promise.resolve({}),
      }),
    );

    expect(screen.queryByText(/matchande annonser/)).toBeNull();
    expect(screen.queryByText(/för bred/)).toBeNull();
    expect(screen.queryByText(/Du har inte angett vilka yrken/)).toBeNull();
  });
});
