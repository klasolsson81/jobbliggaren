import { StrictMode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { STALE_BUILD_RELOADED_NOTICE_KEY } from "@/lib/stale-build/stale-build-reload";
import { ReloadedAfterUpdateNotice } from "./reloaded-after-update-notice";

const route = vi.hoisted(() => ({ pathname: "/oversikt" }));
vi.mock("next/navigation", () => ({ usePathname: () => route.pathname }));

const LINE = "Jobbliggaren har uppdaterats och sidan laddades om. Gör om det du senast gjorde.";

// The stamp is what `stampAndReload` (the core) writes right before the
// document is replaced: a timestamp under the notice key.
const stamp = () => sessionStorage.setItem(STALE_BUILD_RELOADED_NOTICE_KEY, String(Date.now()));
const settle = () => new Promise((resolve) => setTimeout(resolve, 5));

describe("ReloadedAfterUpdateNotice (ADR 0148 D7)", () => {
  beforeEach(() => {
    sessionStorage.clear();
    route.pathname = "/oversikt";
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("N1: with the stamp, the line shows once inside the live region and the stamp is removed", async () => {
    stamp();
    render(<ReloadedAfterUpdateNotice placement="inline" />);

    // The container exists from the first paint; the text arrives after the read.
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
    expect(await screen.findByText(LINE)).toBeInTheDocument();
    expect(screen.getByRole("status")).toContainElement(screen.getByText(LINE));
    expect(sessionStorage.getItem(STALE_BUILD_RELOADED_NOTICE_KEY)).toBeNull();
  });

  it("N2: without the stamp nothing shows, and the empty live region is still there", async () => {
    render(<ReloadedAfterUpdateNotice placement="inline" />);

    await settle();
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
    expect(screen.queryByText(LINE)).not.toBeInTheDocument();
  });

  it("N3: a new mount after the stamp was consumed shows nothing", async () => {
    stamp();
    const first = render(<ReloadedAfterUpdateNotice placement="inline" />);
    expect(await screen.findByText(LINE)).toBeInTheDocument();
    first.unmount();

    render(<ReloadedAfterUpdateNotice placement="inline" />);
    await settle();
    expect(screen.queryByText(LINE)).not.toBeInTheDocument();
  });

  it("N-nav: the line is gone after the next navigation to another path", async () => {
    stamp();
    const { rerender } = render(<ReloadedAfterUpdateNotice placement="inline" />);
    expect(await screen.findByText(LINE)).toBeInTheDocument();

    route.pathname = "/jobb";
    rerender(<ReloadedAfterUpdateNotice placement="inline" />);
    expect(screen.queryByText(LINE)).not.toBeInTheDocument();
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
  });

  it("N-nav-back: A → B → A shows no line on the return, and the live region stays empty (one-shot)", async () => {
    stamp();
    const { rerender } = render(<ReloadedAfterUpdateNotice placement="inline" />);
    expect(await screen.findByText(LINE)).toBeInTheDocument();

    route.pathname = "/jobb";
    rerender(<ReloadedAfterUpdateNotice placement="inline" />);
    route.pathname = "/oversikt";
    rerender(<ReloadedAfterUpdateNotice placement="inline" />);
    await settle();
    expect(screen.queryByText(LINE)).not.toBeInTheDocument();
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
  });

  it("N-same: a rerender on the same path keeps the line (a filter change is still this page)", async () => {
    stamp();
    const { rerender } = render(<ReloadedAfterUpdateNotice placement="inline" />);
    expect(await screen.findByText(LINE)).toBeInTheDocument();

    rerender(<ReloadedAfterUpdateNotice placement="inline" />);
    expect(screen.getByText(LINE)).toBeInTheDocument();
  });

  it("N-strict: under <StrictMode> the line shows once and the stamp is removed", async () => {
    // Vitest's StrictMode double-renders but runs each effect's set-up ONCE here
    // (measured 2026-10-03: react-dom 19.3.0 double-invokes effects only for a root
    // created strict), so this row pins the double RENDER only; the double set-up
    // is measured on `next dev` (session log, #1948).
    stamp();
    render(
      <StrictMode>
        <ReloadedAfterUpdateNotice placement="inline" />
      </StrictMode>,
    );

    expect(await screen.findByText(LINE)).toBeInTheDocument();
    expect(screen.getAllByText(LINE)).toHaveLength(1);
    expect(sessionStorage.getItem(STALE_BUILD_RELOADED_NOTICE_KEY)).toBeNull();
  });

  it("storage that cannot be read shows nothing and throws nothing", async () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new DOMException("blocked", "SecurityError");
    });
    render(<ReloadedAfterUpdateNotice placement="inline" />);

    await settle();
    expect(screen.queryByText(LINE)).not.toBeInTheDocument();
  });

  describe("placement (design-reviewer Major 2 on #1955): the layout chooses the rail and the gap", () => {
    it("rail: the region carries the content rail and the line brings the gap above (the band below brings its own)", async () => {
      stamp();
      render(<ReloadedAfterUpdateNotice placement="rail" />);

      expect(screen.getByRole("status")).toHaveClass("jp-container", "w-full");
      expect(await screen.findByText(LINE)).toHaveClass("jp-banner", "jp-banner--before-band");
    });

    it("inline: no rail of its own, and the line brings the gap below", async () => {
      stamp();
      render(<ReloadedAfterUpdateNotice placement="inline" />);

      expect(screen.getByRole("status")).not.toHaveAttribute("class");
      const line = await screen.findByText(LINE);
      expect(line).toHaveClass("jp-banner");
      expect(line).not.toHaveClass("jp-banner--before-band");
    });

    it.each([
      ["/oversikt", "rail"],
      ["/jobb/123", "rail"],
      ["/sokningar", "inline"],
      ["/sparade", "inline"],
    ] as const)("app on %s resolves to %s — the test AppShell makes (isV3Native)", async (pathname, expected) => {
      route.pathname = pathname;
      stamp();
      render(<ReloadedAfterUpdateNotice placement="app" />);

      const line = await screen.findByText(LINE);
      if (expected === "rail") {
        expect(screen.getByRole("status")).toHaveClass("jp-container");
        expect(line).toHaveClass("jp-banner--before-band");
      } else {
        expect(screen.getByRole("status")).not.toHaveAttribute("class");
        expect(line).not.toHaveClass("jp-banner--before-band");
      }
    });
  });
});
