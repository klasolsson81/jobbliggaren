import { describe, it, expect, vi, beforeEach } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ActionResult } from "@/lib/actions/_action-result";

const { setResumeLanguageActionMock, refreshMock } = vi.hoisted(() => ({
  setResumeLanguageActionMock: vi.fn(),
  refreshMock: vi.fn(),
}));

vi.mock("@/lib/actions/resumes", () => ({ setResumeLanguageAction: setResumeLanguageActionMock }));
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: refreshMock, push: vi.fn(), back: vi.fn() }),
}));

import { CvLanguageChoice } from "./cv-language-choice";

const RESUME_ID = "22222222-2222-4222-8222-222222222222";

function deferred() {
  let resolve!: (value: ActionResult) => void;
  const promise = new Promise<ActionResult>((r) => {
    resolve = r;
  });
  return { promise, resolve };
}

beforeEach(() => {
  setResumeLanguageActionMock.mockReset();
  setResumeLanguageActionMock.mockResolvedValue({ success: true });
  refreshMock.mockReset();
});

describe("CvLanguageChoice", () => {
  it("names the group with its visible label and checks the saved language", () => {
    render(<CvLanguageChoice resumeId={RESUME_ID} initialLanguage="En" />);

    expect(screen.getByText("CV:t är skrivet på")).toBeVisible();
    expect(screen.getByRole("radiogroup", { name: "CV:t är skrivet på" })).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Engelska" })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("radio", { name: "Svenska" })).toHaveAttribute("aria-checked", "false");
  });

  it("saves a new language, shows it while saving, then a receipt, and refreshes the review", async () => {
    const save = deferred();
    setResumeLanguageActionMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    render(<CvLanguageChoice resumeId={RESUME_ID} initialLanguage="Sv" />);

    await user.click(screen.getByRole("radio", { name: "Engelska" }));

    expect(setResumeLanguageActionMock).toHaveBeenCalledWith(RESUME_ID, "En");
    expect(screen.getByRole("radio", { name: "Engelska" })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("status")).toHaveTextContent("Sparar…");
    // Pending keeps the options focusable: announced as unavailable, never `disabled`.
    for (const option of screen.getAllByRole("radio")) {
      expect(option).toHaveAttribute("aria-disabled", "true");
      expect(option).not.toBeDisabled();
    }
    expect(refreshMock).not.toHaveBeenCalled();

    await act(async () => save.resolve({ success: true }));

    await waitFor(() => expect(refreshMock).toHaveBeenCalledTimes(1));
    expect(screen.getByRole("status")).toHaveTextContent(/^Sparat \d{2}:\d{2}$/);
    expect(screen.getByRole("radio", { name: "Engelska" })).not.toHaveAttribute("aria-disabled");
  });

  it("ignores a second change while the first is saving", async () => {
    const save = deferred();
    setResumeLanguageActionMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    render(<CvLanguageChoice resumeId={RESUME_ID} initialLanguage="Sv" />);

    await user.click(screen.getByRole("radio", { name: "Engelska" }));
    await user.click(screen.getByRole("radio", { name: "Svenska" }));
    await user.keyboard("{ArrowLeft}");

    expect(setResumeLanguageActionMock).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("radio", { name: "Engelska" })).toHaveAttribute("aria-checked", "true");

    await act(async () => save.resolve({ success: true }));
  });

  it("sends nothing when the saved language is chosen again", async () => {
    const user = userEvent.setup();
    render(<CvLanguageChoice resumeId={RESUME_ID} initialLanguage="Sv" />);

    await user.click(screen.getByRole("radio", { name: "Svenska" }));

    expect(setResumeLanguageActionMock).not.toHaveBeenCalled();
    expect(screen.getByRole("status")).toHaveTextContent("");
  });

  it("restores the previous language on a refusal, tied to the group, with focus on the checked option", async () => {
    setResumeLanguageActionMock.mockResolvedValue({
      success: false,
      error: "Det gick inte att byta språk. Försök igen om en stund.",
    });
    const user = userEvent.setup();
    render(<CvLanguageChoice resumeId={RESUME_ID} initialLanguage="Sv" />);
    const group = screen.getByRole("radiogroup", { name: "CV:t är skrivet på" });

    await user.click(screen.getByRole("radio", { name: "Engelska" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Det gick inte att byta språk. Försök igen om en stund.");
    expect(group).toHaveAttribute("aria-describedby", alert.id);
    expect(screen.getByRole("radio", { name: "Svenska" })).toHaveAttribute("aria-checked", "true");
    await waitFor(() => expect(screen.getByRole("radio", { name: "Svenska" })).toHaveFocus());
    // Mutually exclusive live regions: no receipt beside the alert.
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(refreshMock).not.toHaveBeenCalled();
  });
});
