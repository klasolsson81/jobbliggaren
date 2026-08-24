import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import AdminError from "./error";

// The harness aliases `@testing-library/react` to a render shim that wraps every
// tree in NextIntlClientProvider (messages/sv).

const boundaryError = Object.assign(new Error("admin-boom-internal"), {
  digest: "digest-admin",
});

describe("(admin)/error boundary (#1477)", () => {
  it("renders the civic error surface without leaking the error to the user", () => {
    render(<AdminError error={boundaryError} unstable_retry={() => {}} />);

    expect(
      screen.getByRole("heading", { name: "Något gick fel" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Ett tekniskt fel uppstod. Försök igen om en stund."),
    ).toBeInTheDocument();
    expect(screen.queryByText(/admin-boom-internal/)).not.toBeInTheDocument();
    expect(screen.queryByText(/digest-admin/)).not.toBeInTheDocument();
  });

  it("mounts no chrome of its own — (admin)/layout owns the strip, footer and #main", () => {
    render(<AdminError error={boundaryError} unstable_retry={() => {}} />);

    expect(screen.queryByRole("banner")).toBeNull();
    expect(screen.queryByRole("contentinfo")).toBeNull();
    expect(screen.queryByRole("main")).toBeNull();
  });

  it("retry invokes Next's unstable_retry() (re-fetch + re-render the segment)", async () => {
    const unstableRetry = vi.fn();
    const user = userEvent.setup();
    render(<AdminError error={boundaryError} unstable_retry={unstableRetry} />);

    await user.click(screen.getByRole("button", { name: "Försök igen" }));

    expect(unstableRetry).toHaveBeenCalledTimes(1);
  });
});
