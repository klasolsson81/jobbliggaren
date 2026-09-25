import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { ProviderButtons } from "./provider-buttons";

// A `next/link` prefetch would mint a flow for a visitor who never pressed the row.
vi.mock("next/link", () => ({
  default: () => {
    throw new Error("an active provider row must be a plain <a>, never next/link");
  },
}));

const MARK_SRC = "/provider-marks/google-g-light-square-4x.png";

describe("ProviderButtons with Google active", () => {
  it("makes the Google row a link to its start, the rest staying inactive, in reach order", () => {
    render(<ProviderButtons active={["google"]} />);

    expect(screen.getAllByRole("listitem").map((item) => item.textContent)).toEqual([
      "Fortsätt med Google",
      "Fortsätt med LinkedInKommer snart",
      "Fortsätt med GitHubKommer snart",
    ]);
    const google = screen.getByRole("link", { name: "Fortsätt med Google" });
    expect(google).toHaveAttribute("href", "/api/auth/oauth/google/start");
    expect(google).toHaveAttribute("data-variant", "outline");
    expect(google).not.toHaveAttribute("aria-disabled");
    for (const button of screen.getAllByRole("button")) {
      expect(button).toHaveAttribute("aria-disabled", "true");
    }
  });

  it("carries next to the start encoded, as the page was given it", () => {
    render(<ProviderButtons active={["google"]} next="/ansokningar?filter=a&b" />);

    expect(screen.getByRole("link", { name: "Fortsätt med Google" })).toHaveAttribute(
      "href",
      "/api/auth/oauth/google/start?next=%2Fansokningar%3Ffilter%3Da%26b"
    );
  });

  it("is described by the line the page names", () => {
    render(<ProviderButtons active={["google"]} describedBy="login-persistence" />);

    expect(screen.getByRole("link", { name: "Fortsätt med Google" })).toHaveAttribute(
      "aria-describedby",
      "login-persistence"
    );
  });

  it("draws the provider's own mark on the active row only, hidden from the accessibility tree", () => {
    const { container } = render(<ProviderButtons active={["google"]} />);

    const marks = container.querySelectorAll("img");
    expect(marks).toHaveLength(1);
    const mark = marks[0]!;
    expect(mark).toHaveAttribute("src", MARK_SRC);
    expect(mark).toHaveAttribute("alt", "");
    expect(mark.closest("[aria-hidden='true']")).not.toBeNull();
    expect(within(screen.getByRole("link")).queryByRole("img")).toBeNull();
    expect(container.querySelector("svg")).toBeNull();
  });

  it("puts the row in no form: the CSP's form-action would refuse a form that leaves for the provider", () => {
    render(<ProviderButtons active={["google"]} />);

    expect(screen.getByRole("link", { name: "Fortsätt med Google" }).closest("form")).toBeNull();
  });
});
