import { describe, it, expect } from "vitest";
import { toggleLeaf, toggleGroup, groupTriState } from "./criterion-selection";

// Fixtur: en avdelning med två huvudgrupper, huvudgrupp 62 = {62010, 62020}, huvudgrupp 63 = {63110}.
const DIV62 = ["62010", "62020"];
const DIV63 = ["63110"];
const SECTION_J = [...DIV62, ...DIV63]; // hela avdelningen J

const set = (...codes: string[]) => new Set(codes);
const sorted = (s: ReadonlySet<string>) => [...s].sort();

describe("toggleLeaf — enskild löv-kod", () => {
  it("lägger till en frånvarande kod", () => {
    expect(sorted(toggleLeaf(set(), "62010"))).toEqual(["62010"]);
  });

  it("tar bort en närvarande kod", () => {
    expect(sorted(toggleLeaf(set("62010", "62020"), "62010"))).toEqual(["62020"]);
  });

  it("rör inte källmängden (immutabel)", () => {
    const source = set("62010");
    toggleLeaf(source, "62020");
    expect(sorted(source)).toEqual(["62010"]);
  });
});

describe("toggleGroup — expansion + avmarkering (tri-state förälder-klick)", () => {
  it("expanderar en tom mängd till alla gruppens löv", () => {
    expect(sorted(toggleGroup(set(), DIV62))).toEqual(["62010", "62020"]);
  });

  it("avmarkerar hela gruppen när alla dess löv redan är valda", () => {
    expect(sorted(toggleGroup(set("62010", "62020"), DIV62))).toEqual([]);
  });

  it("expanderar från PARTIELLT val (indeterminate) till alla gruppens löv", () => {
    // Ett klick på en indeterminate förälder ska markera ALLA, inte avmarkera.
    expect(sorted(toggleGroup(set("62010"), DIV62))).toEqual(["62010", "62020"]);
  });

  it("rör inte löv utanför gruppen (annan huvudgrupp orörd)", () => {
    expect(sorted(toggleGroup(set("63110"), DIV62))).toEqual([
      "62010",
      "62020",
      "63110",
    ]);
  });

  it("hela avdelningen expanderar över alla underliggande huvudgruppers löv", () => {
    expect(sorted(toggleGroup(set(), SECTION_J))).toEqual([
      "62010",
      "62020",
      "63110",
    ]);
  });

  it("tom grupp är en no-op", () => {
    expect(sorted(toggleGroup(set("62010"), []))).toEqual(["62010"]);
  });
});

describe("groupTriState — härledd förälder-state", () => {
  it("alla valda → checked", () => {
    expect(groupTriState(set("62010", "62020"), DIV62)).toBe("checked");
  });

  it("några valda → indeterminate", () => {
    expect(groupTriState(set("62010"), DIV62)).toBe("indeterminate");
  });

  it("inga valda → unchecked", () => {
    expect(groupTriState(set("63110"), DIV62)).toBe("unchecked");
  });

  it("tom grupp → unchecked", () => {
    expect(groupTriState(set("62010"), [])).toBe("unchecked");
  });

  it("avmarkering av ETT löv ur en full grupp gör föräldern indeterminate", () => {
    const afterDeselect = toggleLeaf(set(...DIV62), "62010");
    expect(groupTriState(afterDeselect, DIV62)).toBe("indeterminate");
  });

  it("hela avdelningen är checked först när alla huvudgruppers löv är valda", () => {
    expect(groupTriState(set("62010", "62020"), SECTION_J)).toBe("indeterminate");
    expect(groupTriState(set("62010", "62020", "63110"), SECTION_J)).toBe("checked");
  });
});
