import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CompanyWatchList } from "./company-watch-list";
import type { CompanyWatch } from "@/lib/dto/company-follows";
import type { TaxonomyRegion } from "@/lib/dto/taxonomy";

// The row's unfollow AND the F4b filter dialog are "use server" actions pulling in next/cache +
// server-only; stub the module so the client row imports cleanly and the clicks are observable.
const unfollowActionMock = vi.fn();
const setWatchFilterActionMock = vi.fn();
vi.mock("@/lib/actions/company-follows", () => ({
  unfollowCompanyAction: (...args: unknown[]) => unfollowActionMock(...args),
  setWatchFilterAction: (...args: unknown[]) => setWatchFilterActionMock(...args),
}));

// F4b: the row threads the taxonomy down to the filter dialog's ort picker. Stockholms län holds TWO
// kommuner — that is what makes the ort-count semantics falsifiable (a whole-län pick is ONE ort, not
// two).
const regions: ReadonlyArray<TaxonomyRegion> = [
  {
    conceptId: "r_sthlm",
    label: "Stockholms län",
    municipalities: [
      { conceptId: "m_sthlm", label: "Stockholm" },
      { conceptId: "m_solna", label: "Solna" },
    ],
  },
  {
    conceptId: "r_vg",
    label: "Västra Götalands län",
    municipalities: [{ conceptId: "m_gbg", label: "Göteborg" }],
  },
];

const legalEntity: CompanyWatch = {
  id: "11111111-1111-1111-1111-111111111111",
  organizationNumber: "5592804784",
  isProtectedIdentity: false,
  companyName: "Skatteverket",
  followedAt: "2026-06-14T08:00:00+00:00",
  activeAdCount: 3,
  matchingAdCount: 2,
  // F4b — null = no filter (the domain's canonical NULL; absence is the only "no filter" signal).
  filter: null,
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
  filter: null,
};

function renderList(items: ReadonlyArray<CompanyWatch>) {
  return render(<CompanyWatchList items={items} regions={regions} />);
}

describe("CompanyWatchList (#311 #448, ADR 0087 D2/D8(c))", () => {
  beforeEach(() => {
    unfollowActionMock.mockReset();
    setWatchFilterActionMock.mockReset();
  });

  it("tom lista → honest civic nollstate-copy, ingen lista renderas", () => {
    renderList([]);
    expect(screen.getByText("Du bevakar inga företag än")).toBeInTheDocument();
    expect(screen.queryByRole("list")).toBeNull();
  });

  it("legal-entity → namn + formaterat org.nr (NNNNNN-NNNN) + aktiv-räknare + bevakad-sedan", () => {
    renderList([legalEntity]);
    expect(
      screen.getByRole("heading", { name: "Skatteverket" })
    ).toBeInTheDocument();
    expect(screen.getByText("Org.nr 559280-4784")).toBeInTheDocument();
    expect(screen.getByText("3 aktiva annonser just nu")).toBeInTheDocument();
    // Locale-aware short date (next-intl `month: "short"`): sv June → "juni".
    expect(screen.getByText("Bevakad sedan 14 juni 2026")).toBeInTheDocument();
  });

  it("aktiv-räknare plural: 1 → 'aktiv annons', 0 → 'Inga aktiva annonser'", () => {
    const { rerender } = renderList([{ ...legalEntity, activeAdCount: 1 }]);
    expect(screen.getByText("1 aktiv annons just nu")).toBeInTheDocument();
    rerender(
      <CompanyWatchList
        items={[{ ...legalEntity, activeAdCount: 0 }]}
        regions={regions}
      />
    );
    expect(screen.getByText("Inga aktiva annonser just nu")).toBeInTheDocument();
  });

  it("skyddad identitet → flagga visas, INGET org.nr renderas (§12 / D8(c))", () => {
    const { container } = renderList([soleProp]);
    expect(screen.getByText("Skyddad identitet")).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: "Anna Andersson Konsult" })
    ).toBeInTheDocument();
    // Inget "Org.nr"-prefix, och ingen 10-siffrig org.nr/personnummer-sekvens i DOM.
    expect(screen.queryByText(/Org\.nr/)).toBeNull();
    expect(container.textContent ?? "").not.toMatch(/\d{6}-?\d{4}/);
  });

  it("companyName null → civic fallback-namn (aldrig tom rubrik)", () => {
    renderList([{ ...legalEntity, companyName: null }]);
    expect(
      screen.getByText("Företagets namn är inte tillgängligt")
    ).toBeInTheDocument();
  });

  it("unfollow-knapp → anropar unfollowCompanyAction med CompanyWatchId (opak, aldrig org.nr)", async () => {
    unfollowActionMock.mockResolvedValue({ success: true });
    renderList([legalEntity]);
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
    renderList([legalEntity]);
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
    renderList([legalEntity, soleProp]);
    const headings = screen.getAllByRole("heading", { level: 3 });
    expect(headings[0]).toHaveTextContent("Skatteverket");
    expect(headings[1]).toHaveTextContent("Anna Andersson Konsult");
  });
});

describe("CompanyWatchList — matchande-annons-räknare + vy-toggle (#452)", () => {
  beforeEach(() => {
    unfollowActionMock.mockReset();
    setWatchFilterActionMock.mockReset();
  });

  it("default = matchande vyn: visar matchande-räknaren OCH behåller aktiv-räknaren", () => {
    renderList([legalEntity]);
    expect(screen.getByText("2 matchande annonser just nu")).toBeInTheDocument();
    // #447/#448-raden lever kvar som sekundärt faktum oavsett vy-läge.
    expect(screen.getByText("3 aktiva annonser just nu")).toBeInTheDocument();
  });

  it("matchande-räknare plural: 1 → 'matchande annons', 0 → 'Inga matchande annonser just nu'", () => {
    const { rerender } = renderList([{ ...legalEntity, matchingAdCount: 1 }]);
    expect(screen.getByText("1 matchande annons just nu")).toBeInTheDocument();
    rerender(
      <CompanyWatchList
        items={[{ ...legalEntity, matchingAdCount: 0 }]}
        regions={regions}
      />
    );
    expect(
      screen.getByText("Inga matchande annonser just nu")
    ).toBeInTheDocument();
  });

  it("matchingAdCount null → ärlig nudge, ALDRIG '0 matchande' (ej-bedömt-regeln)", () => {
    renderList([{ ...legalEntity, matchingAdCount: null }]);
    expect(
      screen.getByText(
        "Du har inte angett vilka yrken du söker inom. Ställ in det för att se matchande annonser."
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
    renderList([legalEntity]);
    const user = userEvent.setup();

    // Default matchande-vy: matchande-signalen syns.
    expect(screen.getByText("2 matchande annonser just nu")).toBeInTheDocument();

    await user.click(screen.getByRole("radio", { name: "Alla annonser" }));
    expect(screen.queryByText("2 matchande annonser just nu")).toBeNull();
    expect(screen.getByText("3 aktiva annonser just nu")).toBeInTheDocument();

    // Tillbaka till matchande-vyn.
    await user.click(screen.getByRole("radio", { name: "Matchande" }));
    expect(screen.getByText("2 matchande annonser just nu")).toBeInTheDocument();
  });

  it("toggle döljer även nudgen i 'Alla annonser'-läget (nudge tillhör matchande-vyn)", async () => {
    renderList([{ ...legalEntity, matchingAdCount: null }]);
    const user = userEvent.setup();

    expect(
      screen.getByText(
        "Du har inte angett vilka yrken du söker inom. Ställ in det för att se matchande annonser."
      )
    ).toBeInTheDocument();

    await user.click(screen.getByRole("radio", { name: "Alla annonser" }));
    expect(
      screen.queryByText(
        "Du har inte angett vilka yrken du söker inom. Ställ in det för att se matchande annonser."
      )
    ).toBeNull();
    expect(screen.getByText("3 aktiva annonser just nu")).toBeInTheDocument();
  });

  it("vy-toggle = keyboard-operabel radiogroup med tydlig grupp-label", () => {
    renderList([legalEntity]);
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

/**
 * Bevakning F4b (#803) — the RESTING-state filter disclosure (BC-9′).
 *
 * Non-descopable, a security-auditor build-gate. An active filter narrows this watch's notifications
 * AND the Översikt rail count, while the row's own counts stay deliberately filter-UNaware (RF-8).
 * Worse: when every watch suppresses everything, NO digest email is sent at all — so the email cannot
 * disclose anything either, and silence is indistinguishable from "nothing was published". This row is
 * the only surface that can carry the transparency guarantee in that case, which is why the disclosure
 * must be readable WITHOUT opening anything.
 */
describe("CompanyWatchList — vilande filter-disclosure (BC-9′)", () => {
  beforeEach(() => {
    unfollowActionMock.mockReset();
    setWatchFilterActionMock.mockReset();
  });

  const filtered = (filter: CompanyWatch["filter"]): CompanyWatch => ({
    ...legalEntity,
    filter,
  });

  it("endast matchande → 'Filtrerat: endast matchande annonser'", () => {
    renderList([filtered({ municipalities: [], regions: [], onlyMatched: true })]);

    expect(
      screen.getByText("Filtrerat: endast matchande annonser")
    ).toBeInTheDocument();
  });

  it("N orter → 'Filtrerat: N orter' (plural), 1 ort → singular", () => {
    const { rerender } = renderList([
      filtered({
        municipalities: ["m_sthlm", "m_gbg"],
        regions: [],
        onlyMatched: false,
      }),
    ]);
    expect(screen.getByText("Filtrerat: 2 orter")).toBeInTheDocument();

    rerender(
      <CompanyWatchList
        items={[
          filtered({ municipalities: ["m_gbg"], regions: [], onlyMatched: false }),
        ]}
        regions={regions}
      />
    );
    expect(screen.getByText("Filtrerat: 1 ort")).toBeInTheDocument();
  });

  it("båda axlarna → 'Filtrerat: endast matchande annonser, N orter'", () => {
    renderList([
      filtered({
        municipalities: ["m_gbg"],
        regions: ["r_sthlm"],
        onlyMatched: true,
      }),
    ]);

    expect(
      screen.getByText("Filtrerat: endast matchande annonser, 2 orter")
    ).toBeInTheDocument();
  });

  it("ett HELT LÄN räknas som EN ort — aldrig som länets kommuner", () => {
    // The count semantics are load-bearing, not cosmetic. Stockholms län holds two kommuner in the
    // taxonomy the row is handed; counting the län as its municipalities (here 2, in production ~49)
    // would report a selection the user never made. `municipalities.length + regions.length` is the
    // only honest count: a whole-län pick IS one choice, and it is stored as one län concept-id.
    renderList([
      filtered({ municipalities: [], regions: ["r_sthlm"], onlyMatched: false }),
    ]);

    expect(screen.getByText("Filtrerat: 1 ort")).toBeInTheDocument();
    expect(screen.queryByText("Filtrerat: 2 orter")).toBeNull();
  });

  it("OFILTRERAD bevakning → INGEN disclosure alls (ingen 'Inget filter'-rad, ingen tom chip)", () => {
    // Absence is the signal. A row that always said something about filtering would train the user to
    // stop reading the line — and the line is the one thing that must be believed when it appears.
    const { container } = renderList([legalEntity]);

    expect(screen.queryByText(/Filtrerat/)).toBeNull();
    expect(container.textContent ?? "").not.toMatch(/Inget filter/);
    // Inte heller hjälp-triggern för filter-scopet finns på en ofiltrerad rad.
    expect(
      screen.queryByRole("button", {
        name: "Vad filtret för Skatteverket påverkar",
      })
    ).toBeNull();
  });

  it("disclosuren överlever vy-toggeln (vilande tillstånd, inte ett vy-läge)", async () => {
    // The disclosure is not part of the matching view — it is the resting state of the row. If it
    // were gated on the toggle, a user in "Alla annonser" would never learn their notifications are
    // narrowed.
    renderList([filtered({ municipalities: [], regions: [], onlyMatched: true })]);
    const user = userEvent.setup();

    await user.click(screen.getByRole("radio", { name: "Alla annonser" }));

    expect(
      screen.getByText("Filtrerat: endast matchande annonser")
    ).toBeInTheDocument();
  });

  it("disclosuren bär en företags-bärande hjälp-trigger som förklarar att RÄKNARNA inte är filtrerade", async () => {
    // RF-8: the counts answer "does this employer post ads I match?" (a follow DECISION), the filter
    // answers "which of them should notify me". The InfoDialog is the only place that says so.
    renderList([filtered({ municipalities: ["m_gbg"], regions: [], onlyMatched: false })]);
    const user = userEvent.setup();

    await user.click(
      screen.getByRole("button", { name: "Vad filtret för Skatteverket påverkar" })
    );

    const dialog = await screen.findByRole("dialog");
    expect(
      within(dialog).getByText(
        "Antalen i listan (aktiva annonser och matchande annonser) visar alltid företagets alla annonser, oavsett filter."
      )
    ).toBeInTheDocument();
  });

  it("disclosuren läcker aldrig org.nr (maskerad rad förblir maskerad)", () => {
    const { container } = renderList([
      { ...soleProp, filter: { municipalities: ["m_gbg"], regions: [], onlyMatched: true } },
    ]);

    expect(
      screen.getByText("Filtrerat: endast matchande annonser, 1 ort")
    ).toBeInTheDocument();
    expect(container.textContent ?? "").not.toMatch(/\d{6}-?\d{4}/);
  });
});

describe("CompanyWatchList — 'Filtrera'-knappen öppnar filter-dialogen", () => {
  beforeEach(() => {
    unfollowActionMock.mockReset();
    setWatchFilterActionMock.mockReset();
  });

  it("knappen är TEXT med ett företags-bärande tillgängligt namn (aldrig en ikon-gåta)", () => {
    renderList([legalEntity]);

    const button = screen.getByRole("button", {
      name: "Filtrera annonser från Skatteverket",
    });
    // Den synliga etiketten ingår i det tillgängliga namnet (WCAG 2.5.3).
    expect(button).toHaveTextContent("Filtrera");
  });

  it("klick öppnar dialogen förladdad med bevakningens filter", async () => {
    renderList([
      { ...legalEntity, filter: { municipalities: [], regions: [], onlyMatched: true } },
    ]);
    const user = userEvent.setup();

    await user.click(
      screen.getByRole("button", { name: "Filtrera annonser från Skatteverket" })
    );

    const dialog = await screen.findByRole("dialog");
    expect(
      within(dialog).getByText("Filtrera annonser från Skatteverket")
    ).toBeInTheDocument();
    expect(
      within(dialog).getByRole("checkbox", {
        name: "Endast annonser som matchar min profil",
      })
    ).toHaveAttribute("aria-checked", "true");
  });

  it("dialogen är inte monterad förrän knappen klickas", () => {
    renderList([legalEntity]);

    expect(screen.queryByRole("dialog")).toBeNull();
  });
});
