import { describe, it, expect, vi, beforeEach } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ActionResult } from "@/lib/actions/_action-result";

const { updateMyProfileActionMock, setLocaleActionMock, refreshMock } = vi.hoisted(() => ({
  updateMyProfileActionMock: vi.fn(),
  setLocaleActionMock: vi.fn(),
  refreshMock: vi.fn(),
}));

vi.mock("@/lib/actions/me", () => ({ updateMyProfileAction: updateMyProfileActionMock }));
vi.mock("@/i18n/set-locale-action", () => ({ setLocaleAction: setLocaleActionMock }));
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: refreshMock, push: vi.fn(), back: vi.fn() }),
}));

import { LanguageSetting } from "./language-setting";

function renderSetting(initialLanguage = "sv") {
  return render(
    <div data-testid="group">
      <LanguageSetting initialLanguage={initialLanguage} />
    </div>,
  );
}

beforeEach(() => {
  updateMyProfileActionMock.mockReset();
  updateMyProfileActionMock.mockResolvedValue({ success: true });
  setLocaleActionMock.mockReset();
  setLocaleActionMock.mockResolvedValue(undefined);
  refreshMock.mockReset();
});

describe("LanguageSetting", () => {
  it("offers Svenska and English, and no theme (MVP: one colour mode)", () => {
    renderSetting();
    expect(screen.queryByRole("radiogroup", { name: "Tema" })).not.toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Svenska" })).toHaveAttribute("aria-checked", "true");
    // English is live (next-intl, ADR 0078), not disabled.
    expect(screen.getByRole("radio", { name: "English" })).toBeEnabled();
  });

  it("sends ONLY the language, and flips the UI locale after the save succeeds", async () => {
    const user = userEvent.setup();
    renderSetting();

    await user.click(screen.getByRole("radio", { name: "English" }));

    await waitFor(() => expect(refreshMock).toHaveBeenCalledTimes(1));
    expect(updateMyProfileActionMock).toHaveBeenCalledTimes(1);
    expect(updateMyProfileActionMock.mock.calls[0]![0]).toEqual({ language: "en" });
    expect(setLocaleActionMock).toHaveBeenCalledWith("en");
  });

  it("leaves the locale alone and reverts when the save is refused", async () => {
    updateMyProfileActionMock.mockResolvedValue({ success: false, error: "Kunde inte na servern." });
    const user = userEvent.setup();
    renderSetting();

    await user.click(screen.getByRole("radio", { name: "English" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Kunde inte na servern.");
    expect(screen.getByRole("radio", { name: "Svenska" })).toHaveAttribute("aria-checked", "true");
    expect(setLocaleActionMock).not.toHaveBeenCalled();
    // Mutually exclusive live regions: no receipt beside the alert.
    expect(screen.queryByText(/^Sparat/)).not.toBeInTheDocument();
  });

  it("associates the refusal with the language group", async () => {
    updateMyProfileActionMock.mockResolvedValue({ success: false, error: "Kunde inte na servern." });
    const user = userEvent.setup();
    renderSetting();
    const group = screen.getByRole("radiogroup", { name: "Språk" });

    await user.click(screen.getByRole("radio", { name: "English" }));

    const alert = await screen.findByRole("alert");
    expect(group.getAttribute("aria-describedby")).toBe(alert.id);
  });

  // #1391: Chromium drops focus to BODY when the focused radio becomes disabled
  // (docs/reviews/2026-08-23-1391-rendered-measurement.md). The refusal must bring it back.
  it("moves focus back to the language group when the save is refused", async () => {
    let resolveSave!: (result: ActionResult) => void;
    updateMyProfileActionMock.mockReturnValue(
      new Promise<ActionResult>((resolve) => {
        resolveSave = resolve;
      }),
    );
    const user = userEvent.setup();
    renderSetting();
    const group = screen.getByRole("radiogroup", { name: "Språk" });
    const english = screen.getByRole("radio", { name: "English" });

    await user.click(english);
    expect(english).toBeDisabled();
    // jsdom retains focus on a disabled element and ignores blur(): focus the viewport explicitly.
    document.documentElement.focus();
    expect(document.activeElement).toBe(document.body);

    await act(async () => {
      resolveSave({ success: false, error: "Kunde inte na servern." });
    });
    await screen.findByRole("alert");
    await waitFor(() => expect(group.contains(document.activeElement)).toBe(true));
    expect(document.activeElement).toHaveAttribute("aria-checked", "true");
    expect(document.activeElement).toBeEnabled();
  });

  it("renders the receipt beside the segment that saved (#1391)", async () => {
    const user = userEvent.setup();
    renderSetting();

    await user.click(screen.getByRole("radio", { name: "English" }));

    const receipt = await screen.findByText(/^Sparat \d{2}:\d{2}$/);
    expect(screen.getByTestId("group")).toContainElement(receipt);
  });
});
