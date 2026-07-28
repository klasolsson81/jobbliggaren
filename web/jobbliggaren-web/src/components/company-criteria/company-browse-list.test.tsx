import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { CompanyBrowseList } from "./company-browse-list";
import type { CompanyBrowse, CriterionReference } from "@/lib/dto/company-criteria";

// CompanyFollowButton pulls in the server-action module; mock it so the client island renders in jsdom.
vi.mock("@/lib/actions/company-follows", () => ({
  followCompanyAction: vi.fn(),
  unfollowCompanyAction: vi.fn(),
}));

const REFERENCE: CriterionReference = {
  sniVersion: "2025",
  kommunVersion: "2025",
  sni: [],
  lan: [],
};

const LEGAL_ORGNR = "5592804784";

const LEGAL: CompanyBrowse = {
  organizationNumber: LEGAL_ORGNR,
  isProtectedIdentity: false,
  name: "Acme Bygg AB",
  seatMunicipalityCode: "0180",
  seatMunicipalityName: "Stockholm",
  sniCodes: [],
};

const PROTECTED: CompanyBrowse = {
  organizationNumber: null,
  isProtectedIdentity: true,
  name: "Skyddad Firma",
  seatMunicipalityCode: "0180",
  seatMunicipalityName: "Stockholm",
  sniCodes: [],
};

describe("CompanyBrowseList — #560 PR-C follow-column gate", () => {
  it("renders no follow column when followStateByOrgNr is omitted (bevakningar/[id] parity)", () => {
    render(<CompanyBrowseList items={[LEGAL]} reference={REFERENCE} />);

    expect(
      screen.queryByRole("columnheader", { name: "Bevaka" })
    ).not.toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("renders a follow button per non-masked row when followStateByOrgNr is provided", () => {
    const map = new Map<string, string | null>([[LEGAL_ORGNR, null]]);
    render(
      <CompanyBrowseList items={[LEGAL]} reference={REFERENCE} followStateByOrgNr={map} />
    );

    expect(
      screen.getByRole("columnheader", { name: "Bevaka" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Bevaka Acme Bygg AB" })
    ).toBeInTheDocument();
  });

  it("reflects an existing follow as 'Bevakar'", () => {
    const map = new Map<string, string | null>([[LEGAL_ORGNR, "cw-1"]]);
    render(
      <CompanyBrowseList items={[LEGAL]} reference={REFERENCE} followStateByOrgNr={map} />
    );

    expect(
      screen.getByRole("button", { name: "Bevakar Acme Bygg AB" })
    ).toBeInTheDocument();
  });

  it("never renders a follow button for a masked/sole-prop row (no org.nr key → not followable)", () => {
    render(
      <CompanyBrowseList
        items={[PROTECTED]}
        reference={REFERENCE}
        followStateByOrgNr={new Map()}
      />
    );

    // The column exists, but the protected row carries no follow affordance (ADR 0087 D8(c)) — no button,
    // and a screen-reader-only "Kan inte bevakas" in place of it.
    expect(
      screen.getByRole("columnheader", { name: "Bevaka" })
    ).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    expect(screen.getByText("Kan inte bevakas")).toBeInTheDocument();
  });
});

/**
 * The accessible name is CALLER-SPECIFIC, and that is the whole point of the prop.
 *
 * The defaults ("Företag som matchar din bevakning") belong to the criterion browse. Rendered on
 * `/foretag/sok`, which answers a SEARCH, they are simply false — and they were false there for as
 * long as both surfaces shared this component, because nothing sighted showed it.
 */
describe("CompanyBrowseList — the table's accessible name", () => {
  it("defaults to the criterion-browse wording when no labels are given", () => {
    render(<CompanyBrowseList items={[LEGAL]} reference={REFERENCE} />);
    expect(
      screen.getByRole("table", { name: "Företag som matchar bevakningen" }),
    ).toBeInTheDocument();
  });

  it("uses the caller's wording when given, for BOTH the name and the caption", () => {
    render(
      <CompanyBrowseList
        items={[LEGAL]}
        reference={REFERENCE}
        labels={{ tableAria: "Företag som matchar sökningen", tableCaption: "Sökträffar." }}
      />,
    );
    const table = screen.getByRole("table", { name: "Företag som matchar sökningen" });
    expect(table).toBeInTheDocument();
    // The caption is sr-only, so it is asserted through the accessible description rather than sight.
    expect(table.querySelector("caption")).toHaveTextContent("Sökträffar.");
    // ...and the default must be GONE, not merely joined by the override.
    expect(
      screen.queryByRole("table", { name: "Företag som matchar bevakningen" }),
    ).not.toBeInTheDocument();
  });
});

/**
 * Under `table-layout: fixed` the `<colgroup>` IS the column geometry, so it has to stay in lockstep
 * with the header row: one `<col>` too few and every column after the gap silently takes the wrong
 * width — on /foretag/sok that means the org.nr answer and the browse below it stop lining up, which
 * is the defect this geometry exists to fix.
 *
 * jsdom computes no layout, so the widths themselves are verified rendered (Chromium, see the PR).
 * What jsdom CAN pin is the structural contract, and that is the half a future column addition
 * breaks: the follow column is conditional, so the two must agree in BOTH shapes.
 */
/** The `<col>` classes in document order — the geometry contract, not merely its arity. */
function colClasses(container: HTMLElement): string[] {
  return [...container.querySelectorAll("colgroup > col")].map((c) => c.className);
}

describe("CompanyBrowseList — the declared column geometry", () => {
  it("declares the five columns in order, matching the header row", () => {
    const map = new Map<string, string | null>([[LEGAL_ORGNR, null]]);
    const { container } = render(
      <CompanyBrowseList items={[LEGAL]} reference={REFERENCE} followStateByOrgNr={map} />,
    );

    // ORDER, not just count. Swapping two <col>s keeps every count identical and silently swaps two
    // column widths, so counting alone would call that geometry correct.
    expect(colClasses(container)).toEqual([
      "jp-companyBrowse__col--name",
      "jp-companyBrowse__col--orgnr",
      "jp-companyBrowse__col--seat",
      "jp-companyBrowse__col--sni",
      "jp-companyBrowse__col--follow",
    ]);
    // Scoped to the first header ROW: a second <tr> in <thead> would double a bare `thead th` count
    // while the geometry is untouched.
    expect(container.querySelectorAll("thead tr:first-child > th")).toHaveLength(5);
    expect(container.querySelectorAll("tbody tr:first-child > td")).toHaveLength(5);
  });

  it("drops the follow column and its <col> together (bevakningar/[id] parity)", () => {
    const { container } = render(<CompanyBrowseList items={[LEGAL]} reference={REFERENCE} />);

    expect(colClasses(container)).toEqual([
      "jp-companyBrowse__col--name",
      "jp-companyBrowse__col--orgnr",
      "jp-companyBrowse__col--seat",
      "jp-companyBrowse__col--sni",
    ]);
    expect(container.querySelectorAll("thead tr:first-child > th")).toHaveLength(4);
    expect(container.querySelectorAll("tbody tr:first-child > td")).toHaveLength(4);
  });

  /**
   * The colgroup is inert without the class that turns fixed layout on, and the CSS guard cannot
   * catch its removal: once production stops naming `jp-companyBrowse--withFollow`, the only
   * remaining reference is this test file, and the guard downgrades a test-only reference to
   * advisory rather than failing (`guard-css.mjs` — it cannot tell a positive assertion from a
   * negative one). So the classes are pinned here, in BOTH polarities, or they are pinned nowhere.
   */
  it("carries the fixed-layout class in both shapes, and the wider floor only with the follow column", () => {
    const map = new Map<string, string | null>([[LEGAL_ORGNR, null]]);
    const withFollow = render(
      <CompanyBrowseList items={[LEGAL]} reference={REFERENCE} followStateByOrgNr={map} />,
    ).container.querySelector("table");
    expect(withFollow).toHaveClass("jp-companyBrowse");
    expect(withFollow).toHaveClass("jp-companyBrowse--withFollow");

    const withoutFollow = render(
      <CompanyBrowseList items={[LEGAL]} reference={REFERENCE} />,
    ).container.querySelector("table");
    expect(withoutFollow).toHaveClass("jp-companyBrowse");
    expect(withoutFollow).not.toHaveClass("jp-companyBrowse--withFollow");
  });

  /**
   * Fixed layout removes the escape valve auto layout provided: a column can no longer grow to fit
   * its content, so whether a cell WRAPS is now load-bearing. All three of these were `whitespace`
   * decisions the geometry forced, and two of them are reverts waiting to happen — `whitespace-nowrap`
   * stood on the seat and follow cells before this change, so re-adding it reads as a cleanup.
   */
  it("wraps every cell that fixed layout can no longer widen", () => {
    const map = new Map<string, string | null>([[LEGAL_ORGNR, null]]);
    const { container } = render(
      <CompanyBrowseList items={[LEGAL]} reference={REFERENCE} followStateByOrgNr={map} />,
    );
    const all = [...container.querySelectorAll("tbody tr:first-child > td")];
    // Fail closed on a missing cell rather than letting `undefined` satisfy a negated assertion.
    const cell = (i: number): HTMLElement => {
      const td = all[i];
      if (!td) throw new Error(`no <td> at index ${i} — the row has ${all.length}`);
      return td as HTMLElement;
    };

    // A 42-character company name overflows into Org.nr at the table's minimum width without this.
    expect(cell(0)).toHaveClass("wrap-break-word");
    // The org.nr NUMBER may not break; the cell may, so the "Skyddad identitet" badge can wrap
    // instead of overflowing into Säteskommun.
    expect(cell(1)).not.toHaveClass("whitespace-nowrap");
    expect(cell(1).querySelector(".whitespace-nowrap")).toHaveTextContent("559280-4784");
    // "Ej svensk hemortskommun" (23 837 rows) wraps rather than painting across Branscher.
    expect(cell(2)).not.toHaveClass("whitespace-nowrap");
    expect(cell(2)).toHaveClass("wrap-break-word");
    // The follow cell also holds the failed-follow error, which must wrap — see CompanyFollowButton.
    expect(cell(4)).not.toHaveClass("whitespace-nowrap");
  });
});
