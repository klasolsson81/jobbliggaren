import { describe, it, expect } from "vitest";
import { deriveDisplayLabel } from "./display-label";
import type { CriterionReference } from "@/lib/dto/company-criteria";

const copy = { moreSuffix: "m.fl.", separator: " · " } as const;

// Minimal referens: SNI-avdelning J med huvudgrupperna 62 (Dataprogrammering) och 63
// (Informationstjänster); län 01 (Stockholm) med kommunerna 0180 (Stockholm) och 0184 (Solna).
const reference: Pick<CriterionReference, "sni" | "lan"> = {
  sni: [
    {
      code: "J",
      name: "Informations- och kommunikationsverksamhet",
      divisions: [
        {
          code: "62",
          name: "Dataprogrammering",
          leaves: [
            { code: "62010", name: "Dataprogrammering" },
            { code: "62020", name: "Datakonsultverksamhet" },
          ],
        },
        {
          code: "63",
          name: "Informationstjänster",
          leaves: [{ code: "63110", name: "Databehandling, hosting" }],
        },
      ],
    },
  ],
  lan: [
    {
      code: "01",
      name: "Stockholms län",
      kommuner: [
        { code: "0180", name: "Stockholm" },
        { code: "0184", name: "Solna" },
      ],
    },
  ],
};

describe("deriveDisplayLabel", () => {
  it("namnger avdelningens huvudgrupp + kommun för ett enkelt val", () => {
    expect(deriveDisplayLabel(["62010"], ["0180"], reference, copy)).toBe(
      "Dataprogrammering · Stockholm",
    );
  });

  it("flera huvudgrupper → 'm.fl.' på SNI-axeln; flera kommuner → 'm.fl.' på kommun-axeln", () => {
    expect(
      deriveDisplayLabel(["62010", "63110"], ["0180", "0184"], reference, copy),
    ).toBe("Dataprogrammering m.fl. · Stockholm m.fl.");
  });

  it("namnger en huvudgrupp EN gång även när flera av dess löv är valda", () => {
    expect(deriveDisplayLabel(["62010", "62020"], ["0180"], reference, copy)).toBe(
      "Dataprogrammering · Stockholm",
    );
  });

  it("bara SNI valt → bara SNI-delen (ingen separator)", () => {
    expect(deriveDisplayLabel(["62010"], [], reference, copy)).toBe(
      "Dataprogrammering",
    );
  });

  it("bara kommun vald → bara kommun-delen", () => {
    expect(deriveDisplayLabel([], ["0184"], reference, copy)).toBe("Solna");
  });

  it("okända koder (stale snapshot) mot båda axlar → null (caller faller tillbaka på summering)", () => {
    expect(deriveDisplayLabel(["99999"], ["9999"], reference, copy)).toBeNull();
  });

  it("tomma axlar → null", () => {
    expect(deriveDisplayLabel([], [], reference, copy)).toBeNull();
  });

  it("delvis okänd SNI-kod bidrar inte men den kända axeln renderas ändå", () => {
    expect(deriveDisplayLabel(["99999"], ["0180"], reference, copy)).toBe(
      "Stockholm",
    );
  });
});
