import { describe, it, expect } from "vitest";
import { ALLOWED_CONTENT_TYPES } from "./original-file-proxy";

/**
 * Pins the BFF allowlist's EXACT membership.
 *
 * This is the condition `security-auditor` attached to her 2026-09-07 signature of the multi-user
 * basis (ADR 0101 / DPIA #659 §12 Disposition-addendum — §11's own Disposition carries the earlier
 * 2026-09-06 signature, which that addendum says is NOT inherited). Her reason, and it is not
 * hygiene: growing this map is lapse-trigger 1, no trigger in §11's list has an automatic detector,
 * and this is the only one with a cheap detector that was not built.
 *
 * §11 keys the severity change on the EVENT, not on the signature: trigger 1 becomes Blocker-class
 * from the moment a non-controller uploads a CV that reaches the rendering path. That has not
 * happened yet.
 *
 * The route suite is not zero cover here, and the difference matters: it asserts 502 for an unknown
 * type using `text/html` specifically (`api/cv/[id]/original/route.test.ts`), so `text/html` as a
 * third entry WOULD fail it — the one entry §12 measured as script-executing. What it does not
 * catch is a third entry that is anything else, because "unknown -> 502" still passes for a
 * genuinely unknown type. That is the gap this file closes.
 *
 * The set is imported from production, never restated here: a pin that rewrites the literal it
 * measures is a tautology and cannot fall (the failure mode measured on PR #1692).
 */
describe("original-file-proxy — the BFF allowlist", () => {
  it("has EXACTLY these two entries, so lapse-trigger 1 cannot fire silently", () => {
    // Entries, not just keys: the value drives the download filename's extension, so a changed
    // mapping is as much a drift as a changed membership. Sorted so the assertion does not pin
    // insertion order, which is not a property anything depends on.
    expect([...ALLOWED_CONTENT_TYPES.entries()].sort()).toEqual(
      [
        ["application/pdf", "pdf"],
        [
          "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
          "docx",
        ],
      ].sort()
    );
  });
});
