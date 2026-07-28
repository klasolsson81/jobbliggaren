import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
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
    // The header row IN ORDER too, and scoped to the first <tr>. Pinning the colgroup's order while
    // counting the headers is the same arity gap one row lower: swapping two <th>s labels a 145px
    // column "Branscher" and a 280px column "Säteskommun" with every count in this file still 5.
    expect(
      [...container.querySelectorAll("thead tr:first-child > th")].map((th) => th.textContent),
    ).toEqual(["Företag", "Org.nr", "Säteskommun", "Branscher", "Bevaka"]);
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
    expect(
      [...container.querySelectorAll("thead tr:first-child > th")].map((th) => th.textContent),
    ).toEqual(["Företag", "Org.nr", "Säteskommun", "Branscher"]);
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
    // `jp-table` too: this PR rewrote the className expression, and dropping the ledger styling
    // leaves guard:css green (the element still resolves via jp-companyBrowse) and every other
    // assertion here green.
    expect(withFollow).toHaveClass("jp-table", "jp-companyBrowse", "jp-companyBrowse--withFollow");

    const withoutFollow = render(
      <CompanyBrowseList items={[LEGAL]} reference={REFERENCE} />,
    ).container.querySelector("table");
    expect(withoutFollow).toHaveClass("jp-table", "jp-companyBrowse");
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
    // Branscher wraps on its own commas today; pinned so "every cell" is a measurement, not a title.
    expect(cell(3)).not.toHaveClass("whitespace-nowrap");
    // Index is a POSITION, not an identity: swapping the last two <td>s alone leaves this negation
    // passing on the Branscher cell. Anchor it by content before negating anything about it.
    expect(cell(4)).toContainElement(screen.getByRole("button", { name: "Bevaka Acme Bygg AB" }));
    // The follow cell also holds the failed-follow error, which must wrap — see CompanyFollowButton.
    expect(cell(4)).not.toHaveClass("whitespace-nowrap");
  });

  /**
   * The badge is the REASON nowrap left the org.nr cell, so it is the half that has to be pinned.
   * "Skyddad identitet" sets `font-sans` inside a `font-mono` cell and clears 175px by 14px — a font
   * fallback eats that, and under fixed layout the cell then overflows into Säteskommun instead of
   * growing. Asserting only that the NUMBER carries nowrap leaves the badge free to take the class
   * back, which every other assertion in this file survives (measured, not argued).
   */
  it("leaves the protected-identity badge wrappable — the reason nowrap left the cell", () => {
    const { container } = render(
      <CompanyBrowseList items={[PROTECTED]} reference={REFERENCE} followStateByOrgNr={new Map()} />,
    );
    const orgNrCell = container.querySelectorAll("tbody tr:first-child > td")[1];
    if (!orgNrCell) throw new Error("no org.nr cell — the row rendered fewer than two <td>s");

    // Anchor the cell by its content BEFORE negating anything about that content.
    expect(orgNrCell).toContainElement(screen.getByText("Skyddad identitet"));
    expect(orgNrCell).not.toHaveClass("whitespace-nowrap");
    // The load-bearing one: nothing BETWEEN the cell and the badge may forbid the break either.
    expect(orgNrCell.querySelectorAll(".whitespace-nowrap")).toHaveLength(0);
  });
});

/**
 * `table-layout: fixed` is the whole mechanism, and no gate in this repo covers its removal. jsdom
 * applies no CSS, so a layout assertion is impossible; `guard:css` reports an element only when NO
 * `jp-*` name on it resolves, and this table also carries `jp-table`, which always will. Deleting
 * the rule therefore leaves vitest, guard:css, tsc and eslint all green while the two tables on
 * /foretag/sok go back to disagreeing about every column start. So it is pinned as TEXT, which is
 * the only thing that can see it. Precedent for reading a repo file from a test:
 * `src/i18n/client-namespace-payload.test.ts`.
 */
describe("CompanyBrowseList — the stylesheet still declares the geometry", () => {
  // Same resolution form as `src/i18n/client-namespace-payload.test.ts`: a `new URL(rel, base)`
  // against `import.meta.url` is not a file: URL under this vitest config and throws.
  const css = readFileSync(
    resolve(dirname(fileURLToPath(import.meta.url)), "../../app/globals.css"),
    "utf8",
  );

  it("declares fixed layout for the class the table carries", () => {
    expect(css).toMatch(/\.jp-companyBrowse\s*\{[^}]*table-layout:\s*fixed/);
  });

  it("keeps both minimum widths written as their own arithmetic", () => {
    // The sums are the invariant (declared columns + the 220px name floor), so they are declared as
    // `calc()` rather than as hand-totalled literals — four of the five widths already moved once
    // during review, and a literal would have drifted silently.
    expect(css).toMatch(
      /\.jp-companyBrowse\s*\{[^}]*min-width:\s*calc\(175px \+ 145px \+ 280px \+ 220px\)/,
    );
    expect(css).toMatch(
      /\.jp-companyBrowse--withFollow\s*\{[^}]*min-width:\s*calc\(175px \+ 145px \+ 280px \+ 160px \+ 220px\)/,
    );
  });
});
