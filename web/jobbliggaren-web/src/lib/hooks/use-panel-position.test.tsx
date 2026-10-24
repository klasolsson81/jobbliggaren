import { afterEach, describe, expect, it, vi } from "vitest";
import { useRef } from "react";
import { act, render, screen } from "@testing-library/react";
import { usePanelPosition } from "./use-panel-position";

/**
 * The trigger rects and client sizes are the layout engine's for the Yrke pill on /jobb, measured
 * 2026-10-24 on 5ad51c9c1 (headless Chromium, sv, unselected pills): at 1280x720 its left edge is
 * 745.078125 and its bottom 387.421875; at 1024x768 they are 617.078125 and 455.5625.
 */
const YRKE_1280 = { left: 745.078125, bottom: 387.421875 };
const YRKE_1024 = { left: 617.078125, bottom: 455.5625 };

const layout = { trigger: YRKE_1280, clientWidth: 1280, clientHeight: 720 };
let renders = 0;

function stubLayout() {
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function (
    this: HTMLElement,
  ) {
    const r = this.dataset.trigger !== undefined ? layout.trigger : { left: 0, bottom: 0 };
    return { x: r.left, y: 0, left: r.left, top: 0, right: r.left, bottom: r.bottom, width: 0, height: 0, toJSON: () => r };
  });
  vi.spyOn(Element.prototype, "clientWidth", "get").mockImplementation(function (this: Element) {
    return this === document.documentElement ? layout.clientWidth : 0;
  });
  vi.spyOn(Element.prototype, "clientHeight", "get").mockImplementation(function (this: Element) {
    return this === document.documentElement ? layout.clientHeight : 0;
  });
}

function Harness({ open }: { open: boolean }) {
  const triggerRef = useRef<HTMLButtonElement>(null);
  const { style, ready } = usePanelPosition(open, triggerRef, 580);
  renders++;
  return (
    <>
      <button ref={triggerRef} data-trigger="">
        Yrke
      </button>
      {open && (
        <div role="dialog" style={style} data-ready={ready}>
          <div data-testid="column" style={{ overflowY: "auto" }} />
        </div>
      )}
    </>
  );
}

afterEach(() => {
  vi.restoreAllMocks();
  Object.assign(layout, { trigger: YRKE_1280, clientWidth: 1280, clientHeight: 720 });
});

function resizeTo(clientWidth: number, clientHeight: number, trigger: typeof YRKE_1280) {
  Object.assign(layout, { trigger, clientWidth, clientHeight });
  act(() => {
    window.dispatchEvent(new Event("resize"));
  });
}

describe("usePanelPosition (#2212)", () => {
  it("places the open panel inside the viewport and marks it ready", () => {
    stubLayout();
    render(<Harness open />);

    const panel = screen.getByRole("dialog");
    expect(panel.style.left).toBe("684px");
    expect(panel.style.top).toBe("395.421875px");
    expect(panel.style.width).toBe("580px");
    expect(panel.dataset.ready).toBe("true");
  });

  it("re-measures when the window is resized", () => {
    stubLayout();
    render(<Harness open />);

    resizeTo(1024, 768, YRKE_1024);

    expect(screen.getByRole("dialog").style.left).toBe("428px");
  });

  it("renders nothing more when the page or a column scrolls", () => {
    stubLayout();
    render(<Harness open />);
    const before = renders;

    act(() => {
      window.dispatchEvent(new Event("scroll"));
      screen.getByTestId("column").dispatchEvent(new Event("scroll"));
    });

    expect(renders).toBe(before);
  });

  it("stops listening once the panel closes, and measures afresh when it opens again", () => {
    stubLayout();
    const { rerender } = render(<Harness open />);
    rerender(<Harness open={false} />);
    const closed = renders;

    resizeTo(1024, 768, YRKE_1024);
    expect(renders).toBe(closed);

    rerender(<Harness open />);
    expect(screen.getByRole("dialog").style.left).toBe("428px");
  });

  it("keeps the trigger's own left edge when the document has no layout", () => {
    // jsdom only, unstubbed: zero rects and a zero-size viewport, which no browser produces for a
    // rendered page. The panel degrades to where it was before the clamp existed.
    render(<Harness open />);

    const panel = screen.getByRole("dialog");
    expect(panel.style.left).toBe("0px");
    expect(panel.style.width).toBe("580px");
  });
});
