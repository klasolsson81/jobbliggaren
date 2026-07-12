import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, within } from "@testing-library/react";
import { FollowUpsSection } from "./follow-ups-section";
import type { FollowUpDto } from "@/lib/types/applications";

vi.mock("@/lib/actions/applications", () => ({
  addFollowUpAction: vi.fn().mockResolvedValue({ success: true }),
  recordFollowUpOutcomeAction: vi.fn().mockResolvedValue({ success: true }),
}));

const pendingFollowUp = (
  overrides: Partial<FollowUpDto> = {},
): FollowUpDto => ({
  id: "fu1",
  channel: "Email",
  scheduledAt: "2026-05-15T10:00:00Z",
  outcome: "Pending",
  outcomeAt: null,
  note: "Skickade påminnelse om CV-uppdatering",
  createdAt: "2026-05-14T08:00:00Z",
  ...overrides,
});

const respondedFollowUp = (
  overrides: Partial<FollowUpDto> = {},
): FollowUpDto => ({
  id: "fu2",
  channel: "Phone",
  scheduledAt: "2026-05-10T10:00:00Z",
  outcome: "Responded",
  outcomeAt: "2026-05-12T14:00:00Z",
  note: "Pratade med rekryteraren",
  createdAt: "2026-05-09T08:00:00Z",
  ...overrides,
});

describe("FollowUpsSection — disclosure-mönster (Prompt 4)", () => {
  it("renderar kompakt rad per uppföljning utan outcome-form synlig default", () => {
    render(
      <FollowUpsSection
        applicationId="app-1"
        followUps={[pendingFollowUp()]}
      />,
    );
    expect(screen.getByText("E-post")).toBeInTheDocument();
    expect(screen.queryByLabelText("Utfall")).not.toBeInTheDocument();
  });

  it("Pending: klick på rad expanderar och visar RecordFollowUpOutcomeForm", () => {
    render(
      <FollowUpsSection
        applicationId="app-1"
        followUps={[pendingFollowUp()]}
      />,
    );
    fireEvent.click(
      within(screen.getByRole("list")).getByRole("button", { expanded: false }),
    );
    expect(screen.getByLabelText("Utfall")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /Spara utfall/ }),
    ).toBeInTheDocument();
  });

  it("Låst utfall (Responded): expanderad → plain text, ingen dropdown", () => {
    render(
      <FollowUpsSection
        applicationId="app-1"
        followUps={[respondedFollowUp()]}
      />,
    );
    fireEvent.click(
      within(screen.getByRole("list")).getByRole("button", { expanded: false }),
    );
    expect(screen.queryByLabelText("Utfall")).not.toBeInTheDocument();
    // Outcome-label syns som text i body.
    expect(screen.getAllByText("Svar mottaget").length).toBeGreaterThan(0);
  });

  it("endast EN uppföljning expanderad åt gången", () => {
    render(
      <FollowUpsSection
        applicationId="app-1"
        followUps={[pendingFollowUp(), respondedFollowUp()]}
      />,
    );
    // #805 punkt 5: "?"-triggern i sektionsetiketten bär också aria-expanded →
    // scopa till listan så bara uppföljningsraderna räknas.
    const rows = within(screen.getByRole("list")).getAllByRole("button", {
      expanded: false,
    });
    fireEvent.click(rows[0]!);
    fireEvent.click(rows[1]!);
    const expanded = screen.getAllByRole("button", { expanded: true });
    expect(expanded).toHaveLength(1);
  });

  it("Esc kollapsar aktiv expanderad rad", () => {
    render(
      <FollowUpsSection
        applicationId="app-1"
        followUps={[pendingFollowUp()]}
      />,
    );
    fireEvent.click(
      within(screen.getByRole("list")).getByRole("button", { expanded: false }),
    );
    expect(screen.getByRole("button", { expanded: true })).toBeInTheDocument();
    fireEvent.keyDown(window, { key: "Escape" });
    expect(
      within(screen.getByRole("list")).getByRole("button", { expanded: false }),
    ).toBeInTheDocument();
  });

  it("default visar '+ Lägg till uppföljning'-knapp, ej form", () => {
    render(<FollowUpsSection applicationId="app-1" followUps={[]} />);
    expect(
      screen.getByRole("button", { name: /Lägg till uppföljning/ }),
    ).toBeInTheDocument();
    expect(screen.queryByLabelText("Kanal")).not.toBeInTheDocument();
  });

  it("klick på Lägg till-knapp expanderar form, Avbryt kollapsar", () => {
    render(<FollowUpsSection applicationId="app-1" followUps={[]} />);
    fireEvent.click(
      screen.getByRole("button", { name: /Lägg till uppföljning/ }),
    );
    expect(screen.getByLabelText("Kanal")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Avbryt" }));
    expect(screen.queryByLabelText("Kanal")).not.toBeInTheDocument();
  });

  it("renderar empty-state när inga uppföljningar", () => {
    render(<FollowUpsSection applicationId="app-1" followUps={[]} />);
    expect(
      screen.getByText("Inga uppföljningar registrerade."),
    ).toBeInTheDocument();
  });

  it("renderar första raden av anteckning i kompakt vy", () => {
    render(
      <FollowUpsSection
        applicationId="app-1"
        followUps={[
          pendingFollowUp({
            note: "Första raden\nAndra raden får inte synas",
          }),
        ]}
      />,
    );
    expect(screen.getByText("Första raden")).toBeInTheDocument();
    expect(
      screen.queryByText("Andra raden får inte synas"),
    ).not.toBeInTheDocument();
  });
});
