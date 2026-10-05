import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderToString } from "react-dom/server";
import { AdDescriptionExcerpt } from "./ad-description-excerpt";

// jsdom has no layout and no ResizeObserver, so the two heights the island compares are stubbed:
// the text's own height and the probe that holds the excerpt's height.
let textHeight = 0;
const EXCERPT = 300;

beforeEach(() => {
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function (this: HTMLElement) {
    const height = this.classList.contains("jp-modal__description")
      ? textHeight
      : this.getAttribute("aria-hidden") === "true"
        ? EXCERPT
        : 0;
    return { height, width: 0, top: 0, left: 0, right: 0, bottom: height, x: 0, y: 0, toJSON: () => ({}) } as DOMRect;
  });
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const excerpt = () => (
  <AdDescriptionExcerpt snapshotId="19630000-0000-4000-8000-000000000101" showFullLabel="Visa hela annonsen" showLessLabel="Visa mindre">
    <p>Vi söker en mjukvaruutvecklare.</p>
  </AdDescriptionExcerpt>
);

describe("AdDescriptionExcerpt (#1963)", () => {
  it("clamps a text that runs past the excerpt by more than the fade, behind a toggle that names and controls it", async () => {
    textHeight = 900;
    const { container } = render(excerpt());
    const clip = container.querySelector("[data-collapsed]");
    expect(clip).not.toBeNull();

    const toggle = screen.getByRole("button", { name: "Visa hela annonsen" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(toggle).toHaveAttribute("aria-controls", clip?.id);

    await userEvent.setup().click(toggle);
    expect(screen.getByRole("button", { name: "Visa mindre" })).toHaveAttribute("aria-expanded", "true");
    expect(container.querySelector("[data-collapsed]")).toBeNull();
  });

  it("shows a text that runs past the excerpt by less than the fade in full, with no toggle", () => {
    textHeight = EXCERPT + 50;
    const { container } = render(excerpt());
    expect(container.querySelector("[data-collapsed]")).toBeNull();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("drops the clamp and the toggle when the text fits", () => {
    textHeight = 120;
    const { container } = render(excerpt());
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    expect(container.querySelector("[data-collapsed]")).toBeNull();
    expect(screen.getByText("Vi söker en mjukvaruutvecklare.")).toBeInTheDocument();
  });

  it("serves the collapsed form from the server, so a long ad does not shift on load", () => {
    const html = renderToString(excerpt());
    expect(html).toContain("data-collapsed");
    expect(html).toContain("Visa hela annonsen");
  });

  it("re-measures when the observed size changes", () => {
    const observers: Array<() => void> = [];
    vi.stubGlobal(
      "ResizeObserver",
      class {
        constructor(callback: () => void) {
          observers.push(callback);
        }
        observe() {}
        disconnect() {}
      },
    );
    textHeight = 120;
    render(excerpt());
    expect(screen.queryByRole("button")).not.toBeInTheDocument();

    textHeight = 900;
    act(() => observers.forEach((callback) => callback()));
    expect(screen.getByRole("button", { name: "Visa hela annonsen" })).toBeInTheDocument();
  });

  it("brings the toggle back into view when the text closes again", async () => {
    const scrollIntoView = vi.fn();
    Object.defineProperty(HTMLElement.prototype, "scrollIntoView", { configurable: true, value: scrollIntoView });
    try {
      textHeight = 900;
      render(excerpt());
      const user = userEvent.setup();
      await user.click(screen.getByRole("button", { name: "Visa hela annonsen" }));
      expect(scrollIntoView).not.toHaveBeenCalled();

      await user.click(screen.getByRole("button", { name: "Visa mindre" }));
      expect(scrollIntoView).toHaveBeenCalledTimes(1);
      expect(scrollIntoView).toHaveBeenCalledWith({ block: "nearest" });
      expect(scrollIntoView.mock.contexts[0]).toBe(screen.getByRole("button", { name: "Visa hela annonsen" }));
    } finally {
      delete (HTMLElement.prototype as { scrollIntoView?: unknown }).scrollIntoView;
    }
  });
});
