import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import GlobalError from "./global-error";

// global-error renders its own <html>/<body> and seeds its OWN
// NextIntlClientProvider (locale sv, pages namespace). The render shim's outer
// provider is harmless — the boundary's inner provider governs its subtree, the
// same isolation production sees when no root provider exists. jsdom emits a
// nesting warning for <html> inside the render container; it does not affect the
// queried text.

const rootError = Object.assign(new Error("root-layout-crash"), {
  digest: "digest-abc",
});

describe("global-error boundary (#995)", () => {
  it("renders the civic last-resort surface, no internal detail leaked", () => {
    render(<GlobalError error={rootError} reset={() => {}} />);

    expect(
      screen.getByRole("heading", { name: "Något gick fel" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        "Ett tekniskt fel uppstod. Försök ladda om sidan om en stund.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText(/root-layout-crash/)).not.toBeInTheDocument();
    expect(screen.queryByText(/digest-abc/)).not.toBeInTheDocument();
  });

  it("offers a way back to the start page", () => {
    render(<GlobalError error={rootError} reset={() => {}} />);

    const toStart = screen.getByRole("link", { name: "Till startsidan" });
    expect(toStart).toHaveAttribute("href", "/");
  });

  it("retry invokes reset()", async () => {
    const reset = vi.fn();
    const user = userEvent.setup();
    render(<GlobalError error={rootError} reset={reset} />);

    await user.click(screen.getByRole("button", { name: "Försök igen" }));

    expect(reset).toHaveBeenCalledTimes(1);
  });
});
