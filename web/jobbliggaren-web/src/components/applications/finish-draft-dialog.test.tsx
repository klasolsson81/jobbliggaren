import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { FinishDraftDialog } from "./finish-draft-dialog";
import type { ApplicationDto, JobAdSummaryDto } from "@/lib/dto/applications";

const transitionStatusAction = vi.hoisted(() =>
  vi.fn<
    (id: string, target: string) => Promise<
      { success: true } | { success: false; error: string }
    >
  >(async () => ({ success: true })),
);
vi.mock("@/lib/actions/applications", () => ({ transitionStatusAction }));

const showApplicationToast = vi.hoisted(() => vi.fn());
vi.mock("@/lib/applications/toast-store", () => ({ showApplicationToast }));

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

function makeDraft(overrides: Partial<ApplicationDto> = {}): ApplicationDto {
  return {
    id: "11111111-2222-3333-4444-555555555555",
    jobSeekerId: "seeker-1",
    jobAdId: "ad-1",
    status: "Draft",
    createdAt: "2026-05-01",
    updatedAt: "2026-05-10",
    jobAd,
    ...overrides,
  };
}

function renderDialog(application: ApplicationDto, onOpenChange = vi.fn()) {
  render(
    <FinishDraftDialog
      open
      onOpenChange={onOpenChange}
      application={application}
      top={null}
    />,
  );
  return { onOpenChange };
}

beforeEach(() => {
  transitionStatusAction.mockClear();
  transitionStatusAction.mockResolvedValue({ success: true });
  showApplicationToast.mockClear();
});

describe("FinishDraftDialog (#1827 B3)", () => {
  // The app sends nothing to the employer: the title and the button say what happens, so
  // the dialog carries no further sentence (DESIGN.md §8 rule 2).
  it("says it marks the draft as sent, and nothing its title and button already say", () => {
    renderDialog(makeDraft());

    const dialog = screen.getByRole("dialog", { name: "Markera som Skickad?" });
    expect(dialog).not.toHaveAttribute("aria-describedby");
    expect(
      within(dialog).getByRole("button", { name: "Markera som Skickad" }),
    ).toBeInTheDocument();
    expect(within(dialog).getByText("Backend-utvecklare")).toBeInTheDocument();
    expect(within(dialog).getByText("Volvo")).toBeInTheDocument();
    expect(dialog).toHaveTextContent("Sista ansökningsdag 1 juni 2026");
  });

  // #892: an erased ad without a snapshot rides the wire with an empty identity.
  it("reads the identity through adIdentityOf: an erased ad's empty identity leaves no empty line", () => {
    renderDialog(
      makeDraft({
        jobAd: { ...jobAd, title: "", company: "", url: null, status: "Erased" },
      }),
    );

    const dialog = screen.getByRole("dialog", { name: "Markera som Skickad?" });
    const summary = within(dialog).getByText("Ansökan #11111111").parentElement!;
    // The fallback title and the deadline; no line for the missing company.
    expect(summary.querySelectorAll("p")).toHaveLength(2);
    for (const line of summary.querySelectorAll("p")) {
      expect(line.textContent?.trim()).not.toBe("");
    }
  });

  it("marks the draft as sent, shows the undo toast and closes", async () => {
    const user = userEvent.setup();
    const { onOpenChange } = renderDialog(makeDraft());

    await user.click(screen.getByRole("button", { name: "Markera som Skickad" }));

    await waitFor(() =>
      expect(transitionStatusAction).toHaveBeenCalledWith(
        "11111111-2222-3333-4444-555555555555",
        "Submitted",
      ),
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(showApplicationToast).toHaveBeenCalledWith({
      kind: "statusChange",
      applicationId: "11111111-2222-3333-4444-555555555555",
      company: "Volvo",
      from: "Draft",
      to: "Submitted",
    });
  });

  it("names an erased ad's application by its short id in the toast, never an empty company", async () => {
    const user = userEvent.setup();
    renderDialog(
      makeDraft({
        jobAd: { ...jobAd, title: "", company: "", url: null, status: "Erased" },
      }),
    );

    await user.click(screen.getByRole("button", { name: "Markera som Skickad" }));

    await waitFor(() =>
      expect(showApplicationToast).toHaveBeenCalledWith(
        expect.objectContaining({ company: "#11111111" }),
      ),
    );
  });

  it("keeps the dialog open with the error when the move fails", async () => {
    const user = userEvent.setup();
    transitionStatusAction.mockResolvedValueOnce({
      success: false,
      error: "Statusbytet misslyckades. Försök igen.",
    });
    const { onOpenChange } = renderDialog(makeDraft());

    await user.click(screen.getByRole("button", { name: "Markera som Skickad" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Statusbytet misslyckades. Försök igen.",
    );
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
    expect(showApplicationToast).not.toHaveBeenCalled();
  });

  it("Avbryt closes without moving", async () => {
    const user = userEvent.setup();
    const { onOpenChange } = renderDialog(makeDraft());

    await user.click(screen.getByRole("button", { name: "Avbryt" }));

    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(transitionStatusAction).not.toHaveBeenCalled();
  });
});
