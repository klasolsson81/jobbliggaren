import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { CriterionDialog } from "./criterion-dialog";
import type {
  CompanyWatchCriterion,
  CriterionReference,
} from "@/lib/dto/company-criteria";

// The dialog's FIRST tests (#1711). The preview hook polls a BFF route and the two actions are
// Server Actions; neither is the subject here, so both are stubbed to their resting shape.
vi.mock("@/lib/hooks/use-criterion-preview-count", () => ({
  useCriterionPreviewCount: () => ({ preview: null, loading: false }),
}));
vi.mock("@/lib/actions/company-criteria", () => ({
  createCriterionAction: vi.fn(),
  updateCriterionAction: vi.fn(),
}));

// Huvudgrupp 62 carries FOUR leaves, as in `sni-2025.v1.json`, and Västra Götaland two kommuner:
// the two whole-node picks the old copy counted as "1 vald bransch" / "1 vald kommun".
const REFERENCE: CriterionReference = {
  sniVersion: "SNI 2025",
  kommunVersion: "2025",
  sni: [
    {
      code: "J",
      name: "Informations- och kommunikationsverksamhet",
      divisions: [
        {
          code: "62",
          name: "Dataprogrammering, datakonsultverksamhet o.d.",
          leaves: [
            { code: "62100", name: "Dataprogrammering" },
            { code: "62201", name: "Datakonsultverksamhet" },
            { code: "62202", name: "Datordrifttjänster" },
            { code: "62900", name: "Andra it-tjänster" },
          ],
        },
      ],
    },
  ],
  lan: [
    {
      code: "14",
      name: "Västra Götalands län",
      kommuner: [
        { code: "1480", name: "Göteborg" },
        { code: "1481", name: "Mölndal" },
      ],
    },
    { code: "01", name: "Stockholms län", kommuner: [{ code: "0180", name: "Stockholm" }] },
  ],
};
const WHOLE_HUVUDGRUPP = ["62100", "62201", "62202", "62900"];
const WHOLE_LAN = ["1480", "1481"];

function criterion(sniCodes: string[], municipalityCodes: string[]): CompanyWatchCriterion {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    sniCodes,
    municipalityCodes,
    label: null,
    createdAt: "2026-08-01T08:00:00+00:00",
    updatedAt: "2026-08-01T08:00:00+00:00",
    ads: { magnitude: 42, saturated: false, tooBroad: false, notMaterialised: false },
    matching: { count: 7, tooBroad: false, notMaterialised: false },
  };
}

function openEdit(sniCodes: string[], municipalityCodes: string[]) {
  render(
    <CriterionDialog
      open
      onOpenChange={() => {}}
      criterion={criterion(sniCodes, municipalityCodes)}
      reference={REFERENCE}
    />,
  );
  return screen.getByRole("dialog");
}
// The two picker counters, one per axis. The picker's <section> and its tree root both carry
// `role="group"` under the axis name, so the group is the one that holds the counter; and NOT just
// `p[aria-live]` — the filter status above each counter is a polite `role="status"` too (the
// `criterion-picker.test.tsx` precedent).
const COUNTER = 'p[aria-live="polite"]:not([role])';
function counters(dialog: HTMLElement): string[] {
  return ["Branscher", "Kommuner"].map((axis) => {
    const group = within(dialog)
      .getAllByRole("group", { name: axis })
      .find((g) => g.querySelector(COUNTER) !== null);
    return group?.querySelector<HTMLElement>(COUNTER)?.textContent?.trim() ?? "";
  });
}

describe("CriterionDialog — the pick counters (#1711)", () => {
  it("a whole huvudgrupp and a whole län are ONE pick each, and a pick is called a pick — never a bransch or a kommun", () => {
    const dialog = openEdit(WHOLE_HUVUDGRUPP, WHOLE_LAN);
    expect(counters(dialog)).toEqual(["1 val", "1 val"]);
    const text = dialog.textContent ?? "";
    expect(text).not.toMatch(/vald(a)? bransch/);
    expect(text).not.toMatch(/vald(a)? kommun/);
  });

  it("the count is DECOMPOSED, not the raw leaf count: two leaves of four read 2, all four read 1", () => {
    const { unmount } = render(
      <CriterionDialog
        open
        onOpenChange={() => {}}
        criterion={criterion(["62100", "62201"], ["1480"])}
        reference={REFERENCE}
      />,
    );
    expect(counters(screen.getByRole("dialog"))).toEqual(["2 val", "1 val"]);
    unmount();
    expect(counters(openEdit(WHOLE_HUVUDGRUPP, ["1480"]))[0]).toBe("1 val");
  });

  // design-reviewer B-2 (2026-09-08): the breadth line counts RAW leaves so a narrow watch and a broad
  // one are tellable apart (pinned in `criterion-breadth.test.tsx`). The dialog answers a different
  // question under a different noun, so the two numbers may differ for the same watch without
  // either being wrong.
  it("one leaf and the whole huvudgrupp are both 1 val in the dialog", () => {
    const { unmount } = render(
      <CriterionDialog
        open
        onOpenChange={() => {}}
        criterion={criterion(["62100"], WHOLE_LAN)}
        reference={REFERENCE}
      />,
    );
    expect(counters(screen.getByRole("dialog"))).toEqual(["1 val", "1 val"]);
    unmount();
    expect(counters(openEdit(WHOLE_HUVUDGRUPP, WHOLE_LAN))).toEqual(["1 val", "1 val"]);
  });

  it("the clear control beside the counter uses the same noun", () => {
    const dialog = openEdit(WHOLE_HUVUDGRUPP, WHOLE_LAN);
    expect(within(dialog).getAllByRole("button", { name: "Rensa val" })).toHaveLength(2);
  });
});

describe("CriterionDialog — a11y (no description)", () => {
  it("carries no intro, so the dialog is described by nothing", () => {
    const dialog = openEdit(WHOLE_HUVUDGRUPP, WHOLE_LAN);
    expect(dialog).not.toHaveAttribute("aria-describedby");
  });
});
