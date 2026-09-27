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
const GITHUB_MARK_SRC = "/provider-marks/github-invertocat-black.png";

const markOf = (link: HTMLElement): HTMLImageElement | null => link.querySelector("img");

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

// `active` is the api's providers list: `RegisteredProviders.Keys`, in `ExternalProviderKey.Known` order
// (google, github).
describe("ProviderButtons with GitHub active", () => {
  it("makes the GitHub row a link to its start with its own mark, Google and LinkedIn staying inactive", () => {
    const { container } = render(<ProviderButtons active={["github"]} />);

    expect(screen.getAllByRole("listitem").map((item) => item.textContent)).toEqual([
      "Fortsätt med GoogleKommer snart",
      "Fortsätt med LinkedInKommer snart",
      "Fortsätt med GitHub",
    ]);
    const github = screen.getByRole("link", { name: "Fortsätt med GitHub" });
    expect(github).toHaveAttribute("href", "/api/auth/oauth/github/start");
    expect(screen.getAllByRole("button")).toHaveLength(2);
    for (const button of screen.getAllByRole("button")) {
      expect(button).toHaveAttribute("aria-disabled", "true");
    }
    const marks = container.querySelectorAll("img");
    expect(marks).toHaveLength(1);
    expect(markOf(github)).toHaveAttribute("src", GITHUB_MARK_SRC);
    expect(markOf(github)).toHaveAttribute("alt", "");
    expect(markOf(github)!.closest("[aria-hidden='true']")).not.toBeNull();
  });

  // jsdom lays nothing out: the rendered geometry is measured in a browser (DoD 4), and this pins what
  // the browser is handed.
  it("fits GitHub's frameless mark whole in the 20x20 slot, where Google's 40x40 overflows it by layout", () => {
    render(<ProviderButtons active={["google", "github"]} />);

    const google = markOf(screen.getByRole("link", { name: "Fortsätt med Google" }))!;
    const github = markOf(screen.getByRole("link", { name: "Fortsätt med GitHub" }))!;
    expect(google).toHaveAttribute("width", "40");
    expect(google).toHaveAttribute("height", "40");
    expect(google).toHaveAttribute("class", "absolute -top-2.5 -left-2.5 h-10 w-10 max-w-none");
    expect(github).toHaveAttribute("width", "20");
    expect(github).toHaveAttribute("height", "20");
    expect(github).toHaveAttribute("class", "size-5 object-contain");
    for (const mark of [google, github]) {
      expect(mark.parentElement).toHaveAttribute("class", "relative size-5 shrink-0 overflow-hidden");
    }
  });
});

describe("ProviderButtons with Google and GitHub active", () => {
  it("links each row to its own start, each with its own mark, in the page's order", () => {
    render(<ProviderButtons active={["google", "github"]} />);

    expect(screen.getAllByRole("listitem").map((item) => item.textContent)).toEqual([
      "Fortsätt med Google",
      "Fortsätt med LinkedInKommer snart",
      "Fortsätt med GitHub",
    ]);
    const links = screen.getAllByRole("link");
    expect(links.map((link) => [link.textContent, link.getAttribute("href"), markOf(link)?.getAttribute("src")])).toEqual([
      ["Fortsätt med Google", "/api/auth/oauth/google/start", MARK_SRC],
      ["Fortsätt med GitHub", "/api/auth/oauth/github/start", GITHUB_MARK_SRC],
    ]);
  });

  // Declared unreachable: the api lists its keys in `ExternalProviderKey.Known` order, which is the page's
  // order for the two it knows. Only that the page keeps its own order is asserted.
  it("keeps the page's order when the api lists the providers in another", () => {
    render(<ProviderButtons active={["github", "google"]} />);

    expect(screen.getAllByRole("link").map((link) => link.textContent)).toEqual([
      "Fortsätt med Google",
      "Fortsätt med GitHub",
    ]);
  });

  // #1746: the persistence line left /logga-in (ADR 0142 Amendment (19)), so no row points at it.
  it("describes neither row, and puts neither in a form", () => {
    render(<ProviderButtons active={["google", "github"]} />);

    const links = screen.getAllByRole("link");
    expect(links).toHaveLength(2);
    for (const link of links) {
      expect(link).not.toHaveAttribute("aria-describedby");
      expect(link.closest("form")).toBeNull();
    }
  });

  // Klas's note on the rows (#1746). jsdom lays nothing out, so the geometry is measured rendered (DoD 4); this
  // pins that every active row is one shape and every inactive row another, with the mark before the text.
  it("gives every active row one shape and every inactive row another, the mark before the text", () => {
    const { unmount } = render(<ProviderButtons active={["google", "github"]} />);

    const links = screen.getAllByRole("link");
    expect(new Set(links.map((link) => link.className)).size).toBe(1);
    expect(links[0]!.className).toContain("justify-center");
    for (const link of links) {
      expect(link.firstElementChild).toHaveAttribute("aria-hidden", "true");
      expect(link.lastElementChild).toHaveTextContent(/^Fortsätt med /);
    }
    unmount();

    render(<ProviderButtons />);
    const inactive = screen.getAllByRole("button");
    expect(inactive).toHaveLength(3);
    expect(new Set(inactive.map((button) => button.className)).size).toBe(1);
    expect(inactive[0]!.className).toContain("justify-between");
  });
});
