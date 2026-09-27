import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const { updateMyProfileActionMock } = vi.hoisted(() => ({
  updateMyProfileActionMock: vi.fn(),
}));

vi.mock("@/lib/actions/me", () => ({
  updateMyProfileAction: updateMyProfileActionMock,
  requestEmailChangeAction: vi.fn(),
  confirmEmailChangeAction: vi.fn(),
}));
vi.mock("@/i18n/set-locale-action", () => ({ setLocaleAction: vi.fn().mockResolvedValue(undefined) }));
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: vi.fn(), push: vi.fn(), back: vi.fn() }),
}));

import { AccountSection } from "./account-section";

const EMAIL = "klas@example.se";

/** The group a node lives in: the unit a receipt must land in (#1391). */
function groupOf(node: Element): HTMLElement | null {
  return node.closest<HTMLElement>(".jp-settings-group");
}

beforeEach(() => {
  updateMyProfileActionMock.mockReset();
  updateMyProfileActionMock.mockResolvedValue({ success: true });
});

describe("AccountSection", () => {
  it("is one card, Konto, with the address and the language as its groups", () => {
    render(<AccountSection email={EMAIL} language="sv" />);
    expect(screen.getByRole("heading", { level: 2, name: "Konto" })).toBeInTheDocument();
    expect(
      screen.getAllByRole("heading", { level: 3 }).map((heading) => heading.textContent),
    ).toEqual(["Byt e-postadress", "Språk"]);
    expect(screen.getByText(`Din e-postadress är ${EMAIL}.`)).toBeInTheDocument();
    expect(screen.getByRole("radiogroup", { name: "Språk" })).toBeInTheDocument();
  });

  it("has no phone field (CTO Val 4B: the profile has no phone)", () => {
    render(<AccountSection email={EMAIL} language="sv" />);
    expect(screen.queryByLabelText(/Telefon/i)).not.toBeInTheDocument();
  });

  it("lands a saved language's receipt in the language group, not the address group (#1391)", async () => {
    const user = userEvent.setup();
    render(<AccountSection email={EMAIL} language="sv" />);
    const group = screen.getByRole("radiogroup", { name: "Språk" });

    await user.click(screen.getByRole("radio", { name: "English" }));

    const receipt = await screen.findByText(/^Sparat \d{2}:\d{2}$/);
    expect(groupOf(receipt)).toBe(groupOf(group));
  });

  // Changing the address reads only the session's address, so no profile result may take it away
  // (design-reviewer Major 3, #1740). The language says why it is missing.
  it.each([
    ["an error", undefined, /Inställningarna kunde inte läsas in just nu/],
    ["a rate limit", 30, /för många förfrågningar på kort tid/],
  ])("keeps the address change when the profile read ends in %s", (_kind, retryAfterSeconds, message) => {
    render(<AccountSection email={EMAIL} language={null} retryAfterSeconds={retryAfterSeconds} />);
    expect(screen.getByLabelText("Ny e-postadress")).toBeInTheDocument();
    expect(screen.queryByRole("radiogroup", { name: "Språk" })).not.toBeInTheDocument();
    expect(screen.getByText(message)).toBeInTheDocument();
  });
});
