import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import MarketingError from "./error";

// The harness aliases `@testing-library/react` to a render shim that wraps every
// tree in NextIntlClientProvider (messages/sv).

const boundaryError = Object.assign(new Error("landing-boom-internal"), {
  digest: "digest-landing",
});

describe("(marketing)/error boundary (#1477)", () => {
  it("renders the civic error surface without leaking the error to the user", () => {
    render(<MarketingError error={boundaryError} unstable_retry={() => {}} />);

    expect(
      screen.getByRole("heading", { name: "Något gick fel", level: 1 }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Ett tekniskt fel uppstod. Försök igen om en stund."),
    ).toBeInTheDocument();
    expect(screen.queryByText(/landing-boom-internal/)).not.toBeInTheDocument();
    expect(screen.queryByText(/digest-landing/)).not.toBeInTheDocument();
  });

  it("carries the skip-link target, because on this surface the landmark is the PAGE's", () => {
    // (marketing)/layout renders SiteHeader — whose skip link points at `#main`
    // — but the `<main>` belongs to page.tsx. When the page is what threw, this
    // boundary is the only thing left to carry the target.
    const { container } = render(
      <MarketingError error={boundaryError} unstable_retry={() => {}} />,
    );

    expect(screen.getByRole("main")).toHaveAttribute("id", "main");
    expect(container.querySelectorAll("main")).toHaveLength(1);
  });

  it("mounts no chrome of its own — the layout owns it", () => {
    // The alternative was a boundary that mounts SiteHeader/SiteFooter itself,
    // which would drag both shared RSCs, BrandLogo and the whole footer table
    // into the client bundle of the most CWV-sensitive page in the app. Bites
    // on revert.
    render(<MarketingError error={boundaryError} unstable_retry={() => {}} />);

    expect(screen.queryByRole("banner")).toBeNull();
    expect(screen.queryByRole("contentinfo")).toBeNull();
  });

  it("offers both a retry and a way back to the start page", async () => {
    const unstableRetry = vi.fn();
    const user = userEvent.setup();
    render(
      <MarketingError error={boundaryError} unstable_retry={unstableRetry} />,
    );

    expect(
      screen.getByRole("link", { name: "Till startsidan" }),
    ).toHaveAttribute("href", "/");

    await user.click(screen.getByRole("button", { name: "Försök igen" }));
    expect(unstableRetry).toHaveBeenCalledTimes(1);
  });
});
