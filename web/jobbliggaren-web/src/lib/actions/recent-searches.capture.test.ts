import { describe, it, expect, vi, beforeEach } from "vitest";
import type { JobbUrlState } from "@/lib/job-ads/search-params";

// #2285 (ADR 0060 Amendment 2026-11-22) — the Server Action that stores a search. A Server Action is a public
// endpoint, so only a plain object of the right shape reaches the backend (security-auditor, #2285 plan read), and only
// a search the backend stored revalidates the pages that show recent searches.

const { captureApiMock, revalidatePathMock } = vi.hoisted(() => ({
  captureApiMock: vi.fn(),
  revalidatePathMock: vi.fn(),
}));

vi.mock("next/cache", () => ({ revalidatePath: revalidatePathMock }));
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => (key: string) => `${namespace}.${key}`,
}));
vi.mock("@/lib/api/recent-searches", () => ({
  captureRecentSearch: captureApiMock,
  deleteRecentSearch: vi.fn(),
}));

import { captureRecentSearchAction } from "./recent-searches";

const search = (overrides: Partial<JobbUrlState> = {}): JobbUrlState => ({
  q: "backend",
  occupationGroup: ["MVqp_eS8_kDZ"],
  occupationName: [],
  region: [],
  municipality: [],
  remote: false,
  junior: false,
  employmentType: [],
  worktimeExtent: [],
  matchGrades: ["Strong"],
  employer: [],
  sortBy: "PublishedAtDesc",
  ...overrides,
});

beforeEach(() => {
  vi.clearAllMocks();
});

describe("captureRecentSearchAction", () => {
  it("stores the search and revalidates the two pages that show recent searches", async () => {
    captureApiMock.mockResolvedValue({ kind: "ok", data: { stored: true } });

    expect(await captureRecentSearchAction(search())).toEqual({ stored: true });

    expect(captureApiMock).toHaveBeenCalledTimes(1);
    expect(captureApiMock.mock.calls[0]?.[0]).toMatchObject({ q: "backend", occupationGroup: ["MVqp_eS8_kDZ"] });
    expect(revalidatePathMock.mock.calls.map(([path]) => path)).toEqual(["/jobb", "/sokningar"]);
  });

  it("passes on the search's axes only, not the view state beside them", async () => {
    captureApiMock.mockResolvedValue({ kind: "ok", data: { stored: true } });

    await captureRecentSearchAction({ ...search(), matchningOff: true, hideApplied: true, userId: "someone" });

    const sent = captureApiMock.mock.calls[0]?.[0] as Record<string, unknown>;
    expect(sent).not.toHaveProperty("matchGrades");
    expect(sent).not.toHaveProperty("hideApplied");
    expect(sent).not.toHaveProperty("userId");
  });

  it("answers not stored and revalidates nothing when the backend did not store it", async () => {
    captureApiMock.mockResolvedValue({ kind: "ok", data: { stored: false } });

    expect(await captureRecentSearchAction(search())).toEqual({ stored: false });
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });

  it.each([
    ["a rate limit", { kind: "rateLimited", retryAfterSeconds: 10 }],
    ["an expired session", { kind: "unauthorized" }],
    ["a technical error", { kind: "error" }],
  ])("answers not stored after %s", async (_, result) => {
    captureApiMock.mockResolvedValue(result);

    expect(await captureRecentSearchAction(search())).toEqual({ stored: false });
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });

  it.each([
    ["FormData, which a form submitted without JavaScript would send", (() => {
      const form = new FormData();
      form.set("q", "backend");
      return form;
    })()],
    ["null", null],
    ["a string", "q=backend"],
    ["an array", [search()]],
    ["an object of another class", new (class Search { q = "backend"; })()],
    ["an object missing an axis", { q: "backend", occupationGroup: [] }],
    ["a q over the limit", search({ q: "x".repeat(101) })],
    ["a sort outside the enum", { ...search(), sortBy: "Random" }],
  ])("refuses %s without reaching the backend", async (_, input) => {
    expect(await captureRecentSearchAction(input)).toEqual({ stored: false });
    expect(captureApiMock).not.toHaveBeenCalled();
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });
});
