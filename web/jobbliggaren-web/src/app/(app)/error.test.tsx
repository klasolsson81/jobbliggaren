import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import AppError from "./error";

// The harness aliases `@testing-library/react` to a render shim that wraps every
// tree in NextIntlClientProvider (messages/sv).

const boundaryError = Object.assign(new Error("boom-internal-detail"), {
  digest: "digest-123",
});

describe("(app)/error boundary (#995)", () => {
  it("renders the civic error surface without leaking the error to the user", () => {
    render(<AppError error={boundaryError} unstable_retry={() => {}} />);

    expect(
      screen.getByRole("heading", { name: "Sidan kunde inte visas" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Ett tekniskt fel uppstod när innehållet skulle hämtas. Försök igen om en stund."),
    ).toBeInTheDocument();

    // Acceptance: no stack trace / internal detail is shown to the user.
    expect(screen.queryByText(/boom-internal-detail/)).not.toBeInTheDocument();
    expect(screen.queryByText(/digest-123/)).not.toBeInTheDocument();
  });

  it("offers a way back to the overview", () => {
    render(<AppError error={boundaryError} unstable_retry={() => {}} />);

    const toOverview = screen.getByRole("link", { name: "Till översikten" });
    expect(toOverview).toHaveAttribute("href", "/oversikt");
  });

  it("retry invokes Next's unstable_retry() (re-fetch + re-render the segment)", async () => {
    const unstableRetry = vi.fn();
    const user = userEvent.setup();
    render(<AppError error={boundaryError} unstable_retry={unstableRetry} />);

    await user.click(screen.getByRole("button", { name: "Försök igen" }));

    expect(unstableRetry).toHaveBeenCalledTimes(1);
  });

  it("moves focus to the heading when the boundary mounts (WCAG 4.1.3)", () => {
    // The PROPERTY, not the attribute. A throw caught after hydration swaps this
    // subtree in place; that is not a navigation, so Next's route announcer never
    // re-runs and the element that had focus is unmounted with the old subtree.
    // Bites on revert twice over: remove the ref and focus stays on <body>, remove
    // the tabIndex and .focus() is a silent no-op on a heading.
    render(<AppError error={boundaryError} unstable_retry={() => {}} />);

    expect(document.activeElement).toBe(
      screen.getByRole("heading", { level: 1 }),
    );
  });
});
