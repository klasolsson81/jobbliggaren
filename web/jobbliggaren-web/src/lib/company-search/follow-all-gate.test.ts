import { describe, it, expect } from "vitest";
import { evaluateFollowAllGate } from "./follow-all-gate";

describe("evaluateFollowAllGate", () => {
  it("is ready when both axes are set and there is no name term", () => {
    expect(evaluateFollowAllGate("", ["62010"], ["0180"])).toEqual({ kind: "ready" });
    expect(evaluateFollowAllGate("   ", ["62010", "62020"], ["0180"])).toEqual({
      kind: "ready",
    });
  });

  it("blocks on a name term even when both code axes are set (silent-drift guard, highest precedence)", () => {
    // A name term has no home in a criterion; adding branch+kommun does not make it saveable.
    expect(evaluateFollowAllGate("Volvo", ["62010"], ["0180"])).toEqual({
      kind: "nameTerm",
    });
  });

  it("treats a name term as present only after trimming", () => {
    expect(evaluateFollowAllGate("  Volvo  ", ["62010"], ["0180"])).toEqual({
      kind: "nameTerm",
    });
    // Whitespace-only is NOT a name term (parity with the URL builder's trim-to-empty).
    expect(evaluateFollowAllGate("\t\n ", ["62010"], ["0180"])).toEqual({ kind: "ready" });
  });

  it("reports 'empty' for a browse-all filter (no axes, no name)", () => {
    expect(evaluateFollowAllGate("", [], [])).toEqual({ kind: "empty" });
  });

  it("reports 'sniMissing' for a kommun-only filter (Domain forbids a kommun-only criterion)", () => {
    expect(evaluateFollowAllGate("", [], ["0180"])).toEqual({ kind: "sniMissing" });
  });

  it("reports 'kommunMissing' for an SNI-only filter (Domain forbids an SNI-only criterion)", () => {
    expect(evaluateFollowAllGate("", ["62010"], [])).toEqual({ kind: "kommunMissing" });
  });

  it("prefers the name reason over a missing axis when a name coexists with a single axis", () => {
    // Name + SNI only: the name is the reason it can't be saved, not the missing kommun.
    expect(evaluateFollowAllGate("Volvo", ["62010"], [])).toEqual({ kind: "nameTerm" });
    // Name + kommun only.
    expect(evaluateFollowAllGate("Volvo", [], ["0180"])).toEqual({ kind: "nameTerm" });
    // Name alone.
    expect(evaluateFollowAllGate("Volvo", [], [])).toEqual({ kind: "nameTerm" });
  });
});
