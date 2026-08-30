import { describe, it, expect } from "vitest";
import { renderHook } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import type { ReactNode } from "react";
import svJobads from "../../../messages/sv/jobads.json";
import enJobads from "../../../messages/en/jobads.json";
import { useCodedTaxonomyName } from "./use-coded-taxonomy-name";

/**
 * The hook pairs two catalogue keys: `jobads.enums` for a concept the coded set knows, and
 * `jobads.ui.toolbar.unknownCode` for one it does not. `coded-taxonomy.test.ts` already pins
 * that `codedTaxonomyName` passes an unknown id through to whatever fallback it is handed —
 * what is pinned HERE is which fallback this hook hands it, which is the part #1540 made
 * load-bearing: a register concept the taxonomy snapshot lost now arrives as a `Coded` part
 * and reaches exactly this path.
 *
 * Real catalogues, not stand-ins: a stand-in would assert the test's own composition rather
 * than the shipped one.
 */
function wrapper(messages: Record<string, unknown>, locale: "sv" | "en") {
  return function Wrapper({ children }: { children: ReactNode }) {
    return (
      <NextIntlClientProvider locale={locale} messages={{ jobads: messages }}>
        {children}
      </NextIntlClientProvider>
    );
  };
}

// A municipality-shaped id that is NOT in CODED_TAXONOMY_IDS — the shape a snapshot drop
// produces (#1540).
const LOST_REGISTER_ID = "PVZL_BQT_XtL";

describe("useCodedTaxonomyName", () => {
  it("names a coded concept from the enums catalogue", () => {
    const { result } = renderHook(() => useCodedTaxonomyName(), {
      wrapper: wrapper(svJobads, "sv"),
    });

    expect(result.current("6YE1_gAC_R2G")).toBe("Heltid");
  });

  it("names an id the snapshot could not resolve from the unknown-code copy", () => {
    const { result } = renderHook(() => useCodedTaxonomyName(), {
      wrapper: wrapper(svJobads, "sv"),
    });

    const name = result.current(LOST_REGISTER_ID);

    // The assertion that carries the regression: neither empty nor the bare id. A hook that
    // handed `codedTaxonomyName` an empty fallback would render a blank part inside a label
    // (`recent-search-label.ts` interpolates it), and one that handed the id would put the
    // external system's vocabulary in front of the user (§5).
    expect(name).toBe(`Okänd kod (${LOST_REGISTER_ID})`);
    expect(name).not.toBe(LOST_REGISTER_ID);
  });

  it("names it in the reader's locale, which is the whole point of #1540", () => {
    const { result } = renderHook(() => useCodedTaxonomyName(), {
      wrapper: wrapper(enJobads, "en"),
    });

    expect(result.current(LOST_REGISTER_ID)).toBe(
      `Unknown code (${LOST_REGISTER_ID})`
    );
  });
});
