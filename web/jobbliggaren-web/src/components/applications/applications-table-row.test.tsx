import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { ApplicationActionsProvider } from "./application-actions";
import { ApplicationsTableRow } from "./applications-table-row";
import type {
  ApplicationDto,
  JobAdSummaryDto,
} from "@/lib/types/applications";

// StatusMenu inside the row consumes the provider's server actions — mock the
// module (samma idiom som application-row.test) så raden kan renderas i jsdom.
vi.mock("@/lib/actions/applications", () => ({
  transitionStatusAction: vi.fn(async () => ({ success: true as const })),
  logFollowUpAction: vi.fn(async () => ({ success: true as const })),
}));

const FIXED_NOW = new Date("2026-05-15T12:00:00Z");

const jobAd: JobAdSummaryDto = {
  jobAdId: "ad-1",
  title: "Backend-utvecklare",
  company: "Volvo",
  url: "https://example.com/ad",
  source: "Platsbanken",
  publishedAt: "2026-05-01",
  expiresAt: "2026-06-01",
  status: "Active",
};

function makeApplication(
  overrides: Partial<ApplicationDto> = {},
): ApplicationDto {
  return {
    id: "11111111-2222-3333-4444-555555555555",
    jobSeekerId: "seeker-1",
    jobAdId: "ad-1",
    status: "Submitted",
    createdAt: "2026-05-01",
    updatedAt: "2026-05-12",
    appliedAt: "2026-05-10",
    lastStatusChangeAt: "2026-05-10",
    jobAd,
    ...overrides,
  };
}

function renderRow(application: ApplicationDto) {
  // <tr> must live inside a table (valid DOM nesting / no React warning).
  return render(
    <ApplicationActionsProvider>
      <table>
        <tbody>
          <ApplicationsTableRow
            application={application}
            now={FIXED_NOW}
            selected={false}
            onToggleSelect={vi.fn()}
          />
        </tbody>
      </table>
    </ApplicationActionsProvider>,
  );
}

// #892 (CTO R1): Tabell-raden (volymvyn) är en av de fyra bundna ytorna. Markören
// lever i radens EGEN JSX, så den behöver en rad-egen killing test — den delade
// adIdentityOf-täckningen bevisar inte att DENNA yta faktiskt renderar signalen.
describe("ApplicationsTableRow removed-ad marker (#892)", () => {
  it("visar bevarad snapshot-identitet + borttagen-markör när annonsen är raderad (med snapshot)", () => {
    renderRow(
      makeApplication({
        jobAd: {
          ...jobAd,
          title: "Bevarad roll",
          company: "Bevarat AB",
          status: "Erased",
        },
      }),
    );
    expect(
      screen.getByRole("link", { name: "Bevarad roll" }),
    ).toBeInTheDocument();
    const marker = screen.getByText("Annonsen är borttagen");
    expect(marker).toHaveClass("jp-tag");
  });

  it("visar mono-id-fallback-titel + markör vid raderad annons UTAN snapshot (tom identitet, R5)", () => {
    renderRow(
      makeApplication({
        jobAd: { ...jobAd, title: "", company: "", url: null, status: "Erased" },
      }),
    );
    expect(
      screen.getByRole("link", { name: "Ansökan #11111111" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Annonsen är borttagen")).toHaveClass("jp-tag");
    // R5: "[raderad]"-sentinelen når aldrig wiren → aldrig DOM.
    expect(screen.queryByText("[raderad]")).toBeNull();
  });

  it("visar INGEN markör för en arkiverad annons (Erased-exakt predikat)", () => {
    renderRow(makeApplication({ jobAd: { ...jobAd, status: "Archived" } }));
    expect(
      screen.getByRole("link", { name: "Backend-utvecklare" }),
    ).toBeInTheDocument();
    expect(screen.queryByText("Annonsen är borttagen")).toBeNull();
  });

  it("visar INGEN markör för en levande annons", () => {
    renderRow(makeApplication());
    expect(screen.queryByText("Annonsen är borttagen")).toBeNull();
  });
});
