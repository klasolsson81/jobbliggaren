import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import GuestError from "./error";

// The harness aliases `@testing-library/react` to a render shim that wraps every
// tree in NextIntlClientProvider (messages/sv).

const boundaryError = Object.assign(new Error("guest-boom-internal"), {
  digest: "digest-guest",
});

describe("(guest)/gast/error boundary (#1477)", () => {
  it("renders the civic error surface without leaking the error to the user", () => {
    render(<GuestError error={boundaryError} unstable_retry={() => {}} />);

    expect(
      screen.getByRole("heading", { name: "Sidan kunde inte visas" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Ett tekniskt fel uppstod när innehållet skulle hämtas. Försök igen om en stund."),
    ).toBeInTheDocument();
    expect(screen.queryByText(/guest-boom-internal/)).not.toBeInTheDocument();
    expect(screen.queryByText(/digest-guest/)).not.toBeInTheDocument();
  });

  it("points back into guest mode, not out of it", () => {
    render(<GuestError error={boundaryError} unstable_retry={() => {}} />);

    expect(
      screen.getByRole("link", { name: "Till översikten" }),
    ).toHaveAttribute("href", "/gast/oversikt");
  });

  it("retry invokes Next's unstable_retry() (re-fetch + re-render the segment)", async () => {
    const unstableRetry = vi.fn();
    const user = userEvent.setup();
    render(<GuestError error={boundaryError} unstable_retry={unstableRetry} />);

    await user.click(screen.getByRole("button", { name: "Försök igen" }));

    expect(unstableRetry).toHaveBeenCalledTimes(1);
  });
});
