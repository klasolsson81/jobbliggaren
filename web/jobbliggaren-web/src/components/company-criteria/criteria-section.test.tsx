import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { CriteriaSection } from "./criteria-section";
import type {
  CompanyWatchCriterion,
  CriterionReference,
} from "@/lib/dto/company-criteria";

// #1703 — the catalogue's FIRST tests. Before this delta nothing pinned `/foretag/branschbevakningar`
// at all (`senior-cto-advisor` M-3, 2026-09-08): no `criteria-section.test.tsx`, no
// `criterion-row.test.tsx`. The row could have been disconnected from `CriterionAdLines` again and
// the whole suite would have stayed green — which is the shape that let the surface pay for numbers
// it did not render for a whole release in the first place.
//
// Rows are built as `ListCompanyWatchCriteriaQueryHandler` projects them, and every fixture below
// satisfies both zod refinements on each member — a number exactly when neither flag is set, and the
// two flags never both set (§5 `Tests:`: the premise is one production produces). `ads`/`matching`
// are non-nullable on a list row (`companyWatchCriterionSchema`), so there is no degraded-read arm
// here; that one belongs to the detail page, which reads the two numbers separately.
const counted = (magnitude: number, saturated = false) => ({
  magnitude,
  saturated,
  tooBroad: false,
  notMaterialised: false,
});
const ADS_TOO_BROAD = {
  magnitude: null,
  saturated: false,
  tooBroad: true,
  notMaterialised: false,
};
const ADS_NOT_MATERIALISED = {
  magnitude: null,
  saturated: false,
  tooBroad: false,
  notMaterialised: true,
};
const matchCounted = (count: number) => ({
  count,
  tooBroad: false,
  notMaterialised: false,
});
const MATCH_TOO_BROAD = { count: null, tooBroad: true, notMaterialised: false };
const MATCH_NOT_MATERIALISED = { count: null, tooBroad: false, notMaterialised: true };
// NOT ASSESSED: the user has stated no occupation. `CriterionMatchingAdSetResolver` returns it
// BEFORE consulting the magnitude, so it pairs with ANY ads member — including a refused one, which
// is the single-armed case one test below turns on.
const MATCH_NOT_ASSESSED = { count: null, tooBroad: false, notMaterialised: false };

function criterion(
  overrides: Partial<CompanyWatchCriterion> = {},
): CompanyWatchCriterion {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    sniCodes: ["62010"],
    municipalityCodes: ["1480"],
    label: "Utveckling i Göteborg",
    createdAt: "2026-08-01T08:00:00+00:00",
    updatedAt: "2026-08-01T08:00:00+00:00",
    ads: counted(42),
    matching: matchCounted(7),
    ...overrides,
  };
}

const REFERENCE: CriterionReference = {
  sniVersion: "SNI 2007",
  kommunVersion: "2025",
  sni: [
    {
      code: "J",
      name: "Informations- och kommunikationsverksamhet",
      divisions: [
        {
          code: "62",
          name: "Dataprogrammering, datakonsultverksamhet o.d.",
          leaves: [{ code: "62010", name: "Dataprogrammering" }],
        },
      ],
    },
  ],
  lan: [
    { code: "14", name: "Västra Götalands län", kommuner: [{ code: "1480", name: "Göteborg" }] },
  ],
};

function visibleText(): string {
  return (document.body.textContent ?? "").replace(/\s+/g, " ").trim();
}

const occurrences = (haystack: string, needle: string) => haystack.split(needle).length - 1;

describe("CriteriaSection", () => {
  // The seam itself. #1681 part 2 put `ads` and `matching` on every list row and the catalogue read
  // neither, so the page paid the full per-user grading and rendered none of it — measured three
  // times independently (CTO M-a 2026-09-07; the part-3 session the same day; this session's
  // zero control 2026-09-08: 7/7 readings, 0 `.jp-matchline`, 0 `a.jp-countlink`).
  it("raden renderar de annonssiffror rutten redan betalar för", () => {
    render(
      <CriteriaSection
        items={[criterion({ ads: counted(18), matching: matchCounted(3) })]}
        reference={REFERENCE}
      />,
    );

    const adsLink = screen.getByRole("link", { name: "18 aktiva annonser" });
    expect(adsLink.getAttribute("href")).toBe(
      "/foretag/branschbevakningar/11111111-1111-1111-1111-111111111111/annonser",
    );
    const matchingLink = screen.getByRole("link", { name: "3 matchande annonser just nu" });
    expect(matchingLink.getAttribute("href")).toBe(
      "/foretag/branschbevakningar/11111111-1111-1111-1111-111111111111/annonser?visa=matchande",
    );
  });

  // `variant="standalone"`: the row renders no company — they sit behind "Visa företag" — so the ad
  // label must carry itself. "från dessa företag" would be a reference with no antecedent on this
  // surface (design-reviewer Major 3, #1681 part 3).
  it("annonsetiketten är självbärande — ingen syftning på företag raden inte visar", () => {
    render(<CriteriaSection items={[criterion({ ads: counted(18) })]} reference={REFERENCE} />);

    expect(visibleText()).toContain("18 aktiva annonser");
    expect(visibleText()).not.toContain("från dessa företag");
  });

  // ADR 0047, in the catalogue's own form. At N=1 nothing above the row states the advice, so the
  // row carries the WHOLE sentence — refusal, consequence AND advice. What it must NOT carry is the
  // "Ändra bevakningen" link: on this surface that names the page the reader is standing on, in the
  // same Swedish word as a button ~200 px away (senior-cto-advisor D1, 2026-09-08). The way forward
  // is that button, and the assertion pins BOTH halves — a missing link is only acceptable while the
  // control is there.
  it("vid EN bevakning står hela vägran i raden, men genvägen till denna sida uteblir", () => {
    render(
      <CriteriaSection
        items={[criterion({ ads: ADS_TOO_BROAD, matching: MATCH_TOO_BROAD })]}
        reference={REFERENCE}
      />,
    );

    const text = visibleText();
    expect(occurrences(text, "matchar fler företag än vi kan räkna annonser för")).toBe(1);
    expect(text).toContain("Färre branscher eller kommuner ger färre företag");
    // Zero self-links: not one CTA anywhere on the surface.
    expect(screen.queryAllByRole("link", { name: "Ändra bevakningen" })).toHaveLength(0);
    expect(document.querySelectorAll("a.jp-nudgelink")).toHaveLength(0);
    // …and the action the CTA would have pointed at is present, in the row.
    expect(
      screen.getByRole("button", { name: "Ändra bevakningen Utveckling i Göteborg" }),
    ).toBeTruthy();
    // Nothing to hoist the advice out of at one row.
    expect(document.querySelectorAll(".jp-criteria-advice")).toHaveLength(0);
  });

  // The single-armed refusal: only the ads arm is unanswerable, so `sharedRefusal` does not fire and
  // the row lands in the `ads.tooBroad` branch. That branch is where the CTA mattered most on the
  // other two surfaces, so this is the arm most likely to regrow a self-link.
  it("en ENARMAD vägran ger inte heller någon genväg tillbaka hit", () => {
    render(
      <CriteriaSection
        items={[criterion({ ads: ADS_TOO_BROAD, matching: MATCH_NOT_ASSESSED })]}
        reference={REFERENCE}
      />,
    );

    expect(visibleText()).toContain("Därför visas inte antalet annonser");
    expect(screen.queryAllByRole("link", { name: "Ändra bevakningen" })).toHaveLength(0);
    // The not-assessed nudge is a DIFFERENT action, to a surface this page offers no substitute for,
    // and it must survive the gate untouched (CTO D1, binding 1).
    expect(
      screen.getByRole("link", { name: "Ställ in matchning" }).getAttribute("href"),
    ).toBe("/installningar#matchning");
  });

  // design-reviewer Major 2, one axis up and on a new surface: above one row the 160-character
  // sentence must not repeat per row. The row states the STATUS, the block states what to do — and
  // the block's sentence must not reword the refusal the rows just carried, which is #1707's shape.
  it("rådet står EN gång under listan, inte en gång per rad, och upprepar inte vägran", () => {
    render(
      <CriteriaSection
        items={[
          criterion({ id: "a", ads: ADS_TOO_BROAD, matching: MATCH_TOO_BROAD }),
          criterion({ id: "b", ads: ADS_TOO_BROAD, matching: MATCH_TOO_BROAD }),
          criterion({ id: "c", ads: ADS_TOO_BROAD, matching: MATCH_TOO_BROAD }),
        ]}
        reference={REFERENCE}
      />,
    );

    const text = visibleText();
    // Three rows state the short refusal; the block does not restate it a fourth time.
    expect(occurrences(text, "matchar fler företag än vi kan räkna annonser för")).toBe(3);
    // The advice the short refusal drops is supplied once, beneath the list.
    expect(document.querySelectorAll(".jp-criteria-advice")).toHaveLength(1);
    expect(occurrences(text, "Färre branscher eller kommuner ger färre företag")).toBe(1);
    // Still no route back to this page, at any N.
    expect(screen.queryAllByRole("link", { name: "Ändra bevakningen" })).toHaveLength(0);
  });

  it("rådet uteblir helt när ingen bevakning är för bred", () => {
    render(
      <CriteriaSection
        items={[
          criterion({ id: "a", ads: counted(4), matching: matchCounted(1) }),
          criterion({ id: "b", ads: ADS_NOT_MATERIALISED, matching: MATCH_NOT_MATERIALISED }),
        ]}
        reference={REFERENCE}
      />,
    );

    expect(document.querySelectorAll(".jp-criteria-advice")).toHaveLength(0);
    expect(visibleText()).not.toContain("Färre branscher eller kommuner ger färre företag");
  });

  // ADR 0120: only a COUNTED zero is ever rendered as 0. An unmaterialised watch is ignorance, not
  // a zero, and it gets no link to an empty list either.
  it("ej framräknad renderar okunskap, aldrig en nolla", () => {
    render(
      <CriteriaSection
        items={[criterion({ ads: ADS_NOT_MATERIALISED, matching: MATCH_NOT_MATERIALISED })]}
        reference={REFERENCE}
      />,
    );

    const text = visibleText();
    expect(text).toContain("inte framräknade än");
    expect(text).not.toContain("Inga aktiva annonser");
    expect(document.querySelectorAll("a.jp-countlink")).toHaveLength(0);
  });

  it("en räknad nolla skrivs ut som ett svar, men får ingen länk", () => {
    render(
      <CriteriaSection
        items={[criterion({ ads: counted(0), matching: matchCounted(0) })]}
        reference={REFERENCE}
      />,
    );

    expect(visibleText()).toContain("Inga aktiva annonser just nu.");
    expect(document.querySelectorAll("a.jp-countlink")).toHaveLength(0);
  });

  // The breadth line is the row's DEFINITION and the ad numbers its OUTCOME, so the reading order is
  // definition → outcome (design-reviewer B-3, 2026-09-08). Pinned by DOM order rather than by text,
  // because "it looks right" is what the previous surface also believed.
  it("breddraden står före annonsraderna, aldrig efter dem", () => {
    render(<CriteriaSection items={[criterion()]} reference={REFERENCE} />);

    const body = document.querySelector(".jp-job__body");
    const nodes = [...(body?.querySelectorAll(".jp-criterion-breadth, .jp-matchline") ?? [])];
    expect(nodes.length).toBeGreaterThan(1);
    expect(nodes[0]?.className).toContain("jp-criterion-breadth");
  });
});
