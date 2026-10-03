import { readdirSync, statSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import type { ComponentType } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { ErrorBoundary } from "next/dist/client/components/error-boundary";
import { UnrecognizedActionError } from "next/dist/client/components/unrecognized-action-error";
import type { ErrorInfo } from "next/error";
import {
  AppRouterContext,
  type AppRouterInstance,
} from "next/dist/shared/lib/app-router-context.shared-runtime";
import { reloadDocument } from "@/lib/stale-build/reload-document";
import {
  STALE_BUILD_RELOADED_NOTICE_KEY,
  STALE_BUILD_RELOAD_STAMP_KEY,
} from "@/lib/stale-build/stale-build-reload";
import AdminError from "./(admin)/error";
import AppError from "./(app)/error";
import AuthError from "./(auth)/error";
import GuestError from "./(guest)/gast/error";
import MarketingError from "./(marketing)/error";
import MarketingInnerError from "./(marketing-inner)/error";
import GlobalError from "./global-error";

vi.mock("@/lib/stale-build/reload-document", () => ({ reloadDocument: vi.fn() }));

/**
 * Every boundary reloads the document once on a stale-build action error, and
 * renders nothing while it does (ADR 0148 D6); every other error, and a stale
 * error within the guard's window, renders the surface (#1948).
 *
 * The error is the router's own class — what `server-action-reducer.js`
 * constructs when the server answers `x-nextjs-action-not-found: 1` — so the
 * `instanceof` predicate sees what production produces. The seam is mocked;
 * the stamps are read from the real `sessionStorage`.
 *
 * The table is static and its count is cross-checked against a filesystem
 * walk (the `error-boundaries.contract.test.tsx` form): a boundary added on
 * disk fails the count row until it is in the table.
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

const table: { path: string; Boundary: ComponentType<ErrorInfo> }[] = [
  { path: "(admin)/error.tsx", Boundary: AdminError },
  { path: "(app)/error.tsx", Boundary: AppError },
  { path: "(auth)/error.tsx", Boundary: AuthError },
  { path: "(guest)/gast/error.tsx", Boundary: GuestError },
  { path: "(marketing)/error.tsx", Boundary: MarketingError },
  { path: "(marketing-inner)/error.tsx", Boundary: MarketingInnerError },
  { path: "global-error.tsx", Boundary: GlobalError },
];

const staleError = () => new UnrecognizedActionError('Server Action "00a89fc0" was not found on the server.');
const plainError = () => Object.assign(new Error("boom-internal-detail"), { digest: "digest-123" });
const inert = { retry: () => {}, reset: () => {} };

describe("every error boundary reloads once on a stale-build action error (ADR 0148)", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.mocked(reloadDocument).mockClear();
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("the table covers every error.tsx and global-error.tsx under src/app", () => {
    const onDisk = errorBoundaryFiles(APP_ROOT).filter((f) => statSync(f).isFile());
    expect(onDisk.length, "the filesystem walk found no boundaries — the rows would be vacuous").toBeGreaterThanOrEqual(7);
    expect(table.length).toBe(onDisk.length);
  });

  it.each(table)("$path: a plain error renders the surface and touches neither the seam nor storage", ({ Boundary }) => {
    render(<Boundary error={plainError()} {...inert} />);

    expect(screen.getByRole("heading", { name: "Sidan kunde inte visas" })).toBeInTheDocument();
    expect(reloadDocument).not.toHaveBeenCalled();
    expect(sessionStorage.length).toBe(0);
  });

  it.each(table)("$path: a stale-build action error renders nothing, stamps both keys and reloads once", ({ path, Boundary }) => {
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    render(<Boundary error={staleError()} {...inert} />);

    expect(screen.queryByRole("heading")).not.toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    expect(reloadDocument).toHaveBeenCalledTimes(1);
    expect(setItem.mock.calls.map(([key]) => key)).toEqual([STALE_BUILD_RELOAD_STAMP_KEY, STALE_BUILD_RELOADED_NOTICE_KEY]);
    if (path === "global-error.tsx") {
      // The document shell stays: the site's own title, not the error title
      // (design-reviewer Major 2(b)). React hoists <title> into document.head.
      expect(document.title).toBe("Jobbliggaren");
    }
  });

  it.each(table)("$path: a stale-build action error within the guard's window renders the surface — no reload", ({ Boundary }) => {
    sessionStorage.setItem(STALE_BUILD_RELOAD_STAMP_KEY, String(Date.now() - 1_000));
    render(<Boundary error={staleError()} {...inert} />);

    expect(screen.getByRole("heading", { name: "Sidan kunde inte visas" })).toBeInTheDocument();
    expect(reloadDocument).not.toHaveBeenCalled();
  });

  it("C2: through Next's own ErrorBoundary, a child that throws the router's error reaches the boundary under the name `error`", () => {
    // The actor is ErrorBoundaryHandler (`error-boundary.js`): it renders the
    // error component with `{ error, reset, retry }`, and the hook must read
    // the thrown value under that name.
    const router = { refresh: vi.fn() } as unknown as AppRouterInstance;
    function Child(): never {
      throw staleError();
    }
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
    try {
      render(
        <AppRouterContext.Provider value={router}>
          <ErrorBoundary errorComponent={AppError}>
            <Child />
          </ErrorBoundary>
        </AppRouterContext.Provider>,
      );
    } finally {
      consoleError.mockRestore();
    }

    expect(screen.queryByRole("heading")).not.toBeInTheDocument();
    expect(reloadDocument).toHaveBeenCalledTimes(1);
  });
});
