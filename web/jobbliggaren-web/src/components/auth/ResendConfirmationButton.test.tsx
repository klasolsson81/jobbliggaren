import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ResendConfirmationButton } from "./ResendConfirmationButton";

// The real server action does fetch() + reads env; mock it so the island calls our spy instead.
type ActionResult = { success: true } | { success: false; error: string };
const resendMock = vi.fn<(email: string) => Promise<ActionResult>>();

vi.mock("@/lib/actions/resend-confirmation", () => ({
  resendConfirmationAction: (email: string) => resendMock(email),
}));

const BUTTON_LABEL = "Skicka en ny bekräftelselänk";
const SENT_MESSAGE =
  "Om adressen behöver bekräftas har vi skickat en ny länk. Kontrollera inkorgen och skräpposten.";

describe("ResendConfirmationButton", () => {
  beforeEach(() => {
    resendMock.mockReset();
    resendMock.mockResolvedValue({ success: true });
  });

  it("renders the resend button", () => {
    render(<ResendConfirmationButton getEmail={() => "anna@example.se"} />);
    expect(
      screen.getByRole("button", { name: BUTTON_LABEL })
    ).toBeInTheDocument();
  });

  it("does nothing when the email is empty (no action call)", async () => {
    const user = userEvent.setup();
    render(<ResendConfirmationButton getEmail={() => "   "} />);

    await user.click(screen.getByRole("button", { name: BUTTON_LABEL }));

    expect(resendMock).not.toHaveBeenCalled();
  });

  it("calls the action with the email, shows the uniform sent message and disables the button on success", async () => {
    const user = userEvent.setup();
    render(<ResendConfirmationButton getEmail={() => "anna@example.se"} />);

    await user.click(screen.getByRole("button", { name: BUTTON_LABEL }));

    expect(resendMock).toHaveBeenCalledWith("anna@example.se");
    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent(SENT_MESSAGE)
    );
    // Success starts the 60s cooldown → the button is disabled.
    expect(screen.getByRole("button", { name: BUTTON_LABEL })).toBeDisabled();
  });

  it("shows the error message and keeps the button enabled on failure", async () => {
    resendMock.mockResolvedValue({
      success: false,
      error: "Det gick inte att skicka just nu. Försök igen om en stund.",
    });
    const user = userEvent.setup();
    render(<ResendConfirmationButton getEmail={() => "anna@example.se"} />);

    await user.click(screen.getByRole("button", { name: BUTTON_LABEL }));

    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent(
        "Det gick inte att skicka just nu. Försök igen om en stund."
      )
    );
    // No cooldown on failure → the user can retry immediately.
    expect(screen.getByRole("button", { name: BUTTON_LABEL })).toBeEnabled();
  });
});
