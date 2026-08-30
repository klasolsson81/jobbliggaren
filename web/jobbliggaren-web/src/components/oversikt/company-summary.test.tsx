import { readFileSync } from "node:fs";
import { describe, it, expect } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { CompanySummary } from "./company-summary";
import messages from "../../../messages/sv";
import type { ApiResult } from "@/lib/dto/_helpers";
import type {
  CompanyWatch,
  ListCompanyWatchesResult,
} from "@/lib/dto/company-follows";

// Raderna byggs som ListCompanyWatchesQueryHandler projicerar dem: `matchingAdCount`
// null = SSYK-gaten stängd (inget angivet yrke), och den gaten sätts EN gång per
// request — därav att fixturerna aldrig blandar null och tal utom i det test som
// mäter just `some`-diskriminatorn.
function watch(overrides: Partial<CompanyWatch> = {}): CompanyWatch {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    organizationNumber: "5566524301",
    isProtectedIdentity: false,
    companyName: "Friday Väst AB",
    followedAt: "2026-07-01T10:00:00Z",
    activeAdCount: 0,
    matchingAdCount: 0,
    filter: null,
    ...overrides,
  };
}

function ok(items: CompanyWatch[]): ApiResult<ListCompanyWatchesResult> {
  return { kind: "ok", data: items };
}

const NO_FILTER = null;

// Regeltexten LASES ur katalogen i stallet for att skrivas av. En hardkodad mening hade
// passerat aven efter att komponenten forkat sin egen copy -- och det ar precis det
// forkandet testet finns for att fanga.
const RULE = messages.jobads.companyWatches.filter;

function visibleText(el: Element | null): string {
  return (el?.textContent ?? "").replace(/\s+/g, " ").trim();
}

/**
 * The anchor is split across elements since the ad half became a link (Klas-direktiv
 * 2026-08-30), so an exact `getByText` over the whole sentence can no longer match. Reading the
 * totals span keeps the assertion on the rendered SENTENCE rather than on the markup carrying it.
 */
function anchorText(): string {
  return visibleText(document.querySelector(".jp-appsummary__totals"));
}

describe("CompanySummary", () => {
  it("ankarraden summerar bevakningar och aktiva annonser", () => {
    render(
      <CompanySummary
        watches={ok([
          watch({ id: "a", activeAdCount: 136 }),
          watch({ id: "b", activeAdCount: 4 }),
        ])}
        linkHref="/foretag/bevakade"
      />,
    );

    expect(anchorText()).toBe("2 bevakade företag · 140 aktiva annonser");
    expect(
      screen.getByRole("link", { name: "Visa bevakade företag" }),
    ).toHaveAttribute("href", "/foretag/bevakade");
  });

  // Defekten issuet stänger: bevakningar med aktiva annonser fick inte läsa som tomma
  // bara för att inget NYTT publicerats sedan besöket.
  it("bevakning med aktiva annonser läser aldrig som tom", () => {
    render(<CompanySummary watches={ok([watch({ activeAdCount: 136 })])} linkHref="/foretag/bevakade" />);

    expect(anchorText()).toBe("1 bevakat företag · 136 aktiva annonser");
    expect(screen.queryByText("Du bevakar inga företag än")).toBeNull();
  });

  it("noll aktiva annonser är ett mätt tillstånd, inte ett tomt-läge", () => {
    render(<CompanySummary watches={ok([watch({ activeAdCount: 0 })])} linkHref="/foretag/bevakade" />);

    expect(anchorText()).toBe("1 bevakat företag · inga aktiva annonser");
  });

  it("bedömd matchning renderas, och en bedömd nolla skrivs ut", () => {
    const { rerender } = render(
      <CompanySummary
        watches={ok([
          watch({ id: "a", activeAdCount: 136, matchingAdCount: 9 }),
          watch({ id: "b", activeAdCount: 4, matchingAdCount: 2 }),
        ])}
        linkHref="/foretag/bevakade"
      />,
    );
    expect(
      visibleText(document.querySelector(".jp-matchline")),
    ).toBe("11 matchande annonser hos dina bevakade företag");

    rerender(
      <CompanySummary watches={ok([watch({ matchingAdCount: 0 })])} linkHref="/foretag/bevakade" />,
    );
    expect(
      screen.getByText("Inga matchande annonser hos dina bevakade företag"),
    ).toBeInTheDocument();
  });

  it("ej bedömd matchning tiger helt — ingen nolla, ingen nudge", () => {
    render(
      <CompanySummary
        watches={ok([watch({ activeAdCount: 136, matchingAdCount: null })])}
        linkHref="/foretag/bevakade"
      />,
    );

    expect(screen.queryByText(/matchande/)).toBeNull();
    expect(screen.queryByText(/Ställ in matchning/)).toBeNull();
    // Ankarraden står kvar — det är den som stänger issuet.
    expect(anchorText()).toBe("1 bevakat företag · 136 aktiva annonser");
  });

  // `some`, inte `every`: bryter backendens per-request-gate någon gång får en delsumma
  // aldrig renderas som om den vore hela sanningen. Fixturen är en form produktionen
  // inte producerar i dag (gaten sätter matchingByOrgNr en gång för hela listan,
  // ListCompanyWatchesQueryHandler.cs:221-228) — testet påstår därför INGET om vad
  // produktionen gör, bara att läsningen degraderar säkert om invarianten brister.
  it("blandad null och tal: raden tystnar hellre än underskattar", () => {
    render(
      <CompanySummary
        watches={ok([
          watch({ id: "a", matchingAdCount: 9 }),
          watch({ id: "b", matchingAdCount: null }),
        ])}
        linkHref="/foretag/bevakade"
      />,
    );

    expect(screen.queryByText(/matchande/)).toBeNull();
  });

  it("filter-raden visas en gång över alla bevakningar, inte en per bevakning", () => {
    render(
      <CompanySummary
        watches={ok([
          watch({
            id: "a",
            filter: {
              municipalities: [],
              regions: [],
              onlyMatched: true,
              remote: false,
            },
          }),
          watch({
            id: "b",
            filter: {
              municipalities: ["kommun-1"],
              regions: [],
              onlyMatched: false,
              remote: false,
            },
          }),
          watch({ id: "c", filter: NO_FILTER }),
        ])}
        linkHref="/foretag/bevakade"
      />,
    );

    expect(
      screen.getByText(
        "2 bevakningar har notisfilter. Antalen ovan gäller alla annonser, oavsett filter.",
      ),
    ).toBeInTheDocument();
  });

  it("inget filter någonstans → ingen filter-rad", () => {
    render(<CompanySummary watches={ok([watch({ filter: NO_FILTER })])} linkHref="/foretag/bevakade" />);

    expect(screen.queryByText(/notisfilter/)).toBeNull();
  });

  it("noll bevakningar ger tomt-läget med Sök företag", () => {
    render(<CompanySummary watches={ok([])} linkHref="/foretag/bevakade" />);

    expect(screen.getByText("Du bevakar inga företag än")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Sök företag" })).toHaveAttribute(
      "href",
      "/foretag/sok",
    );
    expect(anchorText()).toBe("");
  });

  it("säger att bevakningarna inte kunde hämtas i stället för att påstå noll", () => {
    render(<CompanySummary watches={{ kind: "error" }} linkHref="/foretag/bevakade" />);

    expect(
      screen.getByText(
        "Bevakade företag kunde inte hämtas. Uppdatera sidan för att försöka igen.",
      ),
    ).toBeInTheDocument();
    // Fabrikation: en degraderad hämtning får varken visa en siffra eller tomt-läget.
    expect(anchorText()).toBe("");
    expect(screen.queryByText("Du bevakar inga företag än")).toBeNull();
  });

  it("summorna länkar DIREKT till annonserna hos varje bevakat företag", () => {
    // Klas-direktiv 2026-08-30: ett klick, inte två. Backend har bundit `string[]` hela tiden
    // (ADR 0087 D6), så axeln bär hela bevakningsmängden.
    render(
      <CompanySummary
        watches={ok([
          watch({ organizationNumber: "5566524301", activeAdCount: 100, matchingAdCount: 7 }),
          watch({
            id: "22222222-2222-2222-2222-222222222222",
            organizationNumber: "5560125790",
            activeAdCount: 36,
            matchingAdCount: 2,
          }),
        ])}
      linkHref="/foretag/bevakade"
      />,
    );

    expect(
      screen.getByRole("link", { name: "136 aktiva annonser" }),
    ).toHaveAttribute("href", "/jobb?employer=5566524301.5560125790");
    expect(
      screen.getByRole("link", { name: "9 matchande annonser" }),
    ).toHaveAttribute(
      "href",
      "/jobb?employer=5566524301.5560125790&matchGrades=Good.Strong",
    );

    // The qualifier is rendered, just OUTSIDE the link -- design-reviewer Major 3: two adjacent
    // rows must underline the same amount of text, not 135px on one and 337px on the other.
    expect(visibleText(document.querySelector(".jp-matchline"))).toBe(
      "9 matchande annonser hos dina bevakade företag",
    );
  });

  it("en yta utan autentiserad destination (gäst) länkar INGEN summa", () => {
    // ⛔ Measured regression, not a hypothetical: the base merge brought #1572 in, and the guest
    // mock's three watches all carry ten-digit org.nr with `isProtectedIdentity: false`. Every
    // link gate this component owns passed, so the guest demo rendered count links to
    // `/jobb?employer=…` -- an `(app)/` segment, therefore in PROTECTED_PREFIXES, therefore
    // `/logga-in` for a guest. `linkHref: null` was made required by #1572 to stop exactly this,
    // and the ad links now read it too.
    const { container } = render(
      <CompanySummary
        watches={ok([
          watch({ organizationNumber: "5566524301", activeAdCount: 100, matchingAdCount: 7 }),
        ])}
        linkHref={null}
      />,
    );

    expect(screen.queryAllByRole("link")).toHaveLength(0);
    expect(container.innerHTML).not.toContain("/jobb");
    expect(container.innerHTML).not.toContain("employer=");
    // The numbers are still true and still shown -- only the route is withheld.
    expect(anchorText()).toBe("1 bevakat företag · 100 aktiva annonser");
  });

  it("gästytan förklarar INTE en frånvaro den själv orsakade", () => {
    // The explanation blames the DATA ("1 bevakning kan inte visas som en lista"). On a surface
    // that links nothing by design, that sentence would be false about the cause.
    render(
      <CompanySummary
        watches={ok([
          watch({ organizationNumber: null, isProtectedIdentity: true, activeAdCount: 40 }),
        ])}
        linkHref={null}
      />,
    );

    expect(screen.queryByText(/kan inte visas som en lista/)).toBeNull();
  });

  it("den maskade bevakningen FÖRKLARAS — ett tal utan väg får inte stå oförklarat", () => {
    // design-reviewer Major 4: the watch row explains this same absence per row; the summary
    // said nothing at all, so the number just stood there with no route and no reason.
    render(
      <CompanySummary
        watches={ok([
          watch({ organizationNumber: "5566524301", activeAdCount: 100 }),
          watch({
            id: "33333333-3333-3333-3333-333333333333",
            organizationNumber: null,
            isProtectedIdentity: true,
            activeAdCount: 36,
          }),
        ])}
      linkHref="/foretag/bevakade"
      />,
    );

    expect(
      screen.getByText(
        "1 bevakning kan inte visas som en lista, så antalen ovan saknar länk.",
      ),
    ).toBeInTheDocument();
  });

  it("noll annonser: ingen länk skulle ändå ha renderats, så förklaringen tiger", () => {
    // Parity with the row: an account whose watches have no ads at all is not missing a route.
    // Without this the note would fire on every masked watch, including ones with nothing to link.
    render(
      <CompanySummary
        watches={ok([
          watch({
            organizationNumber: null,
            isProtectedIdentity: true,
            activeAdCount: 0,
            matchingAdCount: 0,
          }),
        ])}
      linkHref="/foretag/bevakade"
      />,
    );

    expect(screen.queryByText(/kan inte visas som en lista/)).toBeNull();
  });

  it("ett org.nr som inte är tio siffror ger varken länk ELLER tyst tal", () => {
    // code-reviewer Major 4: the summary gated on "is the field non-null" while the href builder
    // gated on "is it ten digits". They disagreed, so `everyWatchLinkable` went true, the builder
    // returned null, and the count rendered with no link AND no note -- exactly the state the row
    // closed by construction. Both callers read `isLinkableOrgNr` now.
    //
    // ⚠ Premise: this shape is contract-impossible today. `OrganizationNumber.Create` enforces
    // ten digits and the one other on-wire form is an HMAC token the handler masks to null. The
    // test asserts only that the READ side degrades safely, never that production emits this.
    render(
      <CompanySummary
        watches={ok([
          watch({ organizationNumber: "55665243", activeAdCount: 100 }),
        ])}
      linkHref="/foretag/bevakade"
      />,
    );

    expect(screen.queryByRole("link", { name: /aktiva annonser/ })).toBeNull();
    expect(
      screen.getByText(
        "1 bevakning kan inte visas som en lista, så antalen ovan saknar länk.",
      ),
    ).toBeInTheDocument();
  });

  it("en varumärkesgruppsrad (org.nr null, flaggan FALSK) släcker också länkarna", () => {
    // code-reviewer Minor 9: only the masked arm was pinned. The production comment names TWO
    // rows arriving with `organizationNumber: null`, and this is the other one -- a BRAND_GROUP
    // watch, whose counts are summed over member org.nrs the FE schema never receives. Removing
    // the `w.organizationNumber` half of the gate leaves the masked test green and this one red.
    render(
      <CompanySummary
        watches={ok([
          watch({
            organizationNumber: null,
            isProtectedIdentity: false,
            companyName: "Friday-koncernen",
            activeAdCount: 40,
          }),
        ])}
      linkHref="/foretag/bevakade"
      />,
    );

    expect(screen.queryByRole("link", { name: /aktiva annonser/ })).toBeNull();
    expect(anchorText()).toBe("1 bevakat företag · 40 aktiva annonser");
    // The row is COUNTED as not linkable, not merely dropped from the link. Without this the
    // gate could admit the row and let the href builder return null instead, which renders the
    // same missing link with no explanation beside it — the state design-reviewer Major 4 and
    // code-reviewer Major 4 both name.
    expect(
      screen.getByText(
        "1 bevakning kan inte visas som en lista, så antalen ovan saknar länk.",
      ),
    ).toBeInTheDocument();
  });

  it("EN maskad bevakning räcker för att ingen summa ska länka", () => {
    // Partiellt vore värre än ingenting: den maskade radens annonser saknas i destinationen
    // medan talet bredvid länken fortfarande räknar dem. Det är count/click-divergensen.
    const { container } = render(
      <CompanySummary
        watches={ok([
          watch({ organizationNumber: "5566524301", activeAdCount: 100, matchingAdCount: 7 }),
          watch({
            id: "33333333-3333-3333-3333-333333333333",
            organizationNumber: null,
            isProtectedIdentity: true,
            activeAdCount: 36,
            matchingAdCount: 2,
          }),
        ])}
      linkHref="/foretag/bevakade"
      />,
    );

    expect(screen.queryAllByRole("link", { name: /annonser/ })).toHaveLength(0);
    expect(container.innerHTML).not.toContain("employer=");
    // Talen skrivs ändå ut — de är mätta och sanna, det är bara vägen dit som saknas.
    expect(anchorText()).toBe("2 bevakade företag · 136 aktiva annonser");
  });

  it("renderar aldrig ett företagsNAMN, och ett org.nr bara inuti en länk — aldrig som text", () => {
    // ⚠ Premissen ändrades 2026-08-30. Denna vakt sa tidigare att INGET org.nr får nå Översikt.
    // Klas-direktivet att summorna ska leda direkt till annonserna gör att org.nr nu MÅSTE nå
    // href:en — annars finns ingen destination. Det som står kvar oförändrat är att inget
    // företagsNAMN renderas, och att inget org.nr blir synlig text.
    //
    // Assertionen går mot markup, inte mot text: `textContent` ser inte `href`.
    const { container } = render(
      <CompanySummary
        watches={ok([
          watch({
            companyName: "Friday Väst AB",
            organizationNumber: "5566524301",
            activeAdCount: 100,
            matchingAdCount: 7,
          }),
        ])}
        linkHref="/foretag/bevakade"
      />,
    );

    expect(container.innerHTML).not.toContain("Friday Väst AB");
    expect(container.innerHTML).toContain("employer=5566524301");

    // INGEN ANNANSTANS, mätt: strip every href and no ten-digit sequence may survive anywhere in
    // the markup — not in a text node, and not in `title`, `data-*` or `aria-label` either.
    // `textContent` alone sees none of those attributes, which is the reach this restores.
    const withoutHrefs = container.innerHTML.replace(/href="[^"]*"/g, "");
    expect(withoutHrefs).not.toMatch(/\d{10}/);
  });

  // Komplement till ovanstående: assertionen mäter DOM, och DOM kan inte skilja dagens
  // korrekta form från en framtida `"use client"` överst i modulen — den skulle serialisera
  // hela watch-arrayen (klartext-org.nr + companyName) in i flight-payloaden på varje
  // /oversikt-laddning medan varje DOM-test förblir grönt. Källan är det enda stället där
  // den skillnaden syns.
  it("modulen är en Server Component — ingen use client-direktiv", () => {
    const src = readFileSync(
      "src/components/oversikt/company-summary.tsx",
      "utf8",
    );
    // Bevisar att rätt fil lästes. Utan den här raden skulle en tom eller felaktig
    // läsning göra negationen nedan sann av fel skäl.
    expect(src).toContain("export function CompanySummary");
    expect(src).not.toMatch(/^\s*["']use client["']/m);
  });

  // #1546 (Klas 2026-08-30) — frågan var "skippas Grundmatch?", och svaret fanns bara i
  // watch-filter-dialogen på /foretag/bevakade. Regeln är nu nåbar där talet står.
  it("matchningsraden bär en ?-hjälp som förklarar vad som räknas som matchande", () => {
    render(
      <CompanySummary
        watches={ok([watch({ activeAdCount: 136, matchingAdCount: 9 })])}
        linkHref="/foretag/bevakade"
      />,
    );

    expect(
      screen.getByRole("button", { name: RULE.onlyMatchedHelpAria }),
    ).toBeInTheDocument();
  });

  it("?-hjälpen tiger när matchningen inte är bedömd", () => {
    render(
      <CompanySummary
        watches={ok([watch({ activeAdCount: 136, matchingAdCount: null })])}
        linkHref="/foretag/bevakade"
      />,
    );

    // Hela matchningsraden utelämnas när SSYK-gaten är stängd. Hjälpen får inte överleva den —
    // en förklaring till ett tal som inte står där förklarar ingenting.
    expect(document.querySelector(".jp-matchline")).toBeNull();
    expect(
      screen.queryByRole("button", { name: RULE.onlyMatchedHelpAria }),
    ).not.toBeInTheDocument();
  });

  // Poängen med att läsa ur en främmande namnrymd är att texten är EN, inte två. Det här är
  // testet som mäter det: Översikt visar watch-dialogens EGNA strängar. Forkas copyn faller det.
  it("regeltexten är watch-dialogens egen sträng, aldrig en kopia", async () => {
    render(
      <CompanySummary
        watches={ok([watch({ activeAdCount: 136, matchingAdCount: 9 })])}
        linkHref="/foretag/bevakade"
      />,
    );

    fireEvent.click(
      screen.getByRole("button", { name: RULE.onlyMatchedHelpAria }),
    );
    await waitFor(() => expect(screen.getByRole("dialog")).toBeInTheDocument());

    expect(screen.getByText(RULE.onlyMatchedHelpTitle)).toBeInTheDocument();
    expect(screen.getByText(RULE.onlyMatchedHelpBody1)).toBeInTheDocument();
    expect(screen.getByText(RULE.onlyMatchedHelpBody2)).toBeInTheDocument();
  });
});
