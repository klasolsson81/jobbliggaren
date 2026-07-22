import { describe, it, expect, vi, beforeEach } from "vitest";

/**
 * #560 PR-3 / S1 #996 — the criteria (smart-bevakning) server actions.
 *
 * The behaviour pinned here is the revalidate target: after the /foretag IA split (#996) the criteria
 * list lives on `/foretag/smarta-bevakningar`, so a successful create/update/delete must revalidate
 * THAT surface (never the old `/foretag` hub), and a failure must never revalidate.
 */

// next/cache revalidatePath is a no-op outside a Next request scope — observe the call, don't run it.
const { revalidatePathMock } = vi.hoisted(() => ({ revalidatePathMock: vi.fn() }));
vi.mock("next/cache", () => ({ revalidatePath: revalidatePathMock }));

// The BFF fetchers are server-only; drive each ApiResult branch through a stub.
const { createCriterionMock, updateCriterionMock, deleteCriterionMock } = vi.hoisted(() => ({
  createCriterionMock: vi.fn(),
  updateCriterionMock: vi.fn(),
  deleteCriterionMock: vi.fn(),
}));
vi.mock("@/lib/api/company-criteria", () => ({
  createCriterion: createCriterionMock,
  updateCriterion: updateCriterionMock,
  deleteCriterion: deleteCriterionMock,
}));

// Resolve the REAL Swedish catalog (parity with company-follows.test.ts): a missing key must fail the
// test rather than silently render a key name.
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
          messages,
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
  createCriterionAction,
  updateCriterionAction,
  deleteCriterionAction,
} from "./company-criteria";

const CRITERION_ID = "22222222-2222-2222-2222-222222222222";
const SMARTA = "/foretag/smarta-bevakningar";
const VALID_INPUT = {
  sniCodes: ["62010"],
  municipalityCodes: ["0180"],
  label: "Techbolag i Stockholm",
};

beforeEach(() => {
  revalidatePathMock.mockReset();
  createCriterionMock.mockReset();
  updateCriterionMock.mockReset();
  deleteCriterionMock.mockReset();
});

describe("criteria actions — revalidate the Smarta bevakningar surface after the #996 split", () => {
  it("createCriterionAction ok → success + revalidatePath('/foretag/smarta-bevakningar')", async () => {
    createCriterionMock.mockResolvedValue({ kind: "ok", data: undefined });

    const result = await createCriterionAction(VALID_INPUT);

    expect(result).toEqual({ success: true });
    expect(revalidatePathMock).toHaveBeenCalledExactlyOnceWith(SMARTA);
  });

  it("updateCriterionAction ok → success + revalidatePath('/foretag/smarta-bevakningar')", async () => {
    updateCriterionMock.mockResolvedValue({ kind: "ok", data: undefined });

    const result = await updateCriterionAction(CRITERION_ID, VALID_INPUT);

    expect(result).toEqual({ success: true });
    expect(revalidatePathMock).toHaveBeenCalledExactlyOnceWith(SMARTA);
  });

  it("deleteCriterionAction ok → success + revalidatePath('/foretag/smarta-bevakningar')", async () => {
    deleteCriterionMock.mockResolvedValue({ kind: "ok" });

    const result = await deleteCriterionAction(CRITERION_ID);

    expect(result).toEqual({ success: true });
    expect(revalidatePathMock).toHaveBeenCalledExactlyOnceWith(SMARTA);
  });

  it("deleteCriterionAction notFound → success-equivalent (already gone), still revalidates", async () => {
    deleteCriterionMock.mockResolvedValue({ kind: "notFound" });

    const result = await deleteCriterionAction(CRITERION_ID);

    expect(result).toEqual({ success: true });
    expect(revalidatePathMock).toHaveBeenCalledExactlyOnceWith(SMARTA);
  });
});

describe("criteria actions — failures never revalidate", () => {
  it("createCriterionAction error → failure copy, no revalidate", async () => {
    createCriterionMock.mockResolvedValue({ kind: "error" });

    const result = await createCriterionAction(VALID_INPUT);

    expect(result.success).toBe(false);
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });

  it("a malformed input is rejected before the wire (no backend round-trip, no revalidate)", async () => {
    // The picker requires ≥1 industry AND ≥1 municipality; an empty selection is a structural reject.
    const result = await createCriterionAction({
      sniCodes: [],
      municipalityCodes: [],
      label: "",
    });

    expect(result.success).toBe(false);
    expect(createCriterionMock).not.toHaveBeenCalled();
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });
});
