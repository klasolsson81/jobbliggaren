import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import AuthError from "./error";

// The harness aliases `@testing-library/react` to a render shim that wraps every
// tree in NextIntlClientProvider (messages/sv), so this boundary's
// `useTranslations("common")` resolves without a manual provider.

const boundaryError = Object.assign(new Error("auth-boom-internal"), {
  digest: "digest-auth",
});

describe("(auth)/error boundary (#1477)", () => {
  it("renders the civic error surface without leaking the error to the user", () => {
    render(<AuthError error={boundaryError} unstable_retry={() => {}} />);

    expect(
      screen.getByRole("heading", { name: "Något gick fel" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Ett tekniskt fel uppstod. Försök igen om en stund."),
    ).toBeInTheDocument();
    expect(screen.queryByText(/auth-boom-internal/)).not.toBeInTheDocument();
    expect(screen.queryByText(/digest-auth/)).not.toBeInTheDocument();
  });

  it("retry invokes Next's unstable_retry() (re-fetch + re-render the segment)", async () => {
    const unstableRetry = vi.fn();
    const user = userEvent.setup();
    render(<AuthError error={boundaryError} unstable_retry={unstableRetry} />);

    await user.click(screen.getByRole("button", { name: "Försök igen" }));

    expect(unstableRetry).toHaveBeenCalledTimes(1);
  });

  it("offers no way back of its own — (auth)/layout owns the back link", () => {
    // Two controls labelled "Till startsidan" stacked on top of each other is
    // the defect this boundary must not introduce: the layout renders that link
    // above `children`, and error.tsx IS the children. Bites if a later edit
    // adds a duplicate here.
    render(<AuthError error={boundaryError} unstable_retry={() => {}} />);

    expect(screen.queryAllByRole("link")).toHaveLength(0);
  });
});
