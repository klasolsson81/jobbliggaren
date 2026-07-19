import { describe, it, expect, vi, beforeEach } from "vitest";

/**
 * Bevakning F4b (#803) — `setWatchFilterAction`.
 *
 * The action is the last FE surface before the wire, so it owns three things worth pinning:
 * the two geo axes reach the fetcher UNCROSSED and UNEXPANDED, an all-empty selection is a legal
 * CLEAR (never a validation error), and only a successful write revalidates.
 */

// next/cache revalidatePath is a no-op outside a Next request scope — observe the call, don't run it.
const { revalidatePathMock } = vi.hoisted(() => ({ revalidatePathMock: vi.fn() }));
vi.mock("next/cache", () => ({ revalidatePath: revalidatePathMock }));

// The BFF fetcher is server-only (Bearer + backend URL); drive every ApiResult branch through a stub.
const { setWatchFilterMock, followCompanyMock, unfollowCompanyMock } = vi.hoisted(() => ({
  setWatchFilterMock: vi.fn(),
  followCompanyMock: vi.fn(),
  unfollowCompanyMock: vi.fn(),
}));
vi.mock("@/lib/api/company-follows", () => ({
  followCompany: followCompanyMock,
  followCompanyFromJobAd: vi.fn(),
  unfollowCompany: unfollowCompanyMock,
  setWatchFilter: setWatchFilterMock,
}));

// Resolve the REAL Swedish catalog rather than echoing keys back: the error copy the user reads is
// part of the contract, and a missing key must fail the test rather than silently render a key name.
vi.mock("next-intl/server", async () => {
  const messages = (await import("../../../messages/sv")).default;
  return {
    getTranslations: async (namespace: string) => {
      const dict = namespace
        .split(".")
        .reduce<unknown>(
          (node, part) =>
            typeof node === "object" && node !== null
              ? (node as Record<string, unknown>)[part]
              : undefined,
          messages
        );
      return (key: string): string => {
        const value =
          typeof dict === "object" && dict !== null
            ? (dict as Record<string, unknown>)[key]
            : undefined;
        if (typeof value !== "string") {
          throw new Error(`i18n-nyckel saknas i messages/sv: ${namespace}.${key}`);
        }
        return value;
      };
    },
  };
});

import {
  setWatchFilterAction,
  followCompanyAction,
  unfollowCompanyAction,
  type SetWatchFilterInput,
} from "./company-follows";

const WATCH_ID = "11111111-1111-1111-1111-111111111111";
const ORG_NR = "5592804784";

const SET_FAILED = "Filtret kunde inte sparas. Försök igen.";
const NOT_LOGGED_IN = "Du är inte inloggad.";

beforeEach(() => {
  revalidatePathMock.mockReset();
  setWatchFilterMock.mockReset();
  setWatchFilterMock.mockResolvedValue({ kind: "ok", data: undefined });
  followCompanyMock.mockReset();
  unfollowCompanyMock.mockReset();
});

describe("setWatchFilterAction — the happy path", () => {
  it("ok → success + revalidatePath('/foretag') (the only surface that renders the filter)", async () => {
    const result = await setWatchFilterAction(WATCH_ID, {
      municipalities: ["gbg_kn"],
      regions: ["skane_lan"],
      onlyMatched: true,
    });

    expect(result).toEqual({ success: true });
    expect(revalidatePathMock).toHaveBeenCalledExactlyOnceWith("/foretag");
  });

  it("passes BOTH axes to the fetcher unexpanded and uncrossed", async () => {
    // Regression pin: if someone expanded a whole-län pick into the län's kommuner, or swapped the
    // axes, the stored filter would reference ids in the wrong JobTech namespace and match NOTHING —
    // a filter that silently suppresses every notification, invisible to the user.
    await setWatchFilterAction(WATCH_ID, {
      municipalities: ["gbg_kn"],
      regions: ["skane_lan"],
      onlyMatched: false,
    });

    expect(setWatchFilterMock).toHaveBeenCalledExactlyOnceWith(WATCH_ID, {
      municipalities: ["gbg_kn"],
      regions: ["skane_lan"],
      onlyMatched: false,
    });
  });

  it("an all-empty selection is a legal CLEAR — it reaches the fetcher, it is never a validation error", async () => {
    // "I unchecked everything and saved" is the natural way to stop filtering. The backend maps the
    // empty selection to the canonical NULL; the UI must never turn it into an error the user cannot
    // get out of (the old filter would stay active with no way to remove it).
    const result = await setWatchFilterAction(WATCH_ID, {
      municipalities: [],
      regions: [],
      onlyMatched: false,
    });

    expect(result).toEqual({ success: true });
    expect(setWatchFilterMock).toHaveBeenCalledExactlyOnceWith(WATCH_ID, {
      municipalities: [],
      regions: [],
      onlyMatched: false,
    });
    expect(revalidatePathMock).toHaveBeenCalledWith("/foretag");
  });
});

describe("setWatchFilterAction — failures never revalidate", () => {
  it.each([
    ["notFound", SET_FAILED],
    ["forbidden", SET_FAILED],
    ["rateLimited", SET_FAILED],
    ["error", SET_FAILED],
  ] as const)("%s → Swedish error copy, no revalidate", async (kind, expected) => {
    setWatchFilterMock.mockResolvedValue({ kind });

    const result = await setWatchFilterAction(WATCH_ID, {
      municipalities: ["gbg_kn"],
      regions: [],
      onlyMatched: false,
    });

    expect(result).toEqual({ success: false, error: expected });
    // No revalidate on failure: the dialog stays open holding the user's draft, and a re-render of
    // the RSC tree would tear it down mid-flow (#141).
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });

  it("unauthorized → the not-signed-in copy (distinct from the generic save failure)", async () => {
    setWatchFilterMock.mockResolvedValue({ kind: "unauthorized" });

    const result = await setWatchFilterAction(WATCH_ID, {
      municipalities: [],
      regions: [],
      onlyMatched: true,
    });

    expect(result).toEqual({ success: false, error: NOT_LOGGED_IN });
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });
});

describe("setWatchFilterAction — the zod guard", () => {
  it("a malformed input is rejected BEFORE the wire (no backend round-trip)", async () => {
    // Structural defense-in-depth: the domain is the authority, but a crossed/garbled payload must
    // fail here rather than be stored as a filter that matches nothing.
    const malformed = {
      municipalities: "gbg_kn", // a bare string, not a list
      regions: ["skane_lan"],
      onlyMatched: true,
    } as unknown as SetWatchFilterInput;

    const result = await setWatchFilterAction(WATCH_ID, malformed);

    expect(result).toEqual({ success: false, error: SET_FAILED });
    expect(setWatchFilterMock).not.toHaveBeenCalled();
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });

  it("a non-boolean onlyMatched is rejected (never coerced to a truthy filter the user did not set)", async () => {
    const malformed = {
      municipalities: [],
      regions: [],
      onlyMatched: "ja",
    } as unknown as SetWatchFilterInput;

    const result = await setWatchFilterAction(WATCH_ID, malformed);

    expect(result).toEqual({ success: false, error: SET_FAILED });
    expect(setWatchFilterMock).not.toHaveBeenCalled();
  });
});

describe("followCompanyAction / unfollowCompanyAction (#560 PR-C) — /foretag/sok revalidation", () => {
  it("followCompanyAction ok → revalidates /jobb, /foretag AND /foretag/sok", async () => {
    followCompanyMock.mockResolvedValue({ kind: "ok", data: { companyWatchId: "cw-1" } });

    const result = await followCompanyAction(ORG_NR);

    expect(result).toEqual({ success: true, companyWatchId: "cw-1" });
    expect(followCompanyMock).toHaveBeenCalledExactlyOnceWith(ORG_NR);
    // The search results carry the follow overlay, so the follow surface must revalidate too (#560 PR-C).
    expect(revalidatePathMock).toHaveBeenCalledWith("/foretag/sok");
    expect(revalidatePathMock).toHaveBeenCalledWith("/foretag");
    expect(revalidatePathMock).toHaveBeenCalledWith("/jobb");
  });

  it("unfollowCompanyAction ok → revalidates /foretag/sok", async () => {
    unfollowCompanyMock.mockResolvedValue({ kind: "ok", data: undefined });

    const result = await unfollowCompanyAction(WATCH_ID);

    expect(result).toEqual({ success: true });
    expect(unfollowCompanyMock).toHaveBeenCalledExactlyOnceWith(WATCH_ID);
    expect(revalidatePathMock).toHaveBeenCalledWith("/foretag/sok");
  });

  it("followCompanyAction failure → no revalidate", async () => {
    followCompanyMock.mockResolvedValue({ kind: "error" });

    const result = await followCompanyAction(ORG_NR);

    expect(result.success).toBe(false);
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });
});
