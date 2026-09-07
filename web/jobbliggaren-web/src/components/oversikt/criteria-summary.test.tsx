import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { CriteriaSummary } from "./criteria-summary";
import type { ApiResult } from "@/lib/dto/_helpers";
import type {
  CompanyWatchCriterion,
  CriterionReference,
  ListCompanyWatchCriteriaResult,
} from "@/lib/dto/company-criteria";

// Rows are built as `ListCompanyWatchCriteriaQueryHandler` projects them: the two members carry the
// SAME shapes the detail page reads, and both zod refinements hold in every fixture below — a
// number exactly when neither flag is set, and the two flags never both set. A fixture that broke
// either would be testing a wire shape production cannot emit.
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
// NOT ASSESSED: the caller has stated no occupation. `CriterionMatchingAdSetResolver` returns this
// BEFORE consulting the magnitude, so it pairs with ANY ads member — including the unanswerable
// ones, which is why one test below pairs it with a refusal.
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

function ok(items: CompanyWatchCriterion[]): ApiResult<ListCompanyWatchCriteriaResult> {
  return { kind: "ok", data: items };
}
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
  lan: [
    { code: "14", name: "Västra Götalands län", kommuner: [{ code: "1480", name: "Göteborg" }] },
  ],
};

const HREF = "/foretag/smarta-bevakningar";

function visibleText(): string {
  return (document.body.textContent ?? "").replace(/\s+/g, " ").trim();
}

describe("CriteriaSummary", () => {
  it("ankarraden räknar bevakningarna och länkar till katalogen", () => {
    render(
      <CriteriaSummary
        criteria={ok([criterion({ id: "a" }), criterion({ id: "b" })])}
        reference={REFERENCE}
        linkHref={HREF}
      />,
    );

    expect(
      document.querySelector(".jp-appsummary__totals")?.textContent?.trim(),
    ).toBe("2 smarta bevakningar");
    expect(
      screen.getByRole("link", { name: "Visa smarta bevakningar" }),
    ).toHaveAttribute("href", HREF);
  });

  // THE defect this form exists to prevent, and the reason variant A was rejected: criteria are
  // PREDICATES with no uniqueness constraint (CompanyWatchCriterionConfiguration declines
  // `UNIQUE(user_id, sni_codes, kommun_codes)` deliberately), so they may overlap and a duplicate
  // doubles a sum exactly. A summed total would therefore be inexact — against Klas's own
  // "exakta siffror" — and would have no destination to link to, since `buildCriterionAdsHref`
  // takes ONE id. If a later edit reintroduces a sum, this fails.
  it("summerar ALDRIG annonstalen över bevakningar", () => {
    render(
      <CriteriaSummary
        criteria={ok([
          criterion({ id: "a", ads: counted(42), matching: matchCounted(7) }),
          criterion({ id: "b", ads: counted(13), matching: matchCounted(2) }),
        ])}
        reference={REFERENCE}
        linkHref={HREF}
      />,
    );

    const text = visibleText();
    expect(text).toContain("42 aktiva annonser");
    expect(text).toContain("13 aktiva annonser");
    // 42+13 and 7+2 — neither sum may appear anywhere on the surface.
    expect(text).not.toContain("55");
    expect(text).not.toContain("9 matchande");
  });

  it("varje bevakning länkar till SINA egna annonser, aldrig till en delad destination", () => {
    render(
      <CriteriaSummary
        criteria={ok([
          criterion({ id: "aaaa1111-0000-4000-8000-000000000001" }),
          criterion({ id: "bbbb2222-0000-4000-8000-000000000002" }),
        ])}
        reference={REFERENCE}
        linkHref={HREF}
      />,
    );

    const hrefs = [...document.querySelectorAll("a.jp-countlink")].map((a) =>
      a.getAttribute("href"),
    );
    expect(hrefs).toEqual([
      "/foretag/smarta-bevakningar/aaaa1111-0000-4000-8000-000000000001/annonser",
      "/foretag/smarta-bevakningar/aaaa1111-0000-4000-8000-000000000001/annonser?visa=matchande",
      "/foretag/smarta-bevakningar/bbbb2222-0000-4000-8000-000000000002/annonser",
      "/foretag/smarta-bevakningar/bbbb2222-0000-4000-8000-000000000002/annonser?visa=matchande",
    ]);
  });

  it('"kunde inte hämtas" är inte "du har inga" — de två läsningarna skiljs', () => {
    render(
      <CriteriaSummary criteria={errored} reference={REFERENCE} linkHref={HREF} />,
    );

    expect(visibleText()).toContain("Smarta bevakningar kunde inte hämtas");
    expect(screen.queryByText("Du har inga smarta bevakningar än")).toBeNull();
    // Never a fabricated zero for a list that was never read.
    expect(visibleText()).not.toContain("0 smarta bevakningar");
  });

  it("tomt läge säger att inga finns, och erbjuder vägen att skapa en", () => {
    render(<CriteriaSummary criteria={ok([])} reference={REFERENCE} linkHref={HREF} />);

    expect(screen.getByText("Du har inga smarta bevakningar än")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ny smart bevakning" })).toHaveAttribute(
      "href",
      HREF,
    );
    expect(visibleText()).not.toContain("aktiva annonser");
  });

  // The three non-numbers. None of them may render as a 0 — that is the whole ADR 0120 family.
  it("för bred renderar en vägran, aldrig en nolla", () => {
    render(
      <CriteriaSummary
        criteria={ok([criterion({ ads: ADS_TOO_BROAD, matching: MATCH_TOO_BROAD })])}
        reference={REFERENCE}
        linkHref={HREF}
      />,
    );

    const text = visibleText();
    expect(text).toContain("för bred");
    expect(text).not.toContain("0 aktiva annonser");
    expect(text).not.toContain("Inga aktiva annonser");
    // Both arms refuse for the same reason, so it is said ONCE (design-reviewer Major 1).
    expect(document.querySelectorAll(".jp-matchline")).toHaveLength(1);
    // A refusal has nothing to link to.
    expect(document.querySelectorAll("a.jp-countlink")).toHaveLength(0);
  });

  it("ej framräknad renderar okunskap, aldrig en nolla och aldrig ett råd", () => {
    render(
      <CriteriaSummary
        criteria={ok([
          criterion({ ads: ADS_NOT_MATERIALISED, matching: MATCH_NOT_MATERIALISED }),
        ])}
        reference={REFERENCE}
        linkHref={HREF}
      />,
    );

    const text = visibleText();
    expect(text).toContain("inte framräknade än");
    expect(text).not.toContain("Inga aktiva annonser");
    // Deliberately no "ändra bevakningen": nothing is wrong with this watch, so advice to edit it
    // would be advice that cannot help.
    expect(text).not.toContain("Ändra bevakningen");
    expect(document.querySelectorAll("a.jp-countlink")).toHaveLength(0);
  });

  it("ej bedömd matchning nudgar, medan annonstalet står kvar", () => {
    render(
      <CriteriaSummary
        criteria={ok([criterion({ ads: counted(42), matching: MATCH_NOT_ASSESSED })])}
        reference={REFERENCE}
        linkHref={HREF}
      />,
    );

    const text = visibleText();
    expect(text).toContain("42 aktiva annonser");
    expect(text).toContain("Ställ in matchning");
    expect(text).not.toContain("0 matchande");
    expect(text).not.toContain("Inga matchande");
    // Only the ads half has a destination.
    expect(document.querySelectorAll("a.jp-countlink")).toHaveLength(1);
  });

  // PRODUCIBLE: the resolver returns NOT ASSESSED before consulting the magnitude, so the shared
  // refusal must NOT fire — the single-arm sentence is the correct one here.
  it("ej bedömd bredvid en för bred bevakning ger den ENARMADE vägran, inte den delade", () => {
    render(
      <CriteriaSummary
        criteria={ok([criterion({ ads: ADS_TOO_BROAD, matching: MATCH_NOT_ASSESSED })])}
        reference={REFERENCE}
        linkHref={HREF}
      />,
    );

    const text = visibleText();
    expect(text).toContain("för bred för att vi ska kunna räkna annonserna.");
    expect(text).not.toContain("eller matcha dem mot din profil");
    expect(text).toContain("Ställ in matchning");
  });

  it("en räknad nolla skrivs ut som ett svar, men får ingen länk", () => {
    render(
      <CriteriaSummary
        criteria={ok([criterion({ ads: counted(0), matching: matchCounted(0) })])}
        reference={REFERENCE}
        linkHref={HREF}
      />,
    );

    expect(visibleText()).toContain("Inga aktiva annonser");
    expect(document.querySelectorAll("a.jp-countlink")).toHaveLength(0);
  });

  it("mättat tal renderas som 10 000+, aldrig som takets siffra", () => {
    render(
      <CriteriaSummary
        criteria={ok([criterion({ ads: counted(10000, true) })])}
        reference={REFERENCE}
        linkHref={HREF}
      />,
    );

    expect(visibleText()).toContain("10 000+ aktiva annonser");
  });

  // Heading resolution: the user's own label wins; else derived from the tree; else neutral.
  it("rubriken faller etikett → härledd → neutral", () => {
    render(
      <CriteriaSummary
        criteria={ok([
          criterion({ id: "a", label: "Mitt eget namn" }),
          criterion({ id: "b", label: null }),
          criterion({ id: "c", label: null, sniCodes: [], municipalityCodes: [] }),
        ])}
        reference={REFERENCE}
        linkHref={HREF}
      />,
    );

    const names = [...document.querySelectorAll(".jp-appsummary__watchname")].map((n) =>
      n.textContent?.trim(),
    );
    expect(names[0]).toBe("Mitt eget namn");
    expect(names[1]).toBe("Dataprogrammering, datakonsultverksamhet o.d. · Göteborg");
    expect(names[2]).toBe("Bevakning");
  });

  // A degraded reference read must cost the HEADING, never the numbers — they are the block's
  // point and they do not depend on the tree.
  it("degraderat referensträd blankar rubriken till neutral men behåller talen", () => {
    render(
      <CriteriaSummary
        criteria={ok([criterion({ label: null, ads: counted(42), matching: matchCounted(7) })])}
        reference={null}
        linkHref={HREF}
      />,
    );

    expect(
      document.querySelector(".jp-appsummary__watchname")?.textContent?.trim(),
    ).toBe("Bevakning");
    expect(visibleText()).toContain("42 aktiva annonser");
    expect(visibleText()).toContain("7 matchande annonser");
  });

  // Parity `CompanySummary`: a surface with no authenticated destination renders no links at all,
  // rather than links that resolve to /logga-in under a label promising the ads.
  it("utan autentiserad destination renderas inga länkar alls", () => {
    render(
      <CriteriaSummary
        criteria={ok([criterion()])}
        reference={REFERENCE}
        linkHref={null}
      />,
    );

    expect(document.querySelectorAll("a")).toHaveLength(0);
    // The numbers still render — they are facts, and only their destinations were unavailable.
    expect(visibleText()).toContain("42 aktiva annonser");
    expect(visibleText()).toContain("7 matchande annonser");
  });

  it("renderar varje bevakning upp till taket, i handlerns egen ordning", () => {
    const items = Array.from({ length: 20 }, (_, i) =>
      criterion({ id: `id-${i}`, label: `Bevakning ${i}` }),
    );
    render(
      <CriteriaSummary criteria={ok(items)} reference={REFERENCE} linkHref={HREF} />,
    );

    const names = [...document.querySelectorAll(".jp-appsummary__watchname")].map((n) =>
      n.textContent?.trim(),
    );
    expect(names).toHaveLength(20);
    expect(names[0]).toBe("Bevakning 0");
    expect(names[19]).toBe("Bevakning 19");
  });
});
