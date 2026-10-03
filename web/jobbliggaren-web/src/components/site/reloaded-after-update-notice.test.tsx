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
    render(<ReloadedAfterUpdateNotice />);

    // The container exists from the first paint; the text arrives after the read.
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
    expect(await screen.findByText(LINE)).toBeInTheDocument();
    expect(screen.getByRole("status")).toContainElement(screen.getByText(LINE));
    expect(sessionStorage.getItem(STALE_BUILD_RELOADED_NOTICE_KEY)).toBeNull();
  });

  it("N2: without the stamp nothing shows, and the empty live region is still there", async () => {
    render(<ReloadedAfterUpdateNotice />);

    await new Promise((resolve) => setTimeout(resolve, 5));
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
    expect(screen.queryByText(LINE)).not.toBeInTheDocument();
  });

  it("N3: a new mount after the stamp was consumed shows nothing", async () => {
    stamp();
    const first = render(<ReloadedAfterUpdateNotice />);
    expect(await screen.findByText(LINE)).toBeInTheDocument();
    first.unmount();

    render(<ReloadedAfterUpdateNotice />);
    await new Promise((resolve) => setTimeout(resolve, 5));
    expect(screen.queryByText(LINE)).not.toBeInTheDocument();
  });

  it("N-nav: the line is gone after the next navigation", async () => {
    stamp();
    const { rerender } = render(<ReloadedAfterUpdateNotice />);
    expect(await screen.findByText(LINE)).toBeInTheDocument();

    route.pathname = "/jobb";
    rerender(<ReloadedAfterUpdateNotice />);
    expect(screen.queryByText(LINE)).not.toBeInTheDocument();
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
  });

  it("storage that cannot be read shows nothing and throws nothing", async () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new DOMException("blocked", "SecurityError");
    });
    render(<ReloadedAfterUpdateNotice />);

    await new Promise((resolve) => setTimeout(resolve, 5));
    expect(screen.queryByText(LINE)).not.toBeInTheDocument();
  });
});
