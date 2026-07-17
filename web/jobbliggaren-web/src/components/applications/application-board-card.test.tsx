import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { ApplicationActionsProvider } from "./application-actions";
import { ApplicationBoardCard } from "./application-board-card";
import type {
  ApplicationDto,
  JobAdSummaryDto,
} from "@/lib/types/applications";

// StatusMenu inside the card consumes the provider's server actions — mock the
// module (samma idiom som application-row.test) så kortet kan renderas i jsdom.
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

function renderCard(application: ApplicationDto) {
  return render(
    <ApplicationActionsProvider>
      <ApplicationBoardCard
        application={application}
        now={FIXED_NOW}
        isDragging={false}
        onDragStart={vi.fn()}
        onDragEnd={vi.fn()}
      />
    </ApplicationActionsProvider>,
  );
}

// #892 (CTO R1): Tavla-kortet är en av de fyra bundna ytorna. Markören lever i
// kortets EGEN JSX (inte i den delade adIdentityOf-hjälparen), så en regression
// som tappar `{adRemoved && …}` här skulle skeppa osedd utan ett kort-eget test.
describe("ApplicationBoardCard removed-ad marker (#892)", () => {
  it("visar bevarad snapshot-identitet + borttagen-markör när annonsen är raderad (med snapshot)", () => {
    renderCard(
      makeApplication({
        jobAd: {
          ...jobAd,
          title: "Bevarad roll",
          company: "Bevarat AB",
          status: "Erased",
        },
      }),
    );
    // BE-fallbacken gav kortet den riktiga (frysta) identiteten…
    expect(
      screen.getByRole("heading", { name: "Bevarad roll" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Bevarat AB")).toBeInTheDocument();
    // …och kortet måste bära dödssignalen så den inte ser levande ut.
    const marker = screen.getByText("Annonsen är borttagen");
    expect(marker).toHaveClass("jp-tag");
  });

  it("visar mono-id-fallback + markör vid raderad annons UTAN snapshot (tom identitet, R5)", () => {
    renderCard(
      makeApplication({
        jobAd: { ...jobAd, title: "", company: "", url: null, status: "Erased" },
      }),
    );
    const fallback = screen.getByRole("heading", { name: "Ansökan #11111111" });
    expect(fallback).toHaveClass("jp-mono");
    expect(screen.getByText("Annonsen är borttagen")).toHaveClass("jp-tag");
    // R5: den domän-interna "[raderad]"-sentinelen når aldrig wiren → aldrig DOM.
    expect(screen.queryByText("[raderad]")).toBeNull();
  });

  it("visar INGEN markör för en arkiverad annons (Erased-exakt predikat, aldrig != Active)", () => {
    renderCard(makeApplication({ jobAd: { ...jobAd, status: "Archived" } }));
    expect(
      screen.getByRole("heading", { name: "Backend-utvecklare" }),
    ).toBeInTheDocument();
    expect(screen.queryByText("Annonsen är borttagen")).toBeNull();
  });

  it("visar INGEN markör för en levande annons", () => {
    renderCard(makeApplication());
    expect(screen.queryByText("Annonsen är borttagen")).toBeNull();
  });
});
