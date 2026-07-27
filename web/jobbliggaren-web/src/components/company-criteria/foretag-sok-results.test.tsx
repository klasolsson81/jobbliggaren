import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../messages/sv/pages.json";
import { ForetagSokResults } from "./foretag-sok-results";
import type { CriterionReference } from "@/lib/dto/company-criteria";

const searchCompanies = vi.fn();
const getCompanyWatchStatusByOrgNr = vi.fn();

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "pages") =>
    createTranslator({ locale: "sv", messages: { pages: svPages }, namespace }),
  getFormatter: async () => ({
    number: (n: number) => new Intl.NumberFormat("sv-SE").format(n),
  }),
}));

vi.mock("@/lib/api/company-search", () => ({
  searchCompanies: (...a: unknown[]) => searchCompanies(...a),
}));
vi.mock("@/lib/api/company-follows", () => ({
  getCompanyWatchStatusByOrgNr: (...a: unknown[]) =>
    getCompanyWatchStatusByOrgNr(...a),
}));
vi.mock("@/lib/actions/company-follows", () => ({
  followCompanyAction: vi.fn(),
  unfollowCompanyAction: vi.fn(),
}));
vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
}));

const REFERENCE: CriterionReference = {
  sniVersion: "2025",
  kommunVersion: "2025",
  sni: [],
  lan: [],
};

const COMPANY = {
  organizationNumber: "5592804784",
  isProtectedIdentity: false,
  name: "Acme Bygg AB",
  seatMunicipalityCode: "0180",
  seatMunicipalityName: "Stockholm",
  sniCodes: [],
};

/**
 * The CALL-SITE pin for the table's accessible name.
 *
 * `company-browse-list.test.tsx` proves the `labels` prop works. It cannot prove this surface passes
 * it — and without that, deleting the prop here silently restores "Företag som matchar bevakningen"
 * on a search page with the whole suite green. That is the exact inversion this wave keeps finding:
 * the rule pinned, the call site not.
 */
describe("ForetagSokResults — the table announces a SEARCH, not a bevakning", () => {
  beforeEach(() => {
    searchCompanies.mockReset();
    getCompanyWatchStatusByOrgNr.mockReset();
    getCompanyWatchStatusByOrgNr.mockResolvedValue([{ companyWatchId: null }]);
    searchCompanies.mockResolvedValue({
      kind: "ok",
      data: {
        companies: { items: [COMPANY], page: 1, pageSize: 20, totalCount: 1 },
        magnitude: { magnitude: 1, saturated: false },
      },
    });
  });

  it("passes its own labels to the shared register table", async () => {
    render(
      await ForetagSokResults({
        namn: "acme",
        sni: [],
        kommun: [],
        page: 1,
        reference: REFERENCE,
      }),
    );

    expect(
      screen.getByRole("table", { name: "Företag som matchar sökningen" }),
    ).toBeInTheDocument();
    // The criterion-browse default must not be what a screen reader hears here.
    expect(
      screen.queryByRole("table", { name: "Företag som matchar bevakningen" }),
    ).not.toBeInTheDocument();
  });

  it("renders the seat WITHOUT the SCB kommun code", async () => {
    render(
      await ForetagSokResults({
        namn: "acme",
        sni: [],
        kommun: [],
        page: 1,
        reference: REFERENCE,
      }),
    );

    expect(screen.getByText("Stockholm")).toBeInTheDocument();
    // It used to read "Stockholm (0180)" — mono decoration disambiguating nothing.
    expect(screen.queryByText(/\(0180\)/)).not.toBeInTheDocument();
  });
});
