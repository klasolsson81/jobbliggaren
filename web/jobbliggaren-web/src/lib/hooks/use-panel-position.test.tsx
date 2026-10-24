import { afterEach, describe, expect, it, vi } from "vitest";
import { useRef } from "react";
import { act, render, screen } from "@testing-library/react";
import { usePanelPosition } from "./use-panel-position";

/**
 * The trigger rects, first-stop depths and client sizes are the layout engine's, measured 2026-10-24
 * on 5ad51c9c1 (headless Chromium, sv, unselected pills). `depth` is how far below the panel's top
 * its first focusable element ends:
 * - the Yrke pill on /jobb: at 1280x720 its left edge is 745.078125 and its bottom 387.421875; the
 *   panel opens at 395.421875 and its search field ends at 472.421875 (depth 77). At 1024x768 the
 *   pill's left edge is 617.078125 and its bottom 455.5625;
 * - the Ort trigger on /foretag/sok: at 1280x720 its left edge is 416 and its bottom 669.984375;
 *   the panel opens at 677.984375 and the first län row ends at 774.921875 (depth 96.9375), below
 *   the 720px fold. At 1920x1080 the left edge is 736 and the rest is the same, inside the fold.
 *   With the page scrolled to 517 (measured the same day on the branch, after the reveal) its top
 *   is 104.984375 and its bottom 152.984375.
 */
const YRKE_1280 = { left: 745.078125, bottom: 387.421875, depth: 77 };
const YRKE_1024 = { left: 617.078125, bottom: 455.5625, depth: 77 };
const FORETAG_ORT_1280 = { left: 416, bottom: 669.984375, depth: 96.9375 };
const FORETAG_ORT_1280_AT_517 = { left: 416, bottom: 152.984375, depth: 96.9375 };
const FORETAG_ORT_1920 = { left: 736, bottom: 669.984375, depth: 96.9375 };

const layout = { trigger: YRKE_1280, clientWidth: 1280, clientHeight: 720 };
let renders = 0;
const scrollIntoView = vi.fn();

function stubLayout() {
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function (
    this: HTMLElement,
  ) {
    const t = layout.trigger;
    // The panel's viewport top follows its own inline `top`, off screen before it is placed, and
    // the first stop sits `depth` below it, as the browser lays them out.
    const panelTop = (el: HTMLElement) => parseFloat(el.style.top) - window.scrollY;
    const r =
      this.dataset.trigger !== undefined
        ? { left: t.left, top: 0, bottom: t.bottom }
        : this.getAttribute("role") === "dialog"
          ? { left: 0, top: panelTop(this), bottom: panelTop(this) }
          : this.dataset.first !== undefined
            ? { left: 0, top: 0, bottom: panelTop(this.closest<HTMLElement>("[role=dialog]")!) + t.depth }
            : { left: 0, top: 0, bottom: 0 };
    return { x: r.left, y: r.top, left: r.left, top: r.top, right: r.left, bottom: r.bottom, width: 0, height: 0, toJSON: () => r };
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
  const panelRef = useRef<HTMLDivElement>(null);
  const { style, ready } = usePanelPosition(open, triggerRef, panelRef, 580);
  renders++;
  return (
    <>
      <button ref={triggerRef} data-trigger="">
        Pill
      </button>
      {open && (
        <div ref={panelRef} role="dialog" style={style} data-ready={ready}>
          <button data-first="">First stop</button>
          <div data-testid="column" style={{ overflowY: "auto" }} />
        </div>
      )}
    </>
  );
}

// jsdom implements no scrolling, so the method the hook calls is defined on the prototype once and
// cleared after each test.
Object.defineProperty(HTMLElement.prototype, "scrollIntoView", { configurable: true, writable: true, value: scrollIntoView });

function scrollPageTo(y: number) {
  Object.defineProperty(window, "scrollY", { configurable: true, writable: true, value: y });
}

afterEach(() => {
  vi.restoreAllMocks();
  scrollIntoView.mockClear();
  scrollPageTo(0);
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

  it("places a panel opened on a scrolled page in document coordinates, and keeps it there as the page scrolls", () => {
    Object.assign(layout, { trigger: FORETAG_ORT_1280_AT_517 });
    scrollPageTo(517);
    stubLayout();
    render(<Harness open />);
    const panel = screen.getByRole("dialog");
    expect(panel.style.top).toBe("677.984375px");
    expect(scrollIntoView).not.toHaveBeenCalled();
    const before = renders;

    // The page scrolls back to the top: the trigger moves down by as much as the offset falls.
    layout.trigger = FORETAG_ORT_1280;
    scrollPageTo(0);
    act(() => {
      window.dispatchEvent(new Event("scroll"));
    });

    expect(renders).toBe(before);
    expect(panel.style.top).toBe("677.984375px");
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

  it("scrolls the trigger to the top of the scroll padding when the first stop would open below the fold", () => {
    layout.trigger = FORETAG_ORT_1280;
    stubLayout();
    render(<Harness open />);

    expect(scrollIntoView).toHaveBeenCalledTimes(1);
    expect(scrollIntoView).toHaveBeenCalledWith({ block: "start" });
    expect(scrollIntoView.mock.contexts[0]).toBe(screen.getByRole("button", { name: "Pill" }));
    expect(screen.getByRole("dialog").dataset.ready).toBe("true");
  });

  it("leaves the page where it is when the first stop opens in view", () => {
    stubLayout();
    render(<Harness open />);

    expect(scrollIntoView).not.toHaveBeenCalled();
  });

  it("scrolls at most once per opening, never on a re-measure", () => {
    Object.assign(layout, { trigger: FORETAG_ORT_1920, clientWidth: 1920, clientHeight: 1080 });
    stubLayout();
    const { rerender } = render(<Harness open />);

    // The window shrinks to 1280x720 while the panel is open: the first län row now ends below
    // the fold, but this opening has already been placed and revealed.
    resizeTo(1280, 720, FORETAG_ORT_1280);
    expect(scrollIntoView).not.toHaveBeenCalled();

    rerender(<Harness open={false} />);
    rerender(<Harness open />);
    expect(scrollIntoView).toHaveBeenCalledTimes(1);
  });

  it("makes a sheet on a phone: no inline position, and no scroll even when the trigger sits low", () => {
    // The Ort trigger on /foretag/sok at 375x812 has its bottom at 828.96875, below the fold.
    Object.assign(layout, { trigger: { left: 20, bottom: 828.96875, depth: 96.9375 }, clientWidth: 375, clientHeight: 812 });
    stubLayout();
    render(<Harness open />);

    const panel = screen.getByRole("dialog");
    expect(panel.style.cssText).toBe("");
    expect(panel.dataset.ready).toBe("true");
    expect(scrollIntoView).not.toHaveBeenCalled();
  });

  it("keeps the trigger's own left edge and never scrolls when the document has no layout", () => {
    // jsdom only, unstubbed: zero rects and a zero-size viewport, which no browser produces for a
    // rendered page. The panel degrades to where it was before the clamp existed.
    render(<Harness open />);

    const panel = screen.getByRole("dialog");
    expect(panel.style.left).toBe("0px");
    expect(panel.style.width).toBe("580px");
    expect(scrollIntoView).not.toHaveBeenCalled();
  });
});
