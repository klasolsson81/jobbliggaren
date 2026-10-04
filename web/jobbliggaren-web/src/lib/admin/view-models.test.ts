import { describe, expect, it } from "vitest";
import { listRegion } from "./view-models";

describe("listRegion", () => {
  it("builds the empty region for no rows, and a loaded one otherwise", () => {
    expect(listRegion([])).toEqual({ kind: "empty" });
    expect(listRegion(["a"])).toEqual({ kind: "loaded", data: ["a"] });
  });
});
