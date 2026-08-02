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
    // totalCount 45 over pageSize 20 = 3 pages, so the pager actually renders. At totalCount 1 it
    // returns null and every assertion about its summary line would pass vacuously.
    searchCompanies.mockResolvedValue({
      kind: "ok",
      data: {
        companies: { items: [COMPANY], page: 1, pageSize: 20, totalCount: 45 },
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

/**
 * #1149 — the browse-all / search split, rendered.
 *
 * `magnitude === null` is the ONE signal that distinguishes the two states, and it drives three
 * user-visible things at once: whether a count line exists, what a screen reader is told the table
 * is, and (with the pager) whether any number on the view claims to be a total. The wire contract
 * was mutation-verified when it was written; none of what the user actually sees was.
 *
 * These cases also pin that both locale catalogues moved in step: `resultsCountUnit` is read
 * through a real `createTranslator` over `messages/sv/pages.json`, so a key renamed in one file
 * and not the other, or a plural form dropped, fails here rather than rendering a raw key.
 */
describe("ForetagSokResults — browse-all carries NO number, a search carries one", () => {
  const respondWith = (magnitude: unknown, totalCount = 45) => {
    searchCompanies.mockResolvedValue({
      kind: "ok",
      data: {
        companies: { items: [COMPANY], page: 1, pageSize: 20, totalCount },
        magnitude,
      },
    });
  };

  beforeEach(() => {
    searchCompanies.mockReset();
    getCompanyWatchStatusByOrgNr.mockReset();
    getCompanyWatchStatusByOrgNr.mockResolvedValue([{ companyWatchId: null }]);
  });

  it("UNFILTERED: heading only, no count line, no total claim, browse-all table name", async () => {
    respondWith(null);

    render(
      await ForetagSokResults({
        namn: "",
        sni: [],
        kommun: [],
        page: 1,
        reference: REFERENCE,
      }),
    );

    // The heading is a label, not a statement — and it is the SAME string as in the filtered case.
    expect(
      screen.getByRole("heading", { level: 2, name: "Företag i registret" }),
    ).toBeInTheDocument();

    // No number anywhere on the view. This is the Blocker: the previous fix removed the honest
    // "10 000+" from the heading and left the saturated pagination total as the only quantity —
    // measured "Sida 1 av 100 (2000 träffar totalt)" against 743 654 active companies.
    expect(screen.queryByText(/träffar totalt/)).toBeNull();
    expect(screen.queryByText(/träff(ar)?$/)).toBeNull();

    // The page position survives; it is navigation, not a completeness claim.
    expect(screen.getByText("Sida 1 av 3")).toBeInTheDocument();

    // A screen reader must not be told these are search results when nothing was searched for.
    expect(
      screen.getByRole("table", { name: "Alla företag i registret" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("table", { name: "Företag som matchar sökningen" }),
    ).not.toBeInTheDocument();
  });

  it("FILTERED: same heading, count on its own line, still no total claim", async () => {
    respondWith({ magnitude: 1234, saturated: false });

    render(
      await ForetagSokResults({
        namn: "acme",
        sni: [],
        kommun: [],
        page: 1,
        reference: REFERENCE,
      }),
    );

    // Invariant heading — the number moved OUT of it, which is the whole point of the change.
    expect(
      screen.getByRole("heading", { level: 2, name: "Företag i registret" }),
    ).toBeInTheDocument();

    // Grouped with a non-breaking space by the sv formatter; the DOM normalizer folds it.
    expect(screen.getByText("1 234 träffar")).toBeInTheDocument();

    // The magnitude is the surface's number. The pager's own count must not appear beside it.
    expect(screen.queryByText(/träffar totalt/)).toBeNull();
    expect(screen.queryByText(/\b45\b/)).toBeNull();

    expect(
      screen.getByRole("table", { name: "Företag som matchar sökningen" }),
    ).toBeInTheDocument();
  });

  it("FILTERED, saturated: renders the honest ceiling, never the bare number", async () => {
    respondWith({ magnitude: 10000, saturated: true });

    render(
      await ForetagSokResults({
        namn: "a",
        sni: [],
        kommun: [],
        page: 1,
        reference: REFERENCE,
      }),
    );

    expect(screen.getByText("10 000+ träffar")).toBeInTheDocument();
  });

  it("FILTERED, exactly one: the Swedish plural selects the singular", async () => {
    // The number renders as a STRING ("10 000+" when saturated) while the plural selects on the
    // NUMBER — they are separate arguments for that reason, and this is what proves it.
    respondWith({ magnitude: 1, saturated: false });

    render(
      await ForetagSokResults({
        namn: "acme bygg ab",
        sni: [],
        kommun: [],
        page: 1,
        reference: REFERENCE,
      }),
    );

    expect(screen.getByText("1 träff")).toBeInTheDocument();
  });
});
