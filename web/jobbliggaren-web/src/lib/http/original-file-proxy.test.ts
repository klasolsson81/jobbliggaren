import { describe, it, expect } from "vitest";
import { ALLOWED_CONTENT_TYPES } from "./original-file-proxy";

/**
 * Pins the BFF allowlist's EXACT membership.
 *
 * This is the condition `security-auditor` attached to her 2026-09-07 signature of the multi-user
 * basis (ADR 0101 / DPIA #659 §11 Disposition-addendum). Her reason, and it is not hygiene:
 * growing this map is lapse-trigger 1, none of the six triggers has an automatic detector, and
 * this is the only one of them with a cheap detector that was not built. Before the multi-user
 * basis a silent third entry cost self-inflicted XSS; after it, it costs account takeover of a
 * third party.
 *
 * The existing route tests cover pdf, docx and "unknown type -> 502" — and a THIRD entry would
 * fail none of them, because "unknown -> 502" still passes for a genuinely unknown type. That gap
 * is what this file closes.
 *
 * The set is imported from production, never restated here: a pin that rewrites the literal it
 * measures is a tautology and cannot fall (the failure mode measured on PR #1692).
 */
describe("original-file-proxy — the BFF allowlist", () => {
  it("has EXACTLY these two entries, so lapse-trigger 1 cannot fire silently", () => {
    // Entries, not just keys: the value drives the download filename's extension, so a changed
    // mapping is as much a drift as a changed membership. Sorted so the assertion does not pin
    // insertion order, which is not a property anything depends on.
    expect([...ALLOWED_CONTENT_TYPES.entries()].sort()).toEqual([
      ["application/pdf", "pdf"],
      [
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "docx",
      ],
    ]);
  });
});
