import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { ApplicationHistoryList } from "./application-history-list";
import type { EmployerApplicationHistory } from "@/lib/dto/application-history";

const legalEntity: EmployerApplicationHistory = {
  organizationNumber: "5592804784",
  isProtectedIdentity: false,
  companyName: "Skatteverket",
  applicationCount: 2,
  applications: [
    { appliedAt: "2026-06-14T08:00:00+00:00", statusName: "Submitted" },
    { appliedAt: "2026-05-02T08:00:00+00:00", statusName: "Rejected" },
  ],
};

// Sole proprietorship: the backend masks a personnummer-shaped org.nr to null and flags it
// (ADR 0087 D8(c)) — the raw value never reaches the FE.
const soleProp: EmployerApplicationHistory = {
  organizationNumber: null,
  isProtectedIdentity: true,
  companyName: "Anna Andersson Konsult",
  applicationCount: 1,
  applications: [{ appliedAt: "2026-06-10T08:00:00+00:00", statusName: "Submitted" }],
};

describe("ApplicationHistoryList (#311 #448, ADR 0087 D2/D8(c); ADR 0090 R-A4)", () => {
  it("tom lista → honest civic nollstate-copy, ingen lista renderas", () => {
    render(<ApplicationHistoryList items={[]} />);
    expect(
      screen.getByText("Du har ingen ansökningshistorik än")
    ).toBeInTheDocument();
    expect(screen.queryByRole("list")).toBeNull();
  });

  it("legal-entity → namn + formaterat org.nr + historik-räknare (skickade ansökningar)", () => {
    render(<ApplicationHistoryList items={[legalEntity]} />);
    expect(
      screen.getByRole("heading", { name: "Skatteverket" })
    ).toBeInTheDocument();
    expect(screen.getByText("Org.nr 559280-4784")).toBeInTheDocument();
    expect(screen.getByText("2 skickade ansökningar")).toBeInTheDocument();
  });

  it("historik-räknare plural: 1 → 'skickad ansökan', 3 → 'skickade ansökningar'", () => {
    const { rerender } = render(
      <ApplicationHistoryList items={[{ ...legalEntity, applicationCount: 1 }]} />
    );
    expect(screen.getByText("1 skickad ansökan")).toBeInTheDocument();
    rerender(
      <ApplicationHistoryList items={[{ ...legalEntity, applicationCount: 3 }]} />
    );
    expect(screen.getByText("3 skickade ansökningar")).toBeInTheDocument();
  });

  it("entries → ansökt-datum + svensk statusetikett (SPOT applications.enums), aldrig råa engelska tokens", () => {
    render(<ApplicationHistoryList items={[legalEntity]} />);
    // Locale-aware short date (next-intl): sv June → "juni".
    expect(screen.getByText("Ansökt 14 juni 2026")).toBeInTheDocument();
    expect(screen.getByText("Ansökt 2 maj 2026")).toBeInTheDocument();
    // Status resolves through applications.enums.status: Submitted → "Skickad", Rejected → "Nekad".
    expect(screen.getByText("Skickad")).toBeInTheDocument();
    expect(screen.getByText("Nekad")).toBeInTheDocument();
    // The raw English enum token never leaks into Swedish copy.
    expect(screen.queryByText("Submitted")).toBeNull();
    expect(screen.queryByText("Rejected")).toBeNull();
    // Native <details> summary (collapsed by default) carries the expand affordance.
    expect(screen.getByText("Visa ansökningar")).toBeInTheDocument();
  });

  it("okänd status-token (deploy-skew) → faller tillbaka till råsträngen, kastar aldrig", () => {
    render(
      <ApplicationHistoryList
        items={[
          {
            ...legalEntity,
            applications: [{ appliedAt: "2026-06-14T08:00:00+00:00", statusName: "Bogus" }],
          },
        ]}
      />
    );
    expect(screen.getByText("Bogus")).toBeInTheDocument();
  });

  it("skyddad identitet → flagga visas, INGET org.nr/personnummer i DOM (§5 / D8(c))", () => {
    const { container } = render(<ApplicationHistoryList items={[soleProp]} />);
    expect(screen.getByText("Skyddad identitet")).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: "Anna Andersson Konsult" })
    ).toBeInTheDocument();
    expect(screen.queryByText(/Org\.nr/)).toBeNull();
    // No 10-digit org.nr/personnummer sequence anywhere in the rendered tree.
    expect(container.textContent ?? "").not.toMatch(/\d{6}-?\d{4}/);
  });

  it("companyName null → civic fallback-namn (aldrig tom rubrik)", () => {
    render(
      <ApplicationHistoryList items={[{ ...legalEntity, companyName: null }]} />
    );
    expect(
      screen.getByText("Företagets namn är inte tillgängligt")
    ).toBeInTheDocument();
  });

  it("ordning bevaras (renderar i mottagen ordning, nyast-ansökt först från backend)", () => {
    render(<ApplicationHistoryList items={[legalEntity, soleProp]} />);
    const headings = screen.getAllByRole("heading", { level: 3 });
    expect(headings[0]).toHaveTextContent("Skatteverket");
    expect(headings[1]).toHaveTextContent("Anna Andersson Konsult");
  });

  it("R-A4-firewall: entries bär bara datum + status — ingen länk till den enskilda ansökan", () => {
    render(<ApplicationHistoryList items={[legalEntity]} />);
    // The history surface never links to an individual application (no application id / JobAdId in the
    // DTO). The only interactive element is the native <details> disclosure — no anchors.
    expect(screen.queryAllByRole("link")).toHaveLength(0);
  });
});
