import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import MarketingInnerError from "./error";

// The harness aliases `@testing-library/react` to a render shim that wraps every
// tree in NextIntlClientProvider (messages/sv).

const boundaryError = Object.assign(new Error("inner-boom-internal"), {
  digest: "digest-inner",
});

describe("(marketing-inner)/error boundary (#1477)", () => {
  it("renders the civic error surface without leaking the error to the user", () => {
    render(
      <MarketingInnerError error={boundaryError} unstable_retry={() => {}} />,
    );

    expect(
      screen.getByRole("heading", { name: "Sidan kunde inte visas" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Ett tekniskt fel uppstod när sidan skulle hämtas. Försök igen om en stund."),
    ).toBeInTheDocument();
    expect(screen.queryByText(/inner-boom-internal/)).not.toBeInTheDocument();
    expect(screen.queryByText(/digest-inner/)).not.toBeInTheDocument();
  });

  it("carries the skip-link target, because in this group the landmark is the PAGE's", () => {
    // (marketing-inner)/layout renders SiteHeader — whose skip link points at
    // `#main` — but the `<main>` itself lives on each page (#284). When the page
    // is what threw, this boundary is the only thing left to carry the target.
    const { container } = render(
      <MarketingInnerError error={boundaryError} unstable_retry={() => {}} />,
    );

    const main = screen.getByRole("main");
    expect(main).toHaveAttribute("id", "main");
    expect(container.querySelectorAll("main")).toHaveLength(1);
  });

  it("offers both a retry and a way back to the start page", async () => {
    const unstableRetry = vi.fn();
    const user = userEvent.setup();
    render(
      <MarketingInnerError error={boundaryError} unstable_retry={unstableRetry} />,
    );

    expect(
      screen.getByRole("link", { name: "Till startsidan" }),
    ).toHaveAttribute("href", "/");

    await user.click(screen.getByRole("button", { name: "Försök igen" }));
    expect(unstableRetry).toHaveBeenCalledTimes(1);
  });
});
