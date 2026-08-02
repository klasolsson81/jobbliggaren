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
    // One match, so `totalCount` is 1 — production derives it from the same predicate as the
    // magnitude and caps it, so the two agree below the cap. This block asserts table labels and
    // never the pager, so it has no reason to want more pages than the fixture describes.
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
  /**
   * `totalCount` is NOT free to choose. `BuildCountCommand` and `BuildMagnitudeCommand` are the
   * same statement modulo their caps, so production always sends
   * `totalCount = min(magnitude, MaxServableRows)` — 2 000 at pageSize 20. A fixture that pairs
   * magnitude 1 234 with totalCount 45 asserts against a response no `src/` path can emit, and any
   * assertion resting on the two being different is unreadable as evidence about production.
   */
  const respondWith = (
    magnitude: { readonly magnitude: number; readonly saturated: boolean } | null,
  ) => {
    const SERVABLE_CAP = 2000; // MAX_PAGE (100) × pageSize (20)
    searchCompanies.mockResolvedValue({
      kind: "ok",
      data: {
        companies: {
          items: [COMPANY],
          page: 1,
          pageSize: 20,
          // Browse-all matches the whole register, so its page count always saturates.
          totalCount:
            magnitude === null
              ? SERVABLE_CAP
              : Math.min(magnitude.magnitude, SERVABLE_CAP),
        },
        magnitude,
      },
    });
  };

  beforeEach(() => {
    searchCompanies.mockReset();
    getCompanyWatchStatusByOrgNr.mockReset();
    getCompanyWatchStatusByOrgNr.mockResolvedValue([{ companyWatchId: null }]);
  });

  it("UNFILTERED: no magnitude claim at all, and the browse-all table name", async () => {
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

    // No count line exists. Binding to the live region rather than to the text makes this a real
    // assertion: the `magnitude !== null` guard cannot be mutated into a green suite (dropping it
    // crashes, and tsc rejects it), so without this the guard was only crash-pinned.
    expect(screen.queryByRole("status")).toBeNull();

    // This is the Blocker, in the state that produced it. The previous fix removed the honest
    // "10 000+" and left the saturated pagination total as the only quantity on the view —
    // measured "Sida 1 av 100 (2000 träffar totalt)" against 743 654 active companies.
    expect(screen.queryByText(/träffar totalt/)).toBeNull();

    // The page position survives; it is navigation, not a completeness claim. 100 pages because
    // browse-all always saturates the servable cap — this is the real production number.
    expect(screen.getByText("Sida 1 av 100")).toBeInTheDocument();

    // The view is not silent about the cap, though. A quantity that says how far you can BROWSE is
    // not a claim about how many companies exist, and leaving 2 000 pages unexplained beside a
    // register of 743 654 is its own dishonesty.
    expect(
      screen.getByText(/Du kan bläddra bland de 2 000 första företagen/),
    ).toBeInTheDocument();

    // A screen reader must not be told these are search results when nothing was searched for.
    expect(
      screen.getByRole("table", { name: "Företag i registret" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("table", { name: "Företag som matchar sökningen" }),
    ).not.toBeInTheDocument();
  });

  it("FILTERED: same heading, count in its own live region, no total claim, no ceiling copy", async () => {
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

    // The number sits in a `<b>` inside the count line, so the string exists in no single text
    // node; bind to the live region. `role="status"` is load-bearing on its own — the search
    // commits with `router.push`, so without it the result of the user's action is announced to
    // nobody (WCAG 4.1.3).
    expect(screen.getByRole("status")).toHaveTextContent("1 234 träffar");

    // The magnitude is the surface's number. The pager must not put a second one beside it.
    expect(screen.queryByText(/träffar totalt/)).toBeNull();

    // Under the cap there is nothing to explain, so the ceiling copy stays away.
    expect(screen.queryByText(/Du kan bläddra bland/)).toBeNull();

    expect(
      screen.getByRole("table", { name: "Företag som matchar sökningen" }),
    ).toBeInTheDocument();
  });

  it("FILTERED, saturated: the honest ceiling above, the browse limit below, never conflated", async () => {
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

    // Two different ceilings, both true, and the reason they may sit on one screen: 10 000+ is how
    // many companies match, 2 000 is how many you can page through. Conflating them is the whole
    // defect class this PR exists for.
    expect(screen.getByRole("status")).toHaveTextContent("10 000+ träffar");
    expect(
      screen.getByText(/Du kan bläddra bland de 2 000 första företagen/),
    ).toBeInTheDocument();
    expect(screen.queryByText(/träffar totalt/)).toBeNull();
  });

  it("FILTERED, exactly at the cap: every match is reachable, so no ceiling copy", async () => {
    // The boundary both reviewers found independently. `totalCount` is itself capped at 2 000, so
    // gating the ceiling copy on it cannot tell "exactly 2 000 matches, all reachable" apart from
    // "more than 2 000, some lost" — and in the first case "Avgränsa sökningen för att hitta fler"
    // asserts that more exist when none do. The magnitude is exact up to its own ceiling, which is
    // why the gate reads it instead.
    respondWith({ magnitude: 2000, saturated: false });

    render(
      await ForetagSokResults({
        namn: "ab",
        sni: [],
        kommun: [],
        page: 1,
        reference: REFERENCE,
      }),
    );

    expect(screen.getByRole("status")).toHaveTextContent("2 000 träffar");
    expect(screen.queryByText(/Du kan bläddra bland/)).toBeNull();
    // Still 100 pages — every one of them reachable, which is exactly why nothing needs explaining.
    expect(screen.getByText("Sida 1 av 100")).toBeInTheDocument();
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

    expect(screen.getByRole("status")).toHaveTextContent("1 träff");
  });
});
