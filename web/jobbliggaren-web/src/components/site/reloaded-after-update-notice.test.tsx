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

  it("N-strict: under StrictMode (next dev's default) the line shows once and the stamp is removed", async () => {
    // RTL's option puts <StrictMode> above the intl wrapper, where react-dom's effect
    // double-invoke reaches it; nested under the wrapper it is not reached (measured).
    stamp();
    render(<ReloadedAfterUpdateNotice placement="inline" />, { reactStrictMode: true });

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
    it("rail: the band and the content rail arrive with the line, which brings the gap above (the band below brings its own)", async () => {
      stamp();
      render(<ReloadedAfterUpdateNotice placement="rail" />);

      // Empty: no class, so no colour and no height.
      expect(screen.getByRole("status")).not.toHaveAttribute("class");
      const line = await screen.findByText(LINE);
      expect(screen.getByRole("status")).toHaveClass("jp-banner-band");
      expect(line.parentElement).toHaveClass("jp-container");
      expect(line).toHaveClass("jp-banner", "jp-banner--before-band");
    });

    it("plate: the band and the rail, and the line brings the gap on both sides (an edge-to-edge plate below)", async () => {
      stamp();
      render(<ReloadedAfterUpdateNotice placement="plate" />);

      const line = await screen.findByText(LINE);
      expect(screen.getByRole("status")).toHaveClass("jp-banner-band");
      expect(line.parentElement).toHaveClass("jp-container");
      expect(line).toHaveClass("jp-banner", "jp-banner--before-plate");
      expect(line).not.toHaveClass("jp-banner--before-band");
    });

    it("inline: no rail of its own, and the line brings the gap below", async () => {
      stamp();
      render(<ReloadedAfterUpdateNotice placement="inline" />);

      const line = await screen.findByText(LINE);
      expect(screen.getByRole("status")).not.toHaveAttribute("class");
      expect(line.parentElement).toBe(screen.getByRole("status"));
      expect(line).toHaveClass("jp-banner");
      expect(line).not.toHaveClass("jp-banner--before-band");
    });
  });
});
