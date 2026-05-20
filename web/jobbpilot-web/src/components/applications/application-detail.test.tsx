import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { ApplicationDetail } from "./application-detail";
import type { ApplicationDetailDto } from "@/lib/types/applications";

// Server-actions mockas (samma repo-mönster som status-edit-card.test) så
// StatusEditCard/AddNoteForm/AddFollowUpForm-öarna kan renderas i jsdom.
vi.mock("@/lib/actions/applications", () => ({
  transitionStatusAction: vi.fn().mockResolvedValue({ success: true }),
  addNoteAction: vi.fn().mockResolvedValue({ success: true }),
  addFollowUpAction: vi.fn().mockResolvedValue({ success: true }),
  recordFollowUpOutcomeAction: vi.fn().mockResolvedValue({ success: true }),
}));

function makeDetail(
  overrides: Partial<ApplicationDetailDto> = {}
): ApplicationDetailDto {
  return {
    id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    jobSeekerId: "seeker-1",
    jobAdId: "ad-1",
    status: "Submitted",
    createdAt: "2026-05-01T08:00:00Z",
    updatedAt: "2026-05-10T08:00:00Z",
    jobAd: {
      jobAdId: "ad-1",
      title: "Backend-utvecklare",
      company: "Volvo",
      url: "https://example.com/ad",
      source: "Platsbanken",
      publishedAt: "2026-05-01",
      expiresAt: "2026-06-01",
    },
    coverLetter: null,
    followUps: [],
    notes: [],
    ...overrides,
  };
}

describe("ApplicationDetail", () => {
  it("renderar status-block med STATUS_LABELS-etikett (REAL status)", () => {
    render(<ApplicationDetail application={makeDetail()} />);
    // "Status" finns både i status-blocket (label) och StatusEditCard (h2).
    expect(screen.getAllByText("Status").length).toBeGreaterThanOrEqual(2);
    // Submitted → "Skickad" (status-blocket + StatusEditCard-pill).
    expect(screen.getAllByText("Skickad").length).toBeGreaterThan(0);
  });

  it("renderar ApplicationDetail-rubrik när inte headless", () => {
    render(<ApplicationDetail application={makeDetail()} />);
    expect(
      screen.getByRole("heading", { name: "Backend-utvecklare" })
    ).toBeInTheDocument();
  });

  it("utelämnar egen rubrik i headless-läge (modal äger titeln)", () => {
    render(<ApplicationDetail application={makeDetail()} headless />);
    expect(
      screen.queryByRole("heading", { name: "Backend-utvecklare" })
    ).not.toBeInTheDocument();
    // Status-blocket renderas fortfarande.
    expect(screen.getAllByText("Status").length).toBeGreaterThanOrEqual(2);
  });

  it("komponerar tidslinjen av REALA events (skapad + status)", () => {
    render(<ApplicationDetail application={makeDetail()} />);
    expect(screen.getByText("Tidslinje")).toBeInTheDocument();
    expect(screen.getByText("Ansökan skapades")).toBeInTheDocument();
    expect(screen.getByText("Status: Skickad")).toBeInTheDocument();
  });

  it("renderar real notes[] och utelämnar coverLetter när null", () => {
    render(
      <ApplicationDetail
        application={makeDetail({
          notes: [
            {
              id: "n1",
              content: "Ringde rekryteraren",
              createdAt: "2026-05-05T08:00:00Z",
            },
          ],
        })}
      />
    );
    expect(screen.getByText("Ringde rekryteraren")).toBeInTheDocument();
    expect(screen.queryByText("Personligt brev")).not.toBeInTheDocument();
  });

  it("renderar Personligt brev när coverLetter finns", () => {
    render(
      <ApplicationDetail
        application={makeDetail({ coverLetter: "Hej, jag söker tjänsten." })}
      />
    );
    expect(screen.getByText("Personligt brev")).toBeInTheDocument();
    expect(
      screen.getByText("Hej, jag söker tjänsten.")
    ).toBeInTheDocument();
  });

  it("faller tillbaka till mono-id-rubrik när jobAd saknas (manuell)", () => {
    render(
      <ApplicationDetail
        application={makeDetail({ jobAd: null, jobAdId: null })}
      />
    );
    expect(screen.getByText("Ansökan #aaaaaaaa")).toBeInTheDocument();
  });
});
