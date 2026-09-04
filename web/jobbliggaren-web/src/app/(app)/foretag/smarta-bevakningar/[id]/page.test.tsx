import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../../../messages/sv/pages.json";
import BevakningBrowsePage from "./page";

const browseCriterionCompanies = vi.fn();
const getCompanyWatchCriteria = vi.fn();
const getCriterionReference = vi.fn();
const getCriterionAdCount = vi.fn();

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "pages") =>
    createTranslator({ locale: "sv", messages: { pages: svPages }, namespace }),
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
      data: { magnitude: 167, saturated: false },
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
      data: { magnitude: 0, saturated: false },
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
