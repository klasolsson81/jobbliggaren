import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { UnrecognizedActionError } from "next/dist/client/components/unrecognized-action-error";
import { reloadDocument } from "./reload-document";
import {
  STALE_BUILD_RELOADED_NOTICE_KEY,
  STALE_BUILD_RELOAD_STAMP_KEY,
  STALE_BUILD_RELOAD_WINDOW_MS,
  isStaleBuildActionError,
  mayReloadForStaleBuild,
  reloadIfStaleBuild,
  stampAndReload,
} from "./stale-build-reload";

vi.mock("./reload-document", () => ({ reloadDocument: vi.fn() }));

// The error the router constructs when the server answers an action id it does
// not know (`server-action-reducer.js`, `x-nextjs-action-not-found: 1`): the real
// class, so the `instanceof` predicate sees what production produces.
const staleError = () => new UnrecognizedActionError('Server Action "00a89fc0" was not found on the server.');
const NOW = 1_800_000_000_000;

describe("stale-build reload core (ADR 0148)", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.mocked(reloadDocument).mockClear();
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("H2/H7: only the router's UnrecognizedActionError qualifies — a digest error, a plain error and a string do not", () => {
    expect(isStaleBuildActionError(Object.assign(new Error("boom"), { digest: "d1" }))).toBe(false);
    expect(isStaleBuildActionError(new Error("boom"))).toBe(false);
    expect(isStaleBuildActionError("UnrecognizedActionError")).toBe(false);
    // A look-alike by name, which no src/ path builds — the router constructs the class
    // (`server-action-reducer.js`); the actor would be a second copy of Next's client
    // runtime or a hand-built error. Declared unreachable (§5 Tests): the row asserts only
    // that the predicate degrades safely — refuses — never what production does with it.
    expect(isStaleBuildActionError(Object.assign(new Error("x"), { name: "UnrecognizedActionError" }))).toBe(false);
    expect(isStaleBuildActionError(staleError())).toBe(true);
    expect(mayReloadForStaleBuild(new Error("boom"), NOW)).toBe(false);
    expect(reloadIfStaleBuild(new Error("boom"), NOW)).toBe(false);
    expect(reloadDocument).not.toHaveBeenCalled();
    expect(sessionStorage.length).toBe(0);
  });

  it("H1: a stale error with an empty storage reloads once and writes both stamps as the timestamp", () => {
    const setItem = vi.spyOn(Storage.prototype, "setItem");

    expect(reloadIfStaleBuild(staleError(), NOW)).toBe(true);

    expect(reloadDocument).toHaveBeenCalledTimes(1);
    // N4 (security-auditor m-3): exactly two writes, both constants, both values
    // the timestamp — no url, no action id, no message, no form content.
    expect(setItem.mock.calls).toEqual([
      [STALE_BUILD_RELOAD_STAMP_KEY, String(NOW)],
      [STALE_BUILD_RELOADED_NOTICE_KEY, String(NOW)],
    ]);
  });

  it("H3: a stamp younger than the window refuses — no reload, no write", () => {
    sessionStorage.setItem(STALE_BUILD_RELOAD_STAMP_KEY, String(NOW - (STALE_BUILD_RELOAD_WINDOW_MS - 1000)));
    const setItem = vi.spyOn(Storage.prototype, "setItem");

    expect(mayReloadForStaleBuild(staleError(), NOW)).toBe(false);
    expect(reloadIfStaleBuild(staleError(), NOW)).toBe(false);
    expect(reloadDocument).not.toHaveBeenCalled();
    expect(setItem).not.toHaveBeenCalled();
  });

  it("H4: a stamp older than the window reloads and renews the stamp", () => {
    sessionStorage.setItem(STALE_BUILD_RELOAD_STAMP_KEY, String(NOW - (STALE_BUILD_RELOAD_WINDOW_MS + 1000)));

    expect(reloadIfStaleBuild(staleError(), NOW)).toBe(true);
    expect(reloadDocument).toHaveBeenCalledTimes(1);
    expect(sessionStorage.getItem(STALE_BUILD_RELOAD_STAMP_KEY)).toBe(String(NOW));
  });

  it("H8: a value no writer produces under the stamp key is read as absent (declared unreachable from src/)", () => {
    // The key's only writer is `stampAndReload`, always `String(now)`; a non-numeric value
    // comes from a script outside the app on the origin (devtools, an extension). Declared
    // unreachable (§5 Tests), so the row asserts only that the read side degrades safely —
    // the guard answers as for an absent stamp — never what production then does.
    sessionStorage.setItem(STALE_BUILD_RELOAD_STAMP_KEY, "not-a-timestamp");

    expect(mayReloadForStaleBuild(staleError(), NOW)).toBe(true);
  });

  it("H5: storage that cannot be read refuses (fail-closed) — no reload", () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new DOMException("blocked", "SecurityError");
    });

    expect(mayReloadForStaleBuild(staleError(), NOW)).toBe(false);
    expect(reloadIfStaleBuild(staleError(), NOW)).toBe(false);
    expect(reloadDocument).not.toHaveBeenCalled();
  });

  it("H5b: storage that cannot be written refuses (fail-closed on the write) — the seam is never called", () => {
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new DOMException("quota", "QuotaExceededError");
    });

    expect(stampAndReload(NOW)).toBe(false);
    expect(reloadIfStaleBuild(staleError(), NOW)).toBe(false);
    expect(reloadDocument).not.toHaveBeenCalled();
  });

  it("H-seq: two stale errors in a row within the window — the first reloads, the second refuses", () => {
    expect(reloadIfStaleBuild(staleError(), NOW)).toBe(true);
    expect(reloadIfStaleBuild(staleError(), NOW + 5_000)).toBe(false);
    expect(reloadDocument).toHaveBeenCalledTimes(1);
  });

  it("the predicate runs before any storage access, so a non-stale error never touches storage", () => {
    const getItem = vi.spyOn(Storage.prototype, "getItem");
    expect(mayReloadForStaleBuild(new Error("boom"), NOW)).toBe(false);
    expect(getItem).not.toHaveBeenCalled();
  });
});
