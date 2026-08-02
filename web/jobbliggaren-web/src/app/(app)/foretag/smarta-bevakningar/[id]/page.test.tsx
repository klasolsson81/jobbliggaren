import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../../../messages/sv/pages.json";
import BevakningBrowsePage from "./page";

const browseCriterionCompanies = vi.fn();
const getCompanyWatchCriteria = vi.fn();
const getCriterionReference = vi.fn();

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
});
