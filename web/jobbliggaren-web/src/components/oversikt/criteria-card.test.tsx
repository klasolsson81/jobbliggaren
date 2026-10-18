import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { CriteriaCard, criteriaCardIsWide } from "./criteria-card";
import messages from "../../../messages/sv";
import { buildCriterionAdsHref } from "@/lib/company-criteria/criterion-ads-href";
import type { ApiResult } from "@/lib/dto/_helpers";
import type {
  CompanyWatchCriterion,
  CriterionNewAdCounts,
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

// The count read answers one row per watch the account owns (`CriterionNewAdCountsDto`). The suites
// that measure nothing about the pill pass a failed read, which renders the card without one.
const COUNTS_UNREAD: ApiResult<CriterionNewAdCounts> = { kind: "error" };
const counts = (
  ...rows: ReadonlyArray<readonly [criterionId: string, newAds: number | null]>
): ApiResult<CriterionNewAdCounts> => ({
  kind: "ok",
  data: { items: rows.map(([criterionId, newAds]) => ({ criterionId, newAds })) },
});

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
const ADS_COPY = messages.pages.foretag.criteria.ads;
const ID = "11111111-1111-1111-1111-111111111111";

function card() {
  return screen.getByRole("region", { name: COPY.criteriaSummary.heading });
}
function text(el: Element | null): string {
  return (el?.textContent ?? "").replace(/\s+/g, " ").trim();
}

describe("CriteriaCard — one watch", () => {
  it("the matching count is the big number; name, breadth and the ads link beneath; the solid CTA goes to the matching ads", () => {
    render(<CriteriaCard criteria={ok([criterion()])} reference={REFERENCE} newAdCounts={COUNTS_UNREAD} />);
    expect(card()).toHaveAttribute("data-span", "4");
    expect(text(card().querySelector<HTMLElement>(".jp-ov-num__value"))).toBe("7");
    expect(text(card().querySelector<HTMLElement>(".jp-ov-num__unit"))).toBe("matchande annonser");
    expect(within(card()).getByText("Utveckling i Göteborg")).toBeInTheDocument();
    expect(within(card()).getByText("1 bransch · 1 kommun")).toBeInTheDocument();
    expect(within(card()).getByRole("link", { name: "42 aktiva annonser" })).toHaveAttribute(
      "href",
      buildCriterionAdsHref(ID, 1, "all"),
    );
    const cta = within(card()).getByRole("link", { name: COPY.cards.criteriaCtaAria });
    expect(cta).toHaveAttribute("href", buildCriterionAdsHref(ID, 1, "matching"));
    expect(cta.className).toContain("jp-ov-cta--info");
    expect(cta.textContent?.trim()).toBe(COPY.cards.matchingCta);
    expect(COPY.cards.criteriaCtaAria.startsWith(COPY.cards.matchingCta)).toBe(true);
  });

  it("the matching number is stated ONCE — as the big number, not again as a line", () => {
    render(<CriteriaCard criteria={ok([criterion()])} reference={REFERENCE} newAdCounts={COUNTS_UNREAD} />);
    expect(within(card()).queryByText(/matchande annonser just nu/)).toBeNull();
  });

  it("a counted matching zero renders 0 and no CTA — a zero has nowhere to lead", () => {
    render(<CriteriaCard criteria={ok([criterion({ matching: matchCounted(0) })])} reference={REFERENCE} newAdCounts={COUNTS_UNREAD} />);
    expect(text(card().querySelector<HTMLElement>(".jp-ov-num__value"))).toBe("0");
    expect(within(card()).queryByRole("link", { name: COPY.cards.criteriaCtaAria })).toBeNull();
  });

  it("too broad: no number, the refusal stated once with its way forward, no CTA — never a zero", () => {
    render(
      <CriteriaCard criteria={ok([criterion({ ads: ADS_TOO_BROAD, matching: MATCH_TOO_BROAD })])} reference={REFERENCE} newAdCounts={COUNTS_UNREAD} />,
    );
    expect(card().querySelector<HTMLElement>(".jp-ov-num")).toBeNull();
    expect(within(card()).getAllByText(/fler företag än vi kan räkna annonser för/)).toHaveLength(1);
    expect(within(card()).getByRole("link", { name: "Ändra bevakningen" })).toHaveAttribute(
      "href",
      "/foretag/branschbevakningar",
    );
    expect(within(card()).queryByRole("link", { name: COPY.cards.criteriaCtaAria })).toBeNull();
    expect(card().textContent).not.toMatch(/(^|\D)0(\D|$)/);
  });

  // The same guard-3 state as the N ≥ 2 case below, in the one-watch form: the row carries the FULL
  // matching refusal here, and it must name the ads, never the companies (design-reviewer, #1715).
  it("a refused matching beside a counted ad number at N = 1: the full sentence names the ads, never the companies", () => {
    render(<CriteriaCard criteria={ok([criterion({ ads: counted(3000), matching: MATCH_TOO_BROAD })])} reference={REFERENCE} newAdCounts={COUNTS_UNREAD} />);
    expect(card().querySelector<HTMLElement>(".jp-ov-num")).toBeNull();
    expect(text(card().querySelector<HTMLElement>("a.jp-countlink"))).toBe("3 000 aktiva annonser");
    expect(text(card())).toContain("Bevakningen har fler annonser än vi kan matcha.");
    expect(text(card())).not.toContain("fler företag än vi kan räkna annonser för");
    expect(within(card()).getByRole("link", { name: "Ändra bevakningen" })).toBeInTheDocument();
  });

  it("not materialised: ignorance, not refusal — the ads link stands, the number waits, no advice", () => {
    render(<CriteriaCard criteria={ok([criterion({ matching: MATCH_NOT_MATERIALISED })])} reference={REFERENCE} newAdCounts={COUNTS_UNREAD} />);
    expect(card().querySelector<HTMLElement>(".jp-ov-num")).toBeNull();
    expect(within(card()).getByRole("link", { name: "42 aktiva annonser" })).toBeInTheDocument();
    expect(within(card()).getByText(/inte framräknad än/)).toBeInTheDocument();
    expect(within(card()).queryByRole("link", { name: "Ändra bevakningen" })).toBeNull();
  });

  it("not assessed: no nudge to match settings, the ads link kept, no number", () => {
    render(<CriteriaCard criteria={ok([criterion({ matching: MATCH_NOT_ASSESSED })])} reference={REFERENCE} newAdCounts={COUNTS_UNREAD} />);
    expect(card().querySelector<HTMLElement>(".jp-ov-num")).toBeNull();
    expect(within(card()).queryByRole("link", { name: "Ställ in matchning" })).toBeNull();
    expect(within(card()).queryByText(COPY.notices.calloutText)).toBeNull();
    expect(within(card()).getByRole("link", { name: "42 aktiva annonser" })).toBeInTheDocument();
  });

  it("a degraded reference tree keeps the numbers and falls back to the user's own label", () => {
    render(<CriteriaCard criteria={ok([criterion()])} reference={null} newAdCounts={COUNTS_UNREAD} />);
    expect(text(card().querySelector<HTMLElement>(".jp-ov-num__value"))).toBe("7");
    expect(within(card()).getByText("Utveckling i Göteborg")).toBeInTheDocument();
  });

  it("no user label: the derived label, and without a tree the neutral noun — never a blank line", () => {
    const { unmount } = render(<CriteriaCard criteria={ok([criterion({ label: null })])} reference={REFERENCE} newAdCounts={COUNTS_UNREAD} />);
    expect(within(card()).getByText("Dataprogrammering, datakonsultverksamhet o.d. · Göteborg")).toBeInTheDocument();
    unmount();
    render(<CriteriaCard criteria={ok([criterion({ label: null })])} reference={null} newAdCounts={COUNTS_UNREAD} />);
    expect(within(card()).getByText("Branschbevakning", { selector: ".jp-ov-sub" })).toBeInTheDocument();
  });
});

describe("CriteriaCard — two or more watches", () => {
  const two = ok([criterion({ id: "a", label: "Första" }), criterion({ id: "b", label: "Andra", matching: matchCounted(3) })]);

  it("reflows to a full row with one ledger row per watch, in handler order, and NO sum", () => {
    render(<CriteriaCard criteria={two} reference={REFERENCE} newAdCounts={COUNTS_UNREAD} />);
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
    render(<CriteriaCard criteria={two} reference={REFERENCE} newAdCounts={COUNTS_UNREAD} />);
    expect(within(card()).getByText("2 branschbevakningar")).toBeInTheDocument();
    expect(within(card()).getByRole("list", { name: "2 branschbevakningar" })).toBeInTheDocument();
  });

  it("the CTA is an outline link to the catalogue — there is no summed target to be solid about", () => {
    render(<CriteriaCard criteria={two} reference={REFERENCE} newAdCounts={COUNTS_UNREAD} />);
    const cta = within(card()).getByRole("link", { name: COPY.criteriaSummary.link });
    expect(cta).toHaveAttribute("href", "/foretag/branschbevakningar");
    expect(cta.className).toContain("jp-ov-cta--outline");
  });

  it("the too-broad advice is stated once beneath the list, and the refused row keeps only its short refusal", () => {
    render(
      <CriteriaCard
        criteria={ok([criterion({ id: "a" }), criterion({ id: "b", ads: ADS_TOO_BROAD, matching: MATCH_TOO_BROAD })])}
        reference={REFERENCE}
        newAdCounts={COUNTS_UNREAD}
      />,
    );
    expect(within(card()).getByText(COPY.criteriaSummary.adsTooBroadAdvice, { exact: false })).toBeInTheDocument();
    // A row refusing both arms is counted under the ads arm only — the block collapses as the row does.
    expect(within(card()).queryByText(COPY.criteriaSummary.matchingTooBroadAdvice, { exact: false })).toBeNull();
    expect(within(card()).getAllByText("Bevakningen matchar fler företag än vi kan räkna annonser för.")).toHaveLength(1);
  });

  // A watch with 2 001-10 000 active ads: CriterionMatchingAdSetResolver.ResolveIdsAsync refuses the
  // matching set above MaxSetSize (2 000) while the ad count runs to Ceiling (10 000), so ONE row
  // carries a counted ad number AND a refused matching number (#1715). The mirror pairing — a
  // refused ad count beside a counted matching number — is unreachable: the resolver's first guard
  // refuses the matching set before any count.
  it("a refused matching beside a counted ad number names the number it lacks, never the number it stands beside", () => {
    render(
      <CriteriaCard
        criteria={ok([criterion({ id: "a", ads: counted(3000), matching: MATCH_TOO_BROAD }), criterion({ id: "b" })])}
        reference={REFERENCE}
        newAdCounts={COUNTS_UNREAD}
      />,
    );
    const rows = [...card().querySelectorAll<HTMLElement>(".jp-ov-criteria__row")];
    const refused = rows[0]!;
    const adsLink = refused.querySelector<HTMLAnchorElement>("a.jp-countlink");
    expect(adsLink).toHaveAttribute("href", buildCriterionAdsHref("a", 1, "all"));
    expect(text(adsLink)).toBe("3 000 aktiva annonser");
    expect(within(refused).getByText(ADS_COPY.matchingTooBroadShort)).toBeInTheDocument();
    // The ads-arm short form says the ADS could not be counted; beside "3 000 aktiva annonser" that
    // is a contradiction, and no row may carry it here.
    for (const row of rows) expect(text(row)).not.toContain("fler företag än vi kan räkna annonser för");
    // The block advice names the matching arm and nothing else: no row here lacks an ad number.
    expect(within(card()).getByText(COPY.criteriaSummary.matchingTooBroadAdvice, { exact: false })).toBeInTheDocument();
    expect(within(card()).queryByText(COPY.criteriaSummary.adsTooBroadAdvice, { exact: false })).toBeNull();
    expect(within(card()).getByRole("link", { name: "Ändra bevakningen" })).toHaveAttribute("href", "/foretag/branschbevakningar");
  });

  it("not assessed: no row carries the nudge to match settings", () => {
    render(
      <CriteriaCard
        criteria={ok([criterion({ id: "a", matching: MATCH_NOT_ASSESSED }), criterion({ id: "b", matching: MATCH_NOT_ASSESSED })])}
        reference={REFERENCE}
        newAdCounts={COUNTS_UNREAD}
      />,
    );
    expect(within(card()).getAllByRole("link", { name: "42 aktiva annonser" })).toHaveLength(2);
    expect(within(card()).queryByRole("link", { name: "Ställ in matchning" })).toBeNull();
    expect(within(card()).queryByText(COPY.notices.calloutText)).toBeNull();
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
    render(<CriteriaCard criteria={ok([])} reference={REFERENCE} newAdCounts={COUNTS_UNREAD} />);
    expect(card()).toHaveAttribute("data-span", "4");
    expect(within(card()).getByText(COPY.criteriaSummary.emptyTitle)).toBeInTheDocument();
    expect(within(card()).getByRole("link", { name: COPY.criteriaSummary.emptyCta })).toHaveAttribute(
      "href",
      "/foretag/branschbevakningar",
    );
  });

  it("unavailable: an en-dash and the unavailable copy — never 'you have none'", () => {
    render(<CriteriaCard criteria={errored} reference={REFERENCE} newAdCounts={COUNTS_UNREAD} />);
    expect(text(card().querySelector<HTMLElement>(".jp-ov-num__value"))).toBe(COPY.cards.unmeasured);
    expect(within(card()).getByText(COPY.criteriaSummary.unavailable)).toBeInTheDocument();
    expect(within(card()).queryByText(COPY.criteriaSummary.emptyTitle)).toBeNull();
  });
});

describe("CriteriaCard — the 'N nya' pill (ADR 0175 D7)", () => {
  const PILL = ".jp-ov-card__pill";
  const nyaHref = (id: string) => `/foretag/branschbevakningar/${id}/nya`;

  it("one watch: the pill sits in the head, reads 'N nya', is named after the watch and links to its /nya", () => {
    render(<CriteriaCard criteria={ok([criterion()])} reference={REFERENCE} newAdCounts={counts([ID, 3])} />);
    const pill = within(card()).getByRole("link", { name: /^3 nya annonser i branschbevakningen / });
    expect(text(pill)).toBe("3 nya");
    expect(pill).toHaveAccessibleName("3 nya annonser i branschbevakningen Utveckling i Göteborg");
    expect(pill).toHaveAttribute("href", nyaHref(ID));
    // In the head, never beside the matching number.
    expect(pill.closest(".jp-ov-card__head")).not.toBeNull();
    expect(text(card().querySelector<HTMLElement>(".jp-ov-num__value"))).toBe("7");
  });

  it("one new ad: the singular form, in the visible text and the name alike", () => {
    render(<CriteriaCard criteria={ok([criterion()])} reference={REFERENCE} newAdCounts={counts([ID, 1])} />);
    const pill = within(card()).getByRole("link", { name: "1 ny annons i branschbevakningen Utveckling i Göteborg" });
    expect(text(pill)).toBe("1 ny");
  });

  it("the name is the card's own row name: without a user label, the derived one", () => {
    render(
      <CriteriaCard criteria={ok([criterion({ label: null })])} reference={REFERENCE} newAdCounts={counts([ID, 2])} />,
    );
    expect(
      within(card()).getByRole("link", {
        name: "2 nya annonser i branschbevakningen Dataprogrammering, datakonsultverksamhet o.d. · Göteborg",
      }),
    ).toBeInTheDocument();
  });

  it("the pill shows even when the matching number is not there to carry", () => {
    render(
      <CriteriaCard
        criteria={ok([criterion({ matching: MATCH_NOT_ASSESSED })])}
        reference={REFERENCE}
        newAdCounts={counts([ID, 4])}
      />,
    );
    expect(card().querySelector<HTMLElement>(".jp-ov-num")).toBeNull();
    expect(within(card()).getByRole("link", { name: /^4 nya annonser i branschbevakningen / })).toHaveAttribute(
      "href",
      nyaHref(ID),
    );
  });

  const noPill: ReadonlyArray<readonly [string, ApiResult<CriterionNewAdCounts>]> = [
    ["a counted 0", counts([ID, 0])],
    ["a null count (the last scan was not Scanned)", counts([ID, null])],
    ["a failed count read", { kind: "error" }],
    ["a rate-limited count read", { kind: "rateLimited", retryAfterSeconds: 60 }],
    ["an unauthorized count read", { kind: "unauthorized" }],
  ];
  it.each(noPill)("%s: no pill, and no 0 in its place", (_, newAdCounts) => {
    render(<CriteriaCard criteria={ok([criterion()])} reference={REFERENCE} newAdCounts={newAdCounts} />);
    expect(card().querySelector(PILL)).toBeNull();
    expect(text(card())).not.toMatch(/\d+ nya?\b/);
    // The rest of the card is unchanged by the count read.
    expect(text(card().querySelector<HTMLElement>(".jp-ov-num__value"))).toBe("7");
  });

  it("two or more: each row's pill follows its name on the name line; the head keeps only the count; never a sum", () => {
    render(
      <CriteriaCard
        criteria={ok([
          criterion({ id: "a", label: "Första" }),
          criterion({ id: "b", label: "Andra", matching: MATCH_NOT_ASSESSED }),
        ])}
        reference={REFERENCE}
        newAdCounts={counts(["a", 3], ["b", 2])}
      />,
    );
    const head = card().querySelector<HTMLElement>(".jp-ov-card__head")!;
    expect(head.querySelector(PILL)).toBeNull();
    expect(text(head.querySelector<HTMLElement>(".jp-ov-card__count"))).toBe("2 branschbevakningar");

    const rows = [...card().querySelectorAll<HTMLElement>(".jp-ov-criteria__row")];
    const expected = [
      { id: "a", name: "Första", visible: "3 nya", aria: "3 nya annonser i branschbevakningen Första" },
      { id: "b", name: "Andra", visible: "2 nya", aria: "2 nya annonser i branschbevakningen Andra" },
    ];
    rows.forEach((row, i) => {
      const want = expected[i]!;
      const line = row.querySelector<HTMLElement>(".jp-ov-criteria__nameline")!;
      // DOM order: the name, then its pill.
      expect([...line.children].map((c) => c.className)).toEqual([
        "jp-ov-criteria__name",
        "jp-ov-card__pill tabular-nums",
      ]);
      expect(text(line.firstElementChild)).toBe(want.name);
      const pill = within(line).getByRole("link", { name: want.aria });
      expect(text(pill)).toBe(want.visible);
      expect(pill).toHaveAttribute("href", nyaHref(want.id));
    });
    expect(card().querySelectorAll(PILL)).toHaveLength(2);
    expect(text(card())).not.toMatch(/\b5 nya\b/);
  });

  it("two or more: only the rows with a number above 0 get a pill", () => {
    render(
      <CriteriaCard
        criteria={ok([
          criterion({ id: "a", label: "Första" }),
          criterion({ id: "b", label: "Andra" }),
          criterion({ id: "c", label: "Tredje", ads: ADS_TOO_BROAD, matching: MATCH_TOO_BROAD }),
        ])}
        reference={REFERENCE}
        newAdCounts={counts(["a", 3], ["b", 0], ["c", null])}
      />,
    );
    const rows = [...card().querySelectorAll<HTMLElement>(".jp-ov-criteria__row")];
    expect(rows.map((row) => row.querySelectorAll(PILL).length)).toEqual([1, 0, 0]);
    expect(within(rows[0]!).getByRole("link", { name: "3 nya annonser i branschbevakningen Första" })).toBeInTheDocument();
  });

  it("two or more with a failed count read: no pill on any row", () => {
    render(
      <CriteriaCard
        criteria={ok([criterion({ id: "a", label: "Första" }), criterion({ id: "b", label: "Andra" })])}
        reference={REFERENCE}
        newAdCounts={COUNTS_UNREAD}
      />,
    );
    expect(card().querySelector(PILL)).toBeNull();
    expect(card().querySelectorAll(".jp-ov-criteria__row")).toHaveLength(2);
  });
});
