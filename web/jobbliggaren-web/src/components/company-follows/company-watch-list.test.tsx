import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CompanyWatchList } from "./company-watch-list";
import type { CompanyWatch } from "@/lib/dto/company-follows";

// The row's unfollow is a "use server" action pulling in next/cache + server-only;
// stub the module so the client row imports cleanly and the click is observable.
const unfollowActionMock = vi.fn();
vi.mock("@/lib/actions/company-follows", () => ({
  unfollowCompanyAction: (...args: unknown[]) => unfollowActionMock(...args),
}));

const legalEntity: CompanyWatch = {
  id: "11111111-1111-1111-1111-111111111111",
  organizationNumber: "5592804784",
  isProtectedIdentity: false,
  companyName: "Skatteverket",
  followedAt: "2026-06-14T08:00:00+00:00",
  activeAdCount: 3,
  matchingAdCount: 2,
};

// Sole proprietorship: the backend masks a personnummer-shaped org.nr to null and
// flags it (ADR 0087 D8(c)) — the raw value never reaches the FE.
const soleProp: CompanyWatch = {
  id: "22222222-2222-2222-2222-222222222222",
  organizationNumber: null,
  isProtectedIdentity: true,
  companyName: "Anna Andersson Konsult",
  followedAt: "2026-06-10T08:00:00+00:00",
  activeAdCount: 1,
  matchingAdCount: 0,
};

describe("CompanyWatchList (#311 #448, ADR 0087 D2/D8(c))", () => {
  beforeEach(() => unfollowActionMock.mockReset());

  it("tom lista → honest civic nollstate-copy, ingen lista renderas", () => {
    render(<CompanyWatchList items={[]} />);
    expect(screen.getByText("Du bevakar inga företag än")).toBeInTheDocument();
    expect(screen.queryByRole("list")).toBeNull();
  });

  it("legal-entity → namn + formaterat org.nr (NNNNNN-NNNN) + aktiv-räknare + bevakad-sedan", () => {
    render(<CompanyWatchList items={[legalEntity]} />);
    expect(
      screen.getByRole("heading", { name: "Skatteverket" })
    ).toBeInTheDocument();
    expect(screen.getByText("Org.nr 559280-4784")).toBeInTheDocument();
    expect(screen.getByText("3 aktiva annonser just nu")).toBeInTheDocument();
    // Locale-aware short date (next-intl `month: "short"`): sv June → "juni".
    expect(screen.getByText("Bevakad sedan 14 juni 2026")).toBeInTheDocument();
  });

  it("aktiv-räknare plural: 1 → 'aktiv annons', 0 → 'Inga aktiva annonser'", () => {
    const { rerender } = render(
      <CompanyWatchList items={[{ ...legalEntity, activeAdCount: 1 }]} />
    );
    expect(screen.getByText("1 aktiv annons just nu")).toBeInTheDocument();
    rerender(<CompanyWatchList items={[{ ...legalEntity, activeAdCount: 0 }]} />);
    expect(screen.getByText("Inga aktiva annonser just nu")).toBeInTheDocument();
  });

  it("skyddad identitet → flagga visas, INGET org.nr renderas (§12 / D8(c))", () => {
    const { container } = render(<CompanyWatchList items={[soleProp]} />);
    expect(screen.getByText("Skyddad identitet")).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: "Anna Andersson Konsult" })
    ).toBeInTheDocument();
    // Inget "Org.nr"-prefix, och ingen 10-siffrig org.nr/personnummer-sekvens i DOM.
    expect(screen.queryByText(/Org\.nr/)).toBeNull();
    expect(container.textContent ?? "").not.toMatch(/\d{6}-?\d{4}/);
  });

  it("companyName null → civic fallback-namn (aldrig tom rubrik)", () => {
    render(<CompanyWatchList items={[{ ...legalEntity, companyName: null }]} />);
    expect(
      screen.getByText("Företagets namn är inte tillgängligt")
    ).toBeInTheDocument();
  });

  it("unfollow-knapp → anropar unfollowCompanyAction med CompanyWatchId (opak, aldrig org.nr)", async () => {
    unfollowActionMock.mockResolvedValue({ success: true });
    render(<CompanyWatchList items={[legalEntity]} />);
    const user = userEvent.setup();
    await user.click(
      screen.getByRole("button", { name: "Sluta bevaka Skatteverket" })
    );
    await waitFor(() =>
      expect(unfollowActionMock).toHaveBeenCalledWith(
        "11111111-1111-1111-1111-111111111111"
      )
    );
  });

  it("unfollow misslyckas → fel visas inline, raden stannar (revalidatePath tar bort vid lyckat)", async () => {
    unfollowActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte sluta bevaka företaget. Försök igen.",
    });
    render(<CompanyWatchList items={[legalEntity]} />);
    const user = userEvent.setup();
    await user.click(
      screen.getByRole("button", { name: "Sluta bevaka Skatteverket" })
    );
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Kunde inte sluta bevaka företaget"
    );
    expect(
      screen.getByRole("heading", { name: "Skatteverket" })
    ).toBeInTheDocument();
  });

  it("ordning bevaras (renderar i mottagen ordning, nyast först från backend)", () => {
    render(<CompanyWatchList items={[legalEntity, soleProp]} />);
    const headings = screen.getAllByRole("heading", { level: 3 });
    expect(headings[0]).toHaveTextContent("Skatteverket");
    expect(headings[1]).toHaveTextContent("Anna Andersson Konsult");
  });
});

describe("CompanyWatchList — matchande-annons-räknare + vy-toggle (#452)", () => {
  beforeEach(() => unfollowActionMock.mockReset());

  it("default = matchande vyn: visar matchande-räknaren OCH behåller aktiv-räknaren", () => {
    render(<CompanyWatchList items={[legalEntity]} />);
    expect(screen.getByText("2 matchande annonser")).toBeInTheDocument();
    // #447/#448-raden lever kvar som sekundärt faktum oavsett vy-läge.
    expect(screen.getByText("3 aktiva annonser just nu")).toBeInTheDocument();
  });

  it("matchande-räknare plural: 1 → 'matchande annons', 0 → 'Inga matchande annonser just nu'", () => {
    const { rerender } = render(
      <CompanyWatchList items={[{ ...legalEntity, matchingAdCount: 1 }]} />
    );
    expect(screen.getByText("1 matchande annons")).toBeInTheDocument();
    rerender(
      <CompanyWatchList items={[{ ...legalEntity, matchingAdCount: 0 }]} />
    );
    expect(
      screen.getByText("Inga matchande annonser just nu")
    ).toBeInTheDocument();
  });

  it("matchingAdCount null → ärlig nudge, ALDRIG '0 matchande' (ej-bedömt-regeln)", () => {
    render(
      <CompanyWatchList items={[{ ...legalEntity, matchingAdCount: null }]} />
    );
    expect(
      screen.getByText(
        "Ange vilka yrken du söker för att se matchande annonser."
      )
    ).toBeInTheDocument();
    // Nudge-länken pekar på den kanoniska matchnings-setup-rutten.
    const cta = screen.getByRole("link", { name: "Ställ in matchning" });
    expect(cta).toHaveAttribute("href", "/installningar#matchning");
    // Ingen numerisk matchande-räknare får renderas (varken "0 matchande" eller
    // "Inga matchande annonser just nu"). Nudge-copyn innehåller ordet "matchande
    // annonser" legitimt, så assertionen scopas till den numeriska räknar-formen.
    expect(screen.queryByText(/\d+ matchande annons/)).toBeNull();
    expect(screen.queryByText("Inga matchande annonser just nu")).toBeNull();
  });

  it("toggle 'Alla annonser' → döljer matchande-signalen, visar aktiv-räknaren; 'Matchande' återställer", async () => {
    render(<CompanyWatchList items={[legalEntity]} />);
    const user = userEvent.setup();

    // Default matchande-vy: matchande-signalen syns.
    expect(screen.getByText("2 matchande annonser")).toBeInTheDocument();

    await user.click(screen.getByRole("radio", { name: "Alla annonser" }));
    expect(screen.queryByText("2 matchande annonser")).toBeNull();
    expect(screen.getByText("3 aktiva annonser just nu")).toBeInTheDocument();

    // Tillbaka till matchande-vyn.
    await user.click(screen.getByRole("radio", { name: "Matchande" }));
    expect(screen.getByText("2 matchande annonser")).toBeInTheDocument();
  });

  it("toggle döljer även nudgen i 'Alla annonser'-läget (nudge tillhör matchande-vyn)", async () => {
    render(
      <CompanyWatchList items={[{ ...legalEntity, matchingAdCount: null }]} />
    );
    const user = userEvent.setup();

    expect(
      screen.getByText(
        "Ange vilka yrken du söker för att se matchande annonser."
      )
    ).toBeInTheDocument();

    await user.click(screen.getByRole("radio", { name: "Alla annonser" }));
    expect(
      screen.queryByText(
        "Ange vilka yrken du söker för att se matchande annonser."
      )
    ).toBeNull();
    expect(screen.getByText("3 aktiva annonser just nu")).toBeInTheDocument();
  });

  it("vy-toggle = keyboard-operabel radiogroup med tydlig grupp-label", () => {
    render(<CompanyWatchList items={[legalEntity]} />);
    const group = screen.getByRole("radiogroup", {
      name: "Visa antal annonser",
    });
    expect(group).toBeInTheDocument();
    // Aktivt alternativ är i tab-ordningen (roving tabindex), det andra utanför.
    expect(screen.getByRole("radio", { name: "Matchande" })).toHaveAttribute(
      "aria-checked",
      "true"
    );
  });
});
