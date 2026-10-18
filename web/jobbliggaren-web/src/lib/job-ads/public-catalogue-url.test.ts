import { describe, expect, it } from "vitest";
import { absoluteUrl } from "@/lib/site-url";
import { PUBLIC_CATALOGUE_PATH, publicCatalogueHref, publicCatalogueUrl } from "./public-catalogue-url";

describe("publicCatalogueHref / publicCatalogueUrl (ADR 0175 F1)", () => {
  it("is the bare path on page 1 and carries sida from page 2", () => {
    expect(PUBLIC_CATALOGUE_PATH).toBe("/lediga-jobb");
    expect(publicCatalogueHref(1)).toBe("/lediga-jobb");
    expect(publicCatalogueHref(2)).toBe("/lediga-jobb?sida=2");
    expect(publicCatalogueHref(2112)).toBe("/lediga-jobb?sida=2112");
  });

  it("makes the absolute form of the same address", () => {
    expect(publicCatalogueUrl(1)).toBe(absoluteUrl("/lediga-jobb"));
    expect(publicCatalogueUrl(3)).toBe(absoluteUrl("/lediga-jobb?sida=3"));
  });
});
