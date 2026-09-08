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

// The catalogue route the block links to. Not a prop any more — the component owns it,
// because every consumer is an authenticated surface and there was never a second value.
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
      <CriteriaSummary criteria={errored} reference={REFERENCE} />,
    );

    expect(visibleText()).toContain("Smarta bevakningar kunde inte hämtas");
    expect(screen.queryByText("Du har inga smarta bevakningar än")).toBeNull();
    // Never a fabricated zero for a list that was never read.
    expect(visibleText()).not.toContain("0 smarta bevakningar");
  });

  it("tomt läge säger att inga finns, och erbjuder vägen att skapa en", () => {
    render(<CriteriaSummary criteria={ok([])} reference={REFERENCE} />);

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
      />,
    );

    const text = visibleText();
    expect(text).toContain("för bred");
    expect(text).not.toContain("Inga aktiva annonser");
    // Both arms refuse for the same reason, so the ROW says it once (design-reviewer Major 1);
    // the second line is the block-level advice, which is asserted on its own below.
    expect(document.querySelectorAll(".jp-appsummary__watch .jp-matchline")).toHaveLength(1);
    // A refusal has nothing to link to.
    expect(document.querySelectorAll("a.jp-countlink")).toHaveLength(0);
  });

  // design-reviewer Major 2, measured at the cap: the 160-character advice rendered five times
  // verbatim, with five identical "Ändra bevakningen" links to one destination. The row now carries
  // the status and the BLOCK carries the advice — the `sharedRefusal` rule, one axis up.
  it("rådet om för breda bevakningar står EN gång under listan, inte en gång per rad", () => {
    render(
      <CriteriaSummary
        criteria={ok([
          criterion({ id: "a", ads: ADS_TOO_BROAD, matching: MATCH_TOO_BROAD }),
          criterion({ id: "b", ads: ADS_TOO_BROAD, matching: MATCH_TOO_BROAD }),
          criterion({ id: "c", ads: ADS_TOO_BROAD, matching: MATCH_TOO_BROAD }),
        ])}
        reference={REFERENCE}
      />,
    );

    // Three rows, three short statuses.
    expect(document.querySelectorAll(".jp-appsummary__watch")).toHaveLength(3);
    expect(document.querySelectorAll(".jp-appsummary__watch .jp-matchline")).toHaveLength(3);
    // ONE advice line, ONE call to action — however many rows are refused.
    expect(document.querySelectorAll(".jp-appsummary__advice")).toHaveLength(1);
    expect(screen.getAllByRole("link", { name: "Ändra bevakningen" })).toHaveLength(1);
    // And the long per-row sentence is gone from the rows entirely.
    expect(visibleText()).not.toContain("eller matcha dem mot din profil");
  });

  it("rådet uteblir helt när ingen bevakning är för bred", () => {
    render(
      <CriteriaSummary
        criteria={ok([criterion({ ads: counted(42), matching: matchCounted(7) })])}
        reference={REFERENCE}
      />,
    );

    expect(document.querySelectorAll(".jp-appsummary__advice")).toHaveLength(0);
    expect(screen.queryByRole("link", { name: "Ändra bevakningen" })).toBeNull();
  });

  // design-reviewer Major 3: "från dessa företag" has no antecedent here — no company is rendered
  // anywhere in the block, so the label must carry itself.
  it("annonsetiketten är självbärande — ingen syftning på företag som inte renderas", () => {
    render(
      <CriteriaSummary
        criteria={ok([criterion({ ads: counted(42) })])}
        reference={REFERENCE}
      />,
    );

    const text = visibleText();
    expect(text).toContain("42 aktiva annonser");
    expect(text).not.toContain("dessa företag");
  });

  // design-reviewer Minor 7: only the ADS arm is unanswerable here, so the plural
  // "Annonssiffrorna" claimed both numbers and read as a contradiction against the next line.
  //
  // ⚠ THE PREMISE IS THE PRODUCIBLE ONE, and the first draft's was not (code-reviewer raised it
  // ungraded; measured here). Pairing `notMaterialised` ads with a COUNTED matching cannot arise:
  // `ListCompanyWatchCriteriaQueryHandler` calls `MatchingBatchAsync` first, whose `ResolveIdsAsync`
  // reads the SAME memoised `MagnitudeAsync` (`CriterionMatchingAdSetResolver.cs:284`) and returns
  // `NotMaterialised` on it (`:291`) — so within one request the two arms cannot disagree that way,
  // and `sharedRefusal` would fire.
  //
  // NOT ASSESSED is the divergence production DOES emit: `MatchingBatchAsync` returns it for every
  // pending criterion at its assessability gate, BEFORE any magnitude is consulted, so it pairs with
  // an unanswerable ads magnitude. `sharedRefusal` stays null and the single-arm branch is reached.
  it("när bara annonstalet saknas skalas påståendet till sitt eget led", () => {
    render(
      <CriteriaSummary
        criteria={ok([
          criterion({ ads: ADS_NOT_MATERIALISED, matching: MATCH_NOT_ASSESSED }),
        ])}
        reference={REFERENCE}
      />,
    );

    const text = visibleText();
    expect(text).toContain("Antalet aktiva annonser är inte framräknat än");
    // The plural would have claimed the matching arm too, which has its own separate reason.
    expect(text).not.toContain("Annonssiffrorna");
    expect(text).toContain("Ställ in matchning");
  });

  it("ej framräknad renderar okunskap, aldrig en nolla och aldrig ett råd", () => {
    render(
      <CriteriaSummary
        criteria={ok([
          criterion({ ads: ADS_NOT_MATERIALISED, matching: MATCH_NOT_MATERIALISED }),
        ])}
        reference={REFERENCE}
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
      />,
    );

    const text = visibleText();
    expect(text).toContain("42 aktiva annonser");
    expect(text).toContain("Ställ in matchning");
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
      />,
    );

    // In the summary variant BOTH refusal arms render the same short status, so copy can no longer
    // tell them apart — the discriminator is whether the matching line SURVIVED. If the shared
    // collapse had fired it would have suppressed that line, swallowing an answer the block has.
    // That tests the logic rather than the wording, which is the stronger pin.
    const text = visibleText();
    expect(text).toContain("för bred för att räknas");
    expect(text).toContain("Ställ in matchning");
    expect(document.querySelectorAll(".jp-appsummary__watch .jp-matchline")).toHaveLength(2);
  });

  it("en räknad nolla skrivs ut som ett svar, men får ingen länk", () => {
    render(
      <CriteriaSummary
        criteria={ok([criterion({ ads: counted(0), matching: matchCounted(0) })])}
        reference={REFERENCE}
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
      />,
    );

    expect(
      document.querySelector(".jp-appsummary__watchname")?.textContent?.trim(),
    ).toBe("Bevakning");
    expect(visibleText()).toContain("42 aktiva annonser");
    expect(visibleText()).toContain("7 matchande annonser");
  });

  it("renderar varje bevakning upp till taket, i handlerns egen ordning", () => {
    const items = Array.from({ length: 20 }, (_, i) =>
      criterion({ id: `id-${i}`, label: `Bevakning ${i}` }),
    );
    render(
      <CriteriaSummary criteria={ok(items)} reference={REFERENCE} />,
    );

    const names = [...document.querySelectorAll(".jp-appsummary__watchname")].map((n) =>
      n.textContent?.trim(),
    );
    expect(names).toHaveLength(20);
    expect(names[0]).toBe("Bevakning 0");
    expect(names[19]).toBe("Bevakning 19");
  });
});
