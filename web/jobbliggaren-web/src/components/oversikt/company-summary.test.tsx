import { readFileSync } from "node:fs";
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { CompanySummary } from "./company-summary";
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

    expect(
      screen.getByText("2 bevakade företag · 140 aktiva annonser"),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "Visa bevakade företag" }),
    ).toHaveAttribute("href", "/foretag/bevakade");
  });

  // Defekten issuet stänger: bevakningar med aktiva annonser fick inte läsa som tomma
  // bara för att inget NYTT publicerats sedan besöket.
  it("bevakning med aktiva annonser läser aldrig som tom", () => {
    render(<CompanySummary watches={ok([watch({ activeAdCount: 136 })])} linkHref="/foretag/bevakade" />);

    expect(
      screen.getByText("1 bevakat företag · 136 aktiva annonser"),
    ).toBeInTheDocument();
    expect(screen.queryByText("Du bevakar inga företag än")).toBeNull();
  });

  it("noll aktiva annonser är ett mätt tillstånd, inte ett tomt-läge", () => {
    render(<CompanySummary watches={ok([watch({ activeAdCount: 0 })])} linkHref="/foretag/bevakade" />);

    expect(
      screen.getByText("1 bevakat företag · inga aktiva annonser"),
    ).toBeInTheDocument();
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
      screen.getByText("11 matchande annonser hos dina bevakade företag"),
    ).toBeInTheDocument();

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
    expect(
      screen.getByText("1 bevakat företag · 136 aktiva annonser"),
    ).toBeInTheDocument();
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
    expect(screen.queryByText(/bevakat företag ·/)).toBeNull();
  });

  it("säger att bevakningarna inte kunde hämtas i stället för att påstå noll", () => {
    render(<CompanySummary watches={{ kind: "error" }} linkHref="/foretag/bevakade" />);

    expect(
      screen.getByText(
        "Bevakade företag kunde inte hämtas. Uppdatera sidan för att försöka igen.",
      ),
    ).toBeInTheDocument();
    // Fabrikation: en degraderad hämtning får varken visa en siffra eller tomt-läget.
    expect(screen.queryByText(/bevakade företag ·/)).toBeNull();
    expect(screen.queryByText("Du bevakar inga företag än")).toBeNull();
  });

  // Mätt av security-auditor: `textContent` konkatenerar textnoder och ser INTE `href`,
  // `title`, `aria-label` eller `data-*`. En nod med href="/jobb?employer=5566524301" ger
  // textContent "Visa" och hade passerat — vilket är exakt ADR 0087 D8-gränsen den här
  // PR:en säger sig hålla utanför. Assertionen går därför mot markup, inte mot text, och
  // det generella siffermönstret fångar även ett org.nr denna fixtur inte råkar bära.
  it("renderar varken företagsnamn eller org.nr — i markup, inte bara i text", () => {
    const { container } = render(
      <CompanySummary
        watches={ok([
          watch({ companyName: "Friday Väst AB", organizationNumber: "5566524301" }),
        ])}
        linkHref="/foretag/bevakade"
      />,
    );

    expect(container.innerHTML).not.toContain("Friday Väst AB");
    expect(container.innerHTML).not.toContain("5566524301");
    // Vilken tiosiffrig sekvens som helst: ett org.nr som läckt via en annan väg än
    // fixturens egen sträng ska också falla.
    expect(container.innerHTML).not.toMatch(/\d{10}/);
    expect(container.innerHTML).not.toContain("employer=");
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
});
