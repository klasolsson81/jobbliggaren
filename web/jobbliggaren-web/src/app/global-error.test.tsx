import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import GlobalError from "./global-error";

// global-error renders its own <html>/<body> and seeds its OWN
// NextIntlClientProvider (locale sv, the fallback namespace). The render shim's outer
// provider is harmless — the boundary's inner provider governs its subtree, the
// same isolation production sees when no root provider exists. jsdom emits a
// nesting warning for <html> inside the render container; it does not affect the
// queried text.

const rootError = Object.assign(new Error("root-layout-crash"), {
  digest: "digest-abc",
});

describe("global-error boundary (#995)", () => {
  it("renders the civic last-resort surface, no internal detail leaked", () => {
    render(<GlobalError error={rootError} retry={() => {}} reset={() => {}} />);

    expect(
      screen.getByRole("heading", { name: "Sidan kunde inte visas" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        "Ett tekniskt fel uppstod. Försök igen om en stund.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText(/root-layout-crash/)).not.toBeInTheDocument();
    expect(screen.queryByText(/digest-abc/)).not.toBeInTheDocument();
  });

  it("names the site in the tab title, which Next's title.template cannot reach here", () => {
    render(<GlobalError error={rootError} retry={() => {}} reset={() => {}} />);

    // React hoists <title> into document.head. Bites on revert: dropping the
    // template composition leaves the tab without the site name.
    expect(document.title).toBe("Sidan kunde inte visas | Jobbliggaren");
  });

  it("offers a way back to the start page", () => {
    render(<GlobalError error={rootError} retry={() => {}} reset={() => {}} />);

    const toStart = screen.getByRole("link", { name: "Till startsidan" });
    expect(toStart).toHaveAttribute("href", "/");
  });


  it("moves focus to the heading when the boundary mounts (WCAG 4.1.3)", () => {
    // The PROPERTY, not the attribute. Bites on revert twice over: remove the ref
    // and focus stays on <body>, remove the tabIndex and .focus() is a silent
    // no-op on a heading.
    render(<GlobalError error={rootError} retry={() => {}} reset={() => {}} />);

    expect(document.activeElement).toBe(
      screen.getByRole("heading", { level: 1 }),
    );
  });
});
