import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import AppError from "./error";

// The harness aliases `@testing-library/react` to a render shim that wraps every
// tree in NextIntlClientProvider (messages/sv), so this client boundary's
// `useTranslations("pages")` resolves without a manual provider (see
// vitest.config.ts / src/test/render-intl.tsx).

const boundaryError = Object.assign(new Error("boom-internal-detail"), {
  digest: "digest-123",
});

describe("(app)/error boundary (#995)", () => {
  it("renders the civic error surface without leaking the error to the user", () => {
    render(<AppError error={boundaryError} reset={() => {}} />);

    expect(
      screen.getByRole("heading", { name: "Något gick fel" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Ett tekniskt fel uppstod. Försök igen om en stund."),
    ).toBeInTheDocument();

    // Acceptance: no stack trace / internal detail is shown to the user.
    expect(screen.queryByText(/boom-internal-detail/)).not.toBeInTheDocument();
    expect(screen.queryByText(/digest-123/)).not.toBeInTheDocument();
  });

  it("offers a way back to the overview", () => {
    render(<AppError error={boundaryError} reset={() => {}} />);

    const toOverview = screen.getByRole("link", { name: "Till översikten" });
    expect(toOverview).toHaveAttribute("href", "/oversikt");
  });

  it("retry invokes Next's reset() to re-render the segment", async () => {
    const reset = vi.fn();
    const user = userEvent.setup();
    render(<AppError error={boundaryError} reset={reset} />);

    await user.click(screen.getByRole("button", { name: "Försök igen" }));

    expect(reset).toHaveBeenCalledTimes(1);
  });
});
