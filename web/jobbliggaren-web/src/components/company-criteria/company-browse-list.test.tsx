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
describe("CompanyBrowseList — the colgroup matches the header row", () => {
  it("declares one <col> per column when the follow column is rendered", () => {
    const map = new Map<string, string | null>([[LEGAL_ORGNR, null]]);
    const { container } = render(
      <CompanyBrowseList items={[LEGAL]} reference={REFERENCE} followStateByOrgNr={map} />,
    );

    expect(container.querySelectorAll("colgroup > col")).toHaveLength(5);
    expect(container.querySelectorAll("thead th")).toHaveLength(5);
    // Every body row must line up with them too — a `colSpan` or a dropped cell breaks the same way.
    expect(container.querySelectorAll("tbody tr:first-child > td")).toHaveLength(5);
  });

  it("drops the follow <col> with the follow column (bevakningar/[id] parity)", () => {
    const { container } = render(<CompanyBrowseList items={[LEGAL]} reference={REFERENCE} />);

    expect(container.querySelectorAll("colgroup > col")).toHaveLength(4);
    expect(container.querySelectorAll("thead th")).toHaveLength(4);
    expect(container.querySelectorAll("tbody tr:first-child > td")).toHaveLength(4);
    // The wider floor belongs to the five-column shape only; this surface must not inherit it.
    expect(container.querySelector("table")).not.toHaveClass("jp-companyBrowse--withFollow");
  });
});
