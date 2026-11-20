import { describe, it, expect, vi, beforeEach } from "vitest";

// ADR 0188 (#2265): the quick-link actions forward the user's choice and turn every write result into a sentence the
// user can act on. A Server Action is a public endpoint, so an argument of the wrong type is refused before any write.
// The translator mock returns `namespace.key`.

const { juniorApiMock, searchApiMock, revalidatePathMock } = vi.hoisted(() => ({
  juniorApiMock: vi.fn(),
  searchApiMock: vi.fn(),
  revalidatePathMock: vi.fn(),
}));

vi.mock("next/cache", () => ({ revalidatePath: revalidatePathMock }));
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => (key: string) => `${namespace}.${key}`,
}));
vi.mock("@/lib/api/quick-links", () => ({
  setJuniorQuickLink: juniorApiMock,
  setRecentSearchQuickLink: searchApiMock,
}));

import { setJuniorQuickLinkAction, setRecentSearchQuickLinkAction } from "./quick-links";

const ID = "11111111-1111-1111-1111-111111111111";

beforeEach(() => {
  vi.clearAllMocks();
});

describe("setJuniorQuickLinkAction", () => {
  it("forwards the choice and revalidates every page that shows the links", async () => {
    juniorApiMock.mockResolvedValue({ kind: "ok" });

    expect(await setJuniorQuickLinkAction(false)).toEqual({ success: true });

    expect(juniorApiMock).toHaveBeenCalledWith(false);
    expect(revalidatePathMock.mock.calls.map(([path]) => path)).toEqual([
      "/jobb",
      "/sokningar",
      "/mina-sidor/snabblankar",
    ]);
  });

  it("refuses a choice that is no boolean without writing", async () => {
    expect(await setJuniorQuickLinkAction("false" as unknown as boolean)).toEqual({
      success: false,
      error: "jobads.quickLinks.errors.failed",
    });
    expect(juniorApiMock).not.toHaveBeenCalled();
  });
});

describe("setRecentSearchQuickLinkAction", () => {
  it("forwards the id and the direction", async () => {
    searchApiMock.mockResolvedValue({ kind: "ok" });

    expect(await setRecentSearchQuickLinkAction(ID, true)).toEqual({ success: true });

    expect(searchApiMock).toHaveBeenCalledWith(ID, true);
  });

  it("refuses arguments of the wrong type without writing", async () => {
    await setRecentSearchQuickLinkAction(42 as unknown as string, true);
    await setRecentSearchQuickLinkAction(ID, "true" as unknown as boolean);

    expect(searchApiMock).not.toHaveBeenCalled();
  });

  it.each([
    [{ kind: "full" }, "jobads.quickLinks.errors.full"],
    [{ kind: "conflict" }, "jobads.quickLinks.errors.conflict"],
    [{ kind: "notFound" }, "jobads.quickLinks.errors.notFound"],
  ])("says what happened on %o, and revalidates the pages that show a state no longer true", async (result, message) => {
    searchApiMock.mockResolvedValue(result);

    expect(await setRecentSearchQuickLinkAction(ID, true)).toEqual({ success: false, error: message });
    expect(revalidatePathMock.mock.calls.map(([path]) => path)).toEqual([
      "/jobb",
      "/sokningar",
      "/mina-sidor/snabblankar",
    ]);
  });

  it.each([
    [{ kind: "unauthorized" }, "jobads.quickLinks.errors.notLoggedIn"],
    [{ kind: "rateLimited", retryAfterSeconds: 9 }, "jobads.quickLinks.errors.rateLimited"],
    [{ kind: "error" }, "jobads.quickLinks.errors.failed"],
  ])("says what happened on %o, and revalidates nothing", async (result, message) => {
    searchApiMock.mockResolvedValue(result);

    expect(await setRecentSearchQuickLinkAction(ID, true)).toEqual({ success: false, error: message });
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });

  it("counts a removal as done when the search is already gone, since its link went with it", async () => {
    searchApiMock.mockResolvedValue({ kind: "notFound" });

    expect(await setRecentSearchQuickLinkAction(ID, false)).toEqual({ success: true });
    expect(revalidatePathMock).toHaveBeenCalledWith("/jobb");
  });
});
