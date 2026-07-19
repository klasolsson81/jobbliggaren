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
      screen.getByRole("button", { name: "Sluta bevaka Acme Bygg AB" })
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
