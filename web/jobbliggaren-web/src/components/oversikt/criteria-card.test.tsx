import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { CriteriaCard, criteriaCardIsWide } from "./criteria-card";
import messages from "../../../messages/sv";
import { buildCriterionAdsHref } from "@/lib/company-criteria/criterion-ads-href";
import type { ApiResult } from "@/lib/dto/_helpers";
import type {
  CompanyWatchCriterion,
  CriterionReference,
  ListCompanyWatchCriteriaResult,
} from "@/lib/dto/company-criteria";

// Rows are built as `ListCompanyWatchCriteriaQueryHandler` projects them: both zod refinements
// hold in every fixture — a number exactly when neither flag is set, and never both flags.
const counted = (magnitude: number, saturated = false) => ({
  magnitude,
  saturated,
  tooBroad: false,
  notMaterialised: false,
});
const ADS_TOO_BROAD = { magnitude: null, saturated: false, tooBroad: true, notMaterialised: false };
const matchCounted = (count: number) => ({ count, tooBroad: false, notMaterialised: false });
const MATCH_TOO_BROAD = { count: null, tooBroad: true, notMaterialised: false };
const MATCH_NOT_MATERIALISED = { count: null, tooBroad: false, notMaterialised: true };
const MATCH_NOT_ASSESSED = { count: null, tooBroad: false, notMaterialised: false };

function criterion(overrides: Partial<CompanyWatchCriterion> = {}): CompanyWatchCriterion {
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
const ok = (items: CompanyWatchCriterion[]): ApiResult<ListCompanyWatchCriteriaResult> => ({
  kind: "ok",
  data: items,
});
const errored: ApiResult<ListCompanyWatchCriteriaResult> = { kind: "error" };

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
  lan: [{ code: "14", name: "Västra Götalands län", kommuner: [{ code: "1480", name: "Göteborg" }] }],
};

const COPY = messages.oversikt;
const ID = "11111111-1111-1111-1111-111111111111";

function card() {
  return screen.getByRole("region", { name: COPY.criteriaSummary.heading });
}
function text(el: Element | null): string {
  return (el?.textContent ?? "").replace(/\s+/g, " ").trim();
}

describe("CriteriaCard — one watch", () => {
  it("the matching count is the big number; name, breadth and the ads link beneath; the solid CTA goes to the matching ads", () => {
    render(<CriteriaCard criteria={ok([criterion()])} reference={REFERENCE} />);
    expect(card()).toHaveAttribute("data-span", "4");
    expect(text(card().querySelector<HTMLElement>(".jp-ov-num__value"))).toBe("7");
    expect(text(card().querySelector<HTMLElement>(".jp-ov-num__unit"))).toBe("matchande annonser");
    expect(within(card()).getByText("Utveckling i Göteborg")).toBeInTheDocument();
    expect(within(card()).getByText("1 bransch · 1 kommun")).toBeInTheDocument();
    expect(within(card()).getByRole("link", { name: "42 aktiva annonser" })).toHaveAttribute(
      "href",
      buildCriterionAdsHref(ID, 1, "all"),
    );
    const cta = within(card()).getByRole("link", { name: COPY.cards.matchingCta });
    expect(cta).toHaveAttribute("href", buildCriterionAdsHref(ID, 1, "matching"));
    expect(cta.className).toContain("jp-ov-cta--info");
  });

  it("the matching number is stated ONCE — as the big number, not again as a line", () => {
    render(<CriteriaCard criteria={ok([criterion()])} reference={REFERENCE} />);
    expect(within(card()).queryByText(/matchande annonser just nu/)).toBeNull();
  });

  it("a counted matching zero renders 0 and no CTA — a zero has nowhere to lead", () => {
    render(<CriteriaCard criteria={ok([criterion({ matching: matchCounted(0) })])} reference={REFERENCE} />);
    expect(text(card().querySelector<HTMLElement>(".jp-ov-num__value"))).toBe("0");
    expect(within(card()).queryByRole("link", { name: COPY.cards.matchingCta })).toBeNull();
  });

  it("too broad: no number, the refusal stated once with its way forward, no CTA — never a zero", () => {
    render(
      <CriteriaCard criteria={ok([criterion({ ads: ADS_TOO_BROAD, matching: MATCH_TOO_BROAD })])} reference={REFERENCE} />,
    );
    expect(card().querySelector<HTMLElement>(".jp-ov-num")).toBeNull();
    expect(within(card()).getAllByText(/fler företag än vi kan räkna annonser för/)).toHaveLength(1);
    expect(within(card()).getByRole("link", { name: "Ändra bevakningen" })).toHaveAttribute(
      "href",
      "/foretag/branschbevakningar",
    );
    expect(within(card()).queryByRole("link", { name: COPY.cards.matchingCta })).toBeNull();
    expect(card().textContent).not.toMatch(/(^|\D)0(\D|$)/);
  });

  it("not materialised: ignorance, not refusal — the ads link stands, the number waits, no advice", () => {
    render(<CriteriaCard criteria={ok([criterion({ matching: MATCH_NOT_MATERIALISED })])} reference={REFERENCE} />);
    expect(card().querySelector<HTMLElement>(".jp-ov-num")).toBeNull();
    expect(within(card()).getByRole("link", { name: "42 aktiva annonser" })).toBeInTheDocument();
    expect(within(card()).getByText(/inte framräknad än/)).toBeInTheDocument();
    expect(within(card()).queryByRole("link", { name: "Ändra bevakningen" })).toBeNull();
  });

  it("not assessed: the nudge to match settings, the ads link kept, no number", () => {
    render(<CriteriaCard criteria={ok([criterion({ matching: MATCH_NOT_ASSESSED })])} reference={REFERENCE} />);
    expect(card().querySelector<HTMLElement>(".jp-ov-num")).toBeNull();
    expect(within(card()).getByRole("link", { name: "Ställ in matchning" })).toBeInTheDocument();
    expect(within(card()).getByRole("link", { name: "42 aktiva annonser" })).toBeInTheDocument();
  });

  it("a degraded reference tree keeps the numbers and falls back to the user's own label", () => {
    render(<CriteriaCard criteria={ok([criterion()])} reference={null} />);
    expect(text(card().querySelector<HTMLElement>(".jp-ov-num__value"))).toBe("7");
    expect(within(card()).getByText("Utveckling i Göteborg")).toBeInTheDocument();
  });

  it("no user label: the derived label, and without a tree the neutral noun — never a blank line", () => {
    const { unmount } = render(<CriteriaCard criteria={ok([criterion({ label: null })])} reference={REFERENCE} />);
    expect(within(card()).getByText("Dataprogrammering, datakonsultverksamhet o.d. · Göteborg")).toBeInTheDocument();
    unmount();
    render(<CriteriaCard criteria={ok([criterion({ label: null })])} reference={null} />);
    expect(within(card()).getByText("Branschbevakning", { selector: ".jp-ov-sub" })).toBeInTheDocument();
  });
});

describe("CriteriaCard — two or more watches", () => {
  const two = ok([criterion({ id: "a", label: "Första" }), criterion({ id: "b", label: "Andra", matching: matchCounted(3) })]);

  it("reflows to a full row with one ledger row per watch, in handler order, and NO sum", () => {
    render(<CriteriaCard criteria={two} reference={REFERENCE} />);
    expect(card()).toHaveAttribute("data-span", "12");
    expect(card().querySelector<HTMLElement>(".jp-ov-num")).toBeNull();
    const rows = [...card().querySelectorAll<HTMLElement>(".jp-ov-criteria__row")];
    expect(rows.map((r) => text(r.querySelector<HTMLElement>(".jp-ov-criteria__name")))).toEqual(["Första", "Andra"]);
    // Each row carries its OWN matching number and link; nothing adds 7 + 3.
    expect(within(rows[0]!).getByRole("link", { name: "7 matchande annonser just nu" })).toHaveAttribute(
      "href",
      buildCriterionAdsHref("a", 1, "matching"),
    );
    expect(within(rows[1]!).getByRole("link", { name: "3 matchande annonser just nu" })).toBeInTheDocument();
    expect(card().textContent).not.toMatch(/\b10\b/);
  });

  it("the head carries the count, and the list is labelled by it", () => {
    render(<CriteriaCard criteria={two} reference={REFERENCE} />);
    expect(within(card()).getByText("2 branschbevakningar")).toBeInTheDocument();
    expect(within(card()).getByRole("list", { name: "2 branschbevakningar" })).toBeInTheDocument();
  });

  it("the CTA is an outline link to the catalogue — there is no summed target to be solid about", () => {
    render(<CriteriaCard criteria={two} reference={REFERENCE} />);
    const cta = within(card()).getByRole("link", { name: COPY.criteriaSummary.link });
    expect(cta).toHaveAttribute("href", "/foretag/branschbevakningar");
    expect(cta.className).toContain("jp-ov-cta--outline");
  });

  it("the too-broad advice is stated once beneath the list, and the refused row keeps only its short refusal", () => {
    render(
      <CriteriaCard
        criteria={ok([criterion({ id: "a" }), criterion({ id: "b", ads: ADS_TOO_BROAD, matching: MATCH_TOO_BROAD })])}
        reference={REFERENCE}
      />,
    );
    expect(within(card()).getByText(COPY.criteriaSummary.tooBroadAdvice, { exact: false })).toBeInTheDocument();
    expect(within(card()).getAllByText("Bevakningen matchar fler företag än vi kan räkna annonser för.")).toHaveLength(1);
  });

  it("criteriaCardIsWide is the one expression the page reads for the siblings' spans", () => {
    expect(criteriaCardIsWide(two)).toBe(true);
    expect(criteriaCardIsWide(ok([criterion()]))).toBe(false);
    expect(criteriaCardIsWide(ok([]))).toBe(false);
    expect(criteriaCardIsWide(errored)).toBe(false);
  });
});

describe("CriteriaCard — no watches, or none readable", () => {
  it("empty: the empty copy and an emphasised link to the catalogue, span 4", () => {
    render(<CriteriaCard criteria={ok([])} reference={REFERENCE} />);
    expect(card()).toHaveAttribute("data-span", "4");
    expect(within(card()).getByText(COPY.criteriaSummary.emptyTitle)).toBeInTheDocument();
    expect(within(card()).getByRole("link", { name: COPY.criteriaSummary.emptyCta })).toHaveAttribute(
      "href",
      "/foretag/branschbevakningar",
    );
  });

  it("unavailable: an en-dash and the unavailable copy — never 'you have none'", () => {
    render(<CriteriaCard criteria={errored} reference={REFERENCE} />);
    expect(text(card().querySelector<HTMLElement>(".jp-ov-num__value"))).toBe(COPY.cards.unmeasured);
    expect(within(card()).getByText(COPY.criteriaSummary.unavailable)).toBeInTheDocument();
    expect(within(card()).queryByText(COPY.criteriaSummary.emptyTitle)).toBeNull();
  });
});
