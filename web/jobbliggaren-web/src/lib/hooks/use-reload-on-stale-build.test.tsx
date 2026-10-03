import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { UnrecognizedActionError } from "next/dist/client/components/unrecognized-action-error";
import { reloadDocument } from "@/lib/stale-build/reload-document";
import {
  STALE_BUILD_RELOADED_NOTICE_KEY,
  STALE_BUILD_RELOAD_STAMP_KEY,
  STALE_BUILD_RELOAD_WINDOW_MS,
} from "@/lib/stale-build/stale-build-reload";
import { useReloadOnStaleBuild } from "./use-reload-on-stale-build";

vi.mock("@/lib/stale-build/reload-document", () => ({ reloadDocument: vi.fn() }));

// The shape every boundary has after ADR 0148: the hook decides in render, the
// surface renders only when it says false. The error is the router's own class
// (what `server-action-reducer.js` constructs on `x-nextjs-action-not-found: 1`).
function Boundary({ error }: { error: unknown }) {
  const reloading = useReloadOnStaleBuild(error);
  if (reloading) return null;
  return <h1>Sidan kunde inte visas</h1>;
}
const staleError = () => new UnrecognizedActionError('Server Action "00a89fc0" was not found on the server.');
const NOW = 1_800_000_000_000;

describe("useReloadOnStaleBuild (ADR 0148)", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.mocked(reloadDocument).mockClear();
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(NOW);
  });
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it("H1: a stale error renders nothing from the first commit, stamps both keys and reloads once", () => {
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    render(<Boundary error={staleError()} />);

    expect(screen.queryByRole("heading")).not.toBeInTheDocument();
    expect(reloadDocument).toHaveBeenCalledTimes(1);
    expect(setItem.mock.calls).toEqual([
      [STALE_BUILD_RELOAD_STAMP_KEY, String(NOW)],
      [STALE_BUILD_RELOADED_NOTICE_KEY, String(NOW)],
    ]);
  });

  it("H2: a plain error renders the surface and touches neither the seam nor storage", () => {
    render(<Boundary error={Object.assign(new Error("boom"), { digest: "d1" })} />);

    expect(screen.getByRole("heading", { name: "Sidan kunde inte visas" })).toBeInTheDocument();
    expect(reloadDocument).not.toHaveBeenCalled();
    expect(sessionStorage.length).toBe(0);
  });

  it("H3: a stale error within the window renders the surface — no reload", () => {
    sessionStorage.setItem(STALE_BUILD_RELOAD_STAMP_KEY, String(NOW - 1_000));
    render(<Boundary error={staleError()} />);

    expect(screen.getByRole("heading", { name: "Sidan kunde inte visas" })).toBeInTheDocument();
    expect(reloadDocument).not.toHaveBeenCalled();
  });

  it("H4: a stale error past the window reloads at the second time point", () => {
    sessionStorage.setItem(STALE_BUILD_RELOAD_STAMP_KEY, String(NOW));
    vi.setSystemTime(NOW + STALE_BUILD_RELOAD_WINDOW_MS + 1);
    render(<Boundary error={staleError()} />);

    expect(screen.queryByRole("heading")).not.toBeInTheDocument();
    expect(reloadDocument).toHaveBeenCalledTimes(1);
    expect(sessionStorage.getItem(STALE_BUILD_RELOAD_STAMP_KEY)).toBe(String(NOW + STALE_BUILD_RELOAD_WINDOW_MS + 1));
  });

  it("H6: the decision is locked per mount — a rerender with the same error after the stamp landed keeps reloading", () => {
    const { rerender } = render(<Boundary error={staleError()} />);
    expect(reloadDocument).toHaveBeenCalledTimes(1);

    // The effect's own stamp is now younger than the window; a fresh decision
    // would refuse and flash the surface. The locked one does not.
    rerender(<Boundary error={staleError()} />);
    expect(screen.queryByRole("heading")).not.toBeInTheDocument();
    expect(reloadDocument).toHaveBeenCalledTimes(1);
  });

  it("H5b: a stamp that cannot be written flips the boundary to its surface — the seam is never called", async () => {
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new DOMException("quota", "QuotaExceededError");
    });
    render(<Boundary error={staleError()} />);

    // The first commit renders nothing (the decision said reload); the failed
    // write flips the state on the next tick (the effect's scheduled setState).
    expect(screen.queryByRole("heading")).not.toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "Sidan kunde inte visas" })).toBeInTheDocument();
    expect(reloadDocument).not.toHaveBeenCalled();
  });

  it("H5: storage that cannot be read renders the surface (fail-closed)", () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new DOMException("blocked", "SecurityError");
    });
    render(<Boundary error={staleError()} />);

    expect(screen.getByRole("heading", { name: "Sidan kunde inte visas" })).toBeInTheDocument();
    expect(reloadDocument).not.toHaveBeenCalled();
  });

  it("H-seq: a second mount within the window refuses after the first reloaded", () => {
    const first = render(<Boundary error={staleError()} />);
    expect(reloadDocument).toHaveBeenCalledTimes(1);
    first.unmount();

    vi.setSystemTime(NOW + 5_000);
    render(<Boundary error={staleError()} />);
    expect(screen.getByRole("heading", { name: "Sidan kunde inte visas" })).toBeInTheDocument();
    expect(reloadDocument).toHaveBeenCalledTimes(1);
  });
});
