import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../messages/sv/pages.json";
import { ForetagSokResults } from "./foretag-sok-results";
import { Announcer } from "@/components/common/announcer";
import { ForetagSokResultsSkeleton } from "./foretag-sok-results-skeleton";
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

  /**
   * The VISIBLE count line. It used to be readable as `getByRole("status")`, because the element
   * that renders the number was itself the live region — the shape #1092 removed: a region born
   * holding its own text is not reliably announced (ARIA22). The number now renders here and is
   * announced from the surface's persistent region instead, so these cases bind to the class.
   *
   * They still cannot bind to the text: the figure sits in a `<b>` inside the line, so the whole
   * sentence exists in no single text node. What is asserted is unchanged — WHICH number the
   * surface renders — and the announcement itself is pinned separately below.
   */
  const countLine = () => {
    const line = document.querySelector(".jp-results-count");
    if (line === null) throw new Error("no count line rendered");
    return line;
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

    // No count line exists. Binding to the class rather than to the text keeps this a real
    // assertion: the `magnitude !== null` guard cannot be mutated into a green suite (dropping it
    // crashes, and tsc rejects it), so without this the guard was only crash-pinned.
    expect(document.querySelector(".jp-results-count")).toBeNull();

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

  it("FILTERED: same heading, count on its own line, no total claim, no ceiling copy", async () => {
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
    // node. The search commits with `router.push`, so the result of the user's action still has to
    // reach a screen reader (WCAG 4.1.3) — that half moved to the surface region and is pinned in
    // its own block below; this asserts which number is rendered.
    expect(countLine()).toHaveTextContent("1 234 träffar");

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
    expect(countLine()).toHaveTextContent("10 000+ träffar");
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

    expect(countLine()).toHaveTextContent("2 000 träffar");
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

    expect(countLine()).toHaveTextContent("1 träff");
  });
});

/**
 * #1092 — the END of the load, announced. Klas fell the WCAG 4.1.3 verdict on 2026-08-24: Major,
 * fixed in-block, and the count IS to be announced.
 *
 * These render inside `Announcer`, which is how the page composes them. Without the
 * wrapper `Announce` is inert by design, so the assertions below would pass vacuously against a
 * results tree that announced nothing — the wrapper is the production shape, not test scaffolding.
 *
 * Three cases because W3C's Understanding document names three different sentences, and the
 * browse-all one is the case its own rule does NOT force: it renders no number by #1149, so
 * nothing here is obliged to exist. It exists anyway, because announcing "Söker företag…" and then
 * never closing it leaves a screen reader waiting on a load that finished.
 */
describe("ForetagSokResults — the load's completion reaches the surface region", () => {
  const respondWith = (
    magnitude: { readonly magnitude: number; readonly saturated: boolean } | null,
    items: ReadonlyArray<typeof COMPANY> = [COMPANY],
  ) => {
    searchCompanies.mockResolvedValue({
      kind: "ok",
      data: {
        companies: {
          items,
          page: 1,
          pageSize: 20,
          totalCount:
            magnitude === null ? 2000 : Math.min(magnitude.magnitude, 2000),
        },
        magnitude,
      },
    });
  };

  const announced = () =>
    document.querySelector('p[role="status"][aria-live="polite"]');

  const renderHosted = async (namn: string) =>
    render(
      <Announcer>
        {await ForetagSokResults({
          namn,
          sni: [],
          kommun: [],
          page: 1,
          reference: REFERENCE,
        })}
      </Announcer>,
    );

  beforeEach(() => {
    searchCompanies.mockReset();
    getCompanyWatchStatusByOrgNr.mockReset();
    getCompanyWatchStatusByOrgNr.mockResolvedValue([{ companyWatchId: null }]);
  });

  it("a SEARCH announces the count — W3C's '18 results returned'", async () => {
    respondWith({ magnitude: 1234, saturated: false });
    await renderHosted("acme");

    expect(announced()).toHaveTextContent("1 234 träffar");
  });

  it("ZERO matches announce the empty statement — W3C's 'No results returned'", async () => {
    // The case that previously reached a screen reader through nothing at all: the empty state has
    // never carried a live region of any kind, so a search that found nothing was silent.
    respondWith({ magnitude: 0, saturated: false }, []);
    await renderHosted("obefintligt");

    expect(announced()).toHaveTextContent("Inga företag matchar sökningen");
  });

  it("BROWSE-ALL announces completion without inventing a figure", async () => {
    respondWith(null);
    await renderHosted("");

    expect(announced()).toHaveTextContent("Företagen i registret visas.");
    // #1149's ruling is untouched: no number is claimed on this state, in the region or on the page.
    expect(announced()).not.toHaveTextContent(/\d/);
    expect(document.querySelector(".jp-results-count")).toBeNull();
  });

  it("the count line itself is no longer a second live region", async () => {
    respondWith({ magnitude: 1234, saturated: false });
    await renderHosted("acme");

    // Exactly one region in the RESULTS SUBTREE — not on the surface, which carries the searchbar's
    // filter region and its org.nr section besides. Restoring `role="status"` on the count line
    // would announce the same sentence twice: the regression this pins, and one no rendered-text
    // assertion sees.
    expect(document.querySelectorAll('[role="status"]')).toHaveLength(1);
    expect(document.querySelector(".jp-results-count")).not.toHaveAttribute(
      "role",
    );
  });

  /**
   * The failure branches END the load too, and the surface rule applies to them unchanged: the
   * skeleton has already said "Söker företag…" through the region, so a branch that sets nothing
   * leaves a screen reader waiting on a search that has finished failing.
   *
   * `code-reviewer` Major 2 on PR #1504 — four reachable branches, none of them covered until now.
   */
  it.each([
    ["rateLimited", "Vänta en stund och ladda om sidan."],
    ["notFound", "Försök igen om en stund."],
    ["forbidden", "Försök igen om en stund."],
    ["error", "Försök igen om en stund."],
  ])(
    "a %s response announces the remedy, not only the cause",
    async (kind, remedy) => {
      searchCompanies.mockResolvedValue({ kind });
      await renderHosted("acme");

      // All four branches share `loadErrorTitle`, so the title alone cannot tell a throttled user
      // from a broken one — and the natural guess on a rate limit, searching again, extends the
      // block. `design-reviewer` Major, scoped re-check on PR #1504. Read through the real
      // catalogue so a renamed key fails here rather than announcing a raw message id.
      expect(announced()).toHaveTextContent("Sökningen kunde inte läsas in");
      expect(announced()).toHaveTextContent(remedy);
      // #1505 `design-reviewer` Major 3 — the card carries NO `role="alert"`. It used to, and the
      // two channels then said the same sentence twice, one of them interrupting: an alert node
      // inserted into a live DOM with its text already in place is the case AT does announce.
      // `Announce` above is the single path. Without this assertion the deletion is unguarded —
      // measured: restoring the role killed no test in the suite.
      expect(document.querySelector('[role="alert"]')).toBeNull();
    },
  );

  it("REPLACES the skeleton's opening sentence rather than leaving it standing", async () => {
    // The defect as a transition, which is the only way to state it honestly. Asserting merely that
    // the region does not say "Söker företag…" after rendering the error in isolation is vacuous —
    // the region starts empty, so it passes with no announcement wired at all. This renders the
    // skeleton first, proves the opening sentence is actually there, then swaps in the failed
    // results the way Suspense does.
    searchCompanies.mockResolvedValue({ kind: "error" });

    const { rerender } = render(
      <Announcer>
        <ForetagSokResultsSkeleton />
      </Announcer>,
    );
    expect(announced()).toHaveTextContent("Söker företag…");

    rerender(
      <Announcer>
        {await ForetagSokResults({
          namn: "acme",
          sni: [],
          kommun: [],
          page: 1,
          reference: REFERENCE,
        })}
      </Announcer>,
    );

    expect(announced()).toHaveTextContent("Sökningen kunde inte läsas in");
    expect(announced()).not.toHaveTextContent("Söker företag…");
  });
});
