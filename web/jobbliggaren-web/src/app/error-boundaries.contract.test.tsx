import { readdirSync, statSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import type { ComponentType } from "react";
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ErrorBoundary } from "next/dist/client/components/error-boundary";
import type { ErrorInfo } from "next/error";
import {
  AppRouterContext,
  type AppRouterInstance,
} from "next/dist/shared/lib/app-router-context.shared-runtime";
import AdminError from "./(admin)/error";
import AppError from "./(app)/error";
import AuthError from "./(auth)/error";
import GuestError from "./(guest)/gast/error";
import MarketingError from "./(marketing)/error";
import MarketingInnerError from "./(marketing-inner)/error";
import GlobalError from "./global-error";

/**
 * Contract pin for every runtime error boundary (#1949): the props a boundary
 * reads are the props Next actually passes.
 *
 * The defect it closes: all seven boundaries read `unstable_retry`, the name
 * Next 16.2 introduced, while Next 16.3 renders the error component with
 * `{ error, reset, retry }` (`ErrorBoundaryHandler.render`,
 * `next/dist/client/components/error-boundary.js`). "Försök igen" then threw
 * a TypeError inside its click handler and did nothing, and every co-located
 * test stayed green because each injected the prop by hand — a premise
 * production no longer produced (AGENTS.md §5 `Tests:`).
 *
 * So the premise here is production's own: each boundary is mounted as the
 * `errorComponent` of Next's real `ErrorBoundary`, under a child that throws,
 * and the click goes through whatever props Next passed. The actor that
 * passes them is `ErrorBoundaryHandler`; the router it reaches on retry is the
 * `AppRouterContext` value, stubbed here so the call can be counted.
 *
 * The table is static, so the count row below compares it with a filesystem
 * walk, the
 * `route-boundaries.test.ts` idea: a boundary added on disk fails that row
 * until it is in the table, and a table row with no file behind it fails it
 * too.
 */

const APP_ROOT = dirname(fileURLToPath(import.meta.url));

function errorBoundaryFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const child = resolve(dir, entry.name);
    if (entry.isDirectory()) errorBoundaryFiles(child, acc);
    else if (entry.name === "error.tsx" || entry.name === "global-error.tsx") acc.push(child);
  }
  return acc;
}

// A static table, cross-checked against the filesystem walk below: a boundary
// added on disk and not here fails the count row, so the list cannot silently
// shrink the pin.
const table: { path: string; Boundary: ComponentType<ErrorInfo> }[] = [
  { path: "(admin)/error.tsx", Boundary: AdminError },
  { path: "(app)/error.tsx", Boundary: AppError },
  { path: "(auth)/error.tsx", Boundary: AuthError },
  { path: "(guest)/gast/error.tsx", Boundary: GuestError },
  { path: "(marketing)/error.tsx", Boundary: MarketingError },
  { path: "(marketing-inner)/error.tsx", Boundary: MarketingInnerError },
  { path: "global-error.tsx", Boundary: GlobalError },
];

function stubRouter(): AppRouterInstance {
  return {
    back: vi.fn(),
    forward: vi.fn(),
    refresh: vi.fn(),
    hmrRefresh: vi.fn(),
    push: vi.fn(),
    replace: vi.fn(),
    prefetch: vi.fn(),
  } as unknown as AppRouterInstance;
}

describe("error boundaries read the props Next passes (#1949)", () => {
  it("the table covers every error.tsx and global-error.tsx under src/app", () => {
    const onDisk = errorBoundaryFiles(APP_ROOT).filter((f) => statSync(f).isFile());
    expect(onDisk.length, "the filesystem walk found no boundaries — the pin would be vacuous").toBeGreaterThanOrEqual(7);
    expect(table.length).toBe(onDisk.length);
  });

  it.each(table)("$path: 'Försök igen' retries through Next's ErrorBoundary", async ({ Boundary }) => {
    // The child throws until the test flips the switch right before the click.
    // A throw-once child would never reach the boundary: React replays a render
    // that threw once more before committing to the boundary, and the replay
    // would already succeed.
    const control = { shouldThrow: true };
    // The recovered child carries the page landmark every group's shell or
    // page owns (`<main id="main" tabIndex={-1}>`); a successful retry hands
    // focus to it. It sits inside the recovered tree rather than around the
    // boundary because global-error replaces the whole document, and <html>
    // nested under a <main> is not a tree React will mount.
    function Child() {
      if (control.shouldThrow) throw new Error("transient-render-failure");
      return (
        <main id="main" tabIndex={-1}>
          <p>recovered-child-content</p>
        </main>
      );
    }
    const router = stubRouter();
    const uncaught: unknown[] = [];
    const onError = (event: ErrorEvent) => {
      uncaught.push(event.error ?? event.message);
      event.preventDefault();
    };
    window.addEventListener("error", onError);
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
    // Only the animation frame is faked: the focus hand-off after a successful
    // retry runs on the next frame (useFocusMainOnUnmount), and userEvent's own
    // timers stay real.
    vi.useFakeTimers({ toFake: ["requestAnimationFrame"] });
    try {
      render(
        <AppRouterContext.Provider value={router}>
          <ErrorBoundary errorComponent={Boundary}>
            <Child />
          </ErrorBoundary>
        </AppRouterContext.Provider>,
      );

      expect(screen.getByRole("heading", { name: "Sidan kunde inte visas" })).toBeInTheDocument();

      control.shouldThrow = false;
      await userEvent.setup().click(screen.getByRole("button", { name: "Försök igen" }));

      expect(uncaught, "the click handler threw — the boundary reads a prop Next does not pass").toEqual([]);
      expect(router.refresh, "retry re-fetches the segment; reset alone would replay the failed payload").toHaveBeenCalledTimes(1);
      expect(await screen.findByText("recovered-child-content")).toBeInTheDocument();
      expect(screen.queryByRole("heading", { name: "Sidan kunde inte visas" })).not.toBeInTheDocument();

      // The surface took focus on mount and is gone: without the hand-off a
      // keyboard user is left on <body> (design-reviewer Major 4).
      vi.advanceTimersToNextFrame();
      expect(document.activeElement).toBe(document.getElementById("main"));
    } finally {
      vi.useRealTimers();
      window.removeEventListener("error", onError);
      consoleError.mockRestore();
    }
  });
});
