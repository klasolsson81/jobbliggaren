import { describe, expect, it } from "vitest";
import { PIPELINE_ORDER } from "@/lib/applications/status";
import {
  countByStatus,
  totalCount,
} from "@/lib/applications/pipeline-counts";
import { isPersonnummerShapedOrgNr } from "@/lib/dto/company-registry";
import {
  buildGuestPipeline,
  GUEST_COMPANY_WATCHES,
  GUEST_MOCK,
} from "./mock-data";
import { toCompanyWatches, toPipelineGroups } from "./mock-adapters";

/**
 * #1572 — adaptrarna är enda vägen från gäst-mocken till de BE-DTO:er appens
 * `<ApplicationSummary>` och `<CompanySummary>` läser. Testerna nedan mäter det
 * som skiljer en adapter från en omdöpning: att statusvokabulärerna faktiskt
 * möts, och att de två halvorna av en pipeline-grupp inte kan säga olika saker.
 */
describe("toPipelineGroups", () => {
  it("varje mappad status är en RIKTIG ApplicationStatus", () => {
    // Gästmocken har fem statusar, appen tio, och två av gästens namn
    // (`Interview`, `Offer`) finns inte i appens union alls. Faller så snart en
    // mappning pekar på ett namn backend inte känner.
    const groups = toPipelineGroups(buildGuestPipeline());

    expect(groups.length).toBeGreaterThan(0);
    for (const g of groups) {
      expect(PIPELINE_ORDER).toContain(g.status);
    }
  });

  it("mappningen är injektiv — två gäststatusar kollapsar inte till en", () => {
    // En kollaps hade tyst halverat ett steg i sammanfattningen och ändå
    // summerat rätt totalt, vilket är precis den sortens fel `totalCount` inte
    // kan se.
    const groups = toPipelineGroups(buildGuestPipeline());
    const statuses = groups.map((g) => g.status);

    expect(new Set(statuses).size).toBe(statuses.length);
  });

  it("count och applications kan inte säga olika saker", () => {
    const groups = toPipelineGroups(buildGuestPipeline());

    for (const g of groups) {
      expect(g.applications).toHaveLength(g.count);
      for (const app of g.applications) {
        expect(app.status).toBe(g.status);
      }
    }
  });

  it("summan överlever mappningen — sammanfattningen räknar samma total", () => {
    const groups = toPipelineGroups(buildGuestPipeline());

    expect(totalCount(countByStatus(groups))).toBe(
      GUEST_MOCK.applications.length,
    );
  });

  it("tomt-lägets gren är onåbar på gästytan", () => {
    // `<ApplicationSummary>`s tomt-läge bär en CTA till `/ny-ansokan`, som är en
    // skyddad route. Sidan skickar därför ingen href-prop för den grenen, och
    // det här är mätningen som gör den utelämnandet sant i stället för antaget.
    const groups = toPipelineGroups(buildGuestPipeline());

    expect(totalCount(countByStatus(groups))).toBeGreaterThan(0);
  });
});

describe("toCompanyWatches", () => {
  it("bevarar antal, och tomt-lägets gren är onåbar", () => {
    // Samma skäl som ovan: `<CompanySummary>`s tomt-läge länkar till
    // `/foretag/sok`, också skyddad.
    const watches = toCompanyWatches(GUEST_COMPANY_WATCHES);

    expect(watches).toHaveLength(GUEST_COMPANY_WATCHES.length);
    expect(watches.length).toBeGreaterThan(0);
  });

  it("org.nr är varken ett giltigt org.nr ELLER personnummer-format", () => {
    // Repot är publikt, och de två halvorna svarar på olika frågor.
    //
    // Luhn: värdet är inte ett registrerat org.nr. Men Luhn ensamt utesluter INTE den
    // klass #841 handlar om — `9001010001` är Luhn-ogiltigt OCH personnummer-format,
    // alltså en enskild firmas innehavares personnummer i org.nr-position. Repots egen
    // auktoritet på den formen är `isPersonnummerShapedOrgNr` (ADR 0088 D4, #454), och
    // det är den som pinnas här (`security-auditor` Minor 2, 2026-08-29).
    const luhnOk = (n: string) =>
      [...n].reduce((sum, ch, i) => {
        const d = Number(ch) * (i % 2 === 0 ? 2 : 1);
        return sum + (d > 9 ? d - 9 : d);
      }, 0) %
        10 ===
      0;
    // Kontrollvärdet som visar att de två predikaten är oberoende: utan det läser
    // paret nedan som en dubblering av samma mätning.
    expect(luhnOk("9001010001")).toBe(false);
    expect(isPersonnummerShapedOrgNr("9001010001")).toBe(true);

    for (const w of toCompanyWatches(GUEST_COMPANY_WATCHES)) {
      const orgNr = w.organizationNumber!;
      expect(orgNr).toMatch(/^\d{10}$/);
      expect(luhnOk(orgNr), orgNr).toBe(false);
      expect(isPersonnummerShapedOrgNr(orgNr), orgNr).toBe(false);
    }
  });

  it("matchningsraden renderas, filter-noten gör det inte", () => {
    // Sammanfattningen tiger om matchning när NÅGON bevakning är obedömd — utan den
    // första invarianten kunde mocken tyst tömma raden.
    //
    // Filter-noten ska däremot INTE renderas här: den löses på appytan av "Visa
    // bevakade företag", och gästytan renderar ingen länk. Ett filter i mocken hade
    // gett en brasklapp besökaren inte kan följa upp (`design-reviewer` Minor 3).
    const watches = toCompanyWatches(GUEST_COMPANY_WATCHES);

    expect(watches.reduce((s, w) => s + w.activeAdCount, 0)).toBeGreaterThan(0);
    expect(watches.every((w) => w.matchingAdCount !== null)).toBe(true);
    expect(watches.every((w) => w.filter === null)).toBe(true);
  });
});
