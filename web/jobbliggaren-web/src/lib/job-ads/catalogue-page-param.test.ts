import { describe, expect, it } from "vitest";
import { parseCataloguePageParam } from "./catalogue-page-param";

describe("parseCataloguePageParam (ADR 0175 F11)", () => {
  it("reads no sida as page 1", () => {
    expect(parseCataloguePageParam(undefined)).toEqual({ kind: "page", page: 1 });
  });

  it("sends sida=1 to the bare path, so page 1 has one address", () => {
    expect(parseCataloguePageParam("1")).toEqual({ kind: "redirectToFirst" });
  });

  it.each([
    ["2", 2],
    ["40", 40],
    ["4000", 4000],
    ["4001", 4001],
  ])("reads sida=%s as page %i, and leaves the range to the API", (sida, page) => {
    expect(parseCataloguePageParam(sida)).toEqual({ kind: "page", page });
  });

  it.each(["0", "02", "-1", "1.5", "abc", "", " 2", "2 ", "1e3", "99999999999999999999"])(
    "answers sida=%j with no such page, without asking the API",
    (sida) => {
      expect(parseCataloguePageParam(sida)).toEqual({ kind: "noSuchPage" });
    }
  );

  it("answers a repeated sida with no such page", () => {
    expect(parseCataloguePageParam(["2", "3"])).toEqual({ kind: "noSuchPage" });
  });
});
