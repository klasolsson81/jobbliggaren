import { describe, it, expect } from "vitest";
import { formatOrgNr } from "./org-nr";

describe("formatOrgNr (#454 PR-0 — delad org.nr-formatterare)", () => {
  it("formaterar 10 siffror som NNNNNN-NNNN", () => {
    expect(formatOrgNr("5560125790")).toBe("556012-5790");
  });

  it("annan längd visas verbatim (aldrig fel-splittad)", () => {
    expect(formatOrgNr("556012")).toBe("556012");
    expect(formatOrgNr("")).toBe("");
    expect(formatOrgNr("55601257901")).toBe("55601257901");
  });
});
