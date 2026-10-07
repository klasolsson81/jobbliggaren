import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { MatchList } from "./match-list";
import type { MatchList as MatchListData } from "@/lib/dto/me-matches";

// next/link renders as <a> in jsdom. The i18n provider (messages/sv) is injected
// by the test render shim, so assertions match the Swedish catalog verbatim.

const baseItem: MatchListData[number] = {
  jobAdId: "11111111-1111-1111-1111-111111111111",
  title: "Systemutvecklare",
  company: "Skatteverket",
  url: "https://example.se/ad/1",
  grade: "Strong",
  createdAt: "2026-06-14T08:00:00+00:00",
  isNew: false,
};

describe("MatchList (ADR 0080 Vag 4 PR-5)", () => {
  it("tom lista → honest civic nollstate-copy (#423: BÅDA opt-in-villkoren)", () => {
    render(<MatchList items={[]} />);

    expect(screen.getByText("Du har inga matchningar än")).toBeInTheDocument();
    // #423: copyn får inte påstå att bara ett angivet yrke räcker. Den måste
    // nämna BÅDA villkoren — att matchningen slås på OCH yrket — så en användare
    // på standardvägen inte tror att hen kvalificerar och väntar förgäves.
    // ADR 0080: konstatera villkoret, värva inte (ingen nudge/banner). #1891:
    // varje villkor länkar dit där det uppfylls.
    const emptyBody = screen.getByText(/Matchningen körs varje natt/);
    expect(emptyBody).toHaveTextContent(/när du har slagit på den under Notiser/);
    expect(emptyBody).toHaveTextContent(/angett yrken under Matchning/);
    expect(within(emptyBody).getByRole("link", { name: "Notiser" })).toHaveAttribute(
      "href",
      "/mina-sidor/notiser"
    );
    expect(within(emptyBody).getByRole("link", { name: "Matchning" })).toHaveAttribute(
      "href",
      "/mina-sidor"
    );
    // Ingen lista renderas.
    expect(screen.queryByRole("list")).toBeNull();
  });

  it("lista → titel länkar till /jobb/{id}, företag + grad-chip + datum med år", () => {
    render(<MatchList items={[baseItem]} />);

    const titleLink = screen.getByRole("link", { name: "Systemutvecklare" });
    expect(titleLink).toHaveAttribute("href", "/jobb/11111111-1111-1111-1111-111111111111");

    expect(screen.getByText("Skatteverket")).toBeInTheDocument();
    // Grad-chip = namngiven kategori (Stark match), aldrig en siffra (Goodhart).
    expect(screen.getByText("Stark match")).toBeInTheDocument();
    // Datum "14 jun 2026" (kortform MED år).
    expect(screen.getByText("14 jun 2026")).toBeInTheDocument();
  });

  // #2012 — the card's pointer and hover border promise a click, so the card is the target: the
  // title is the row's one link to the ad, stretched over the card.
  it("makes each title the card's one link to the ad, with no aria-label and no control inside it", () => {
    const { container } = render(<MatchList items={[baseItem]} />);
    expect(container.querySelectorAll('a[href^="/jobb/"]')).toHaveLength(1);
    const link = screen.getByRole("link", { name: "Systemutvecklare" });
    expect(link).toHaveClass("jp-job__rowlink", { exact: true });
    // The modal's focus return re-finds a restored opener by its href and whether it has an
    // aria-label (useInformationModalFocus).
    expect(link).not.toHaveAttribute("aria-label");
    expect(link.querySelector("a, button")).toBeNull();
    const external = screen.getByRole("link", { name: /Öppna annonsen på externa/ });
    expect(link.contains(external)).toBe(false);
    expect(screen.getByRole("article")).toHaveClass("jp-job", { exact: true });
    // The lift above the row link's overlay selects a control only in this shape (globals.css).
    expect(external.parentElement).toHaveClass("jp-job__actions");
    expect(external.parentElement?.parentElement).toBe(screen.getByRole("article"));
  });

  it("describes the link with the new tag, the grade, the company and the date, in that order", () => {
    const { rerender } = render(<MatchList items={[{ ...baseItem, isNew: true }]} />);
    const described = () =>
      (screen.getByRole("link", { name: "Systemutvecklare" }).getAttribute("aria-describedby") ?? "")
        .split(" ")
        .map((id) => document.getElementById(id));

    const withNew = described();
    expect(withNew).toHaveLength(4);
    expect(withNew[0]).toHaveAttribute("data-tag", "new");
    expect(withNew[1]).toHaveTextContent("Stark match");
    expect(withNew[2]).toHaveTextContent("Skatteverket");
    expect(withNew[3]).toHaveClass("jp-job__meta");

    rerender(<MatchList items={[baseItem]} />);
    expect(described().map((part) => part?.textContent)).toEqual([
      "Stark match",
      "Skatteverket",
      "Matchad 14 jun 2026",
    ]);
  });

  it("keeps the date's label and its space in one text node", () => {
    // Chrome drops a whitespace-only node from a computed description ("Matchad14 jun 2026").
    const { container } = render(<MatchList items={[baseItem]} />);
    const span = container.querySelector(".jp-job__meta > span");
    expect(span?.childNodes).toHaveLength(2);
    expect(span?.firstChild?.nodeType).toBe(Node.TEXT_NODE);
    expect(span?.firstChild?.textContent).toMatch(/\S $/);
    expect(span?.lastChild?.nodeName).toBe("B");
  });

  it("isNew=true → 'Ny'-indikator med text (aldrig färg-ensam) + sr-only-kontext, ingen aria-label", () => {
    render(<MatchList items={[{ ...baseItem, isNew: true }]} />);

    // Synlig text "Ny" (färg är aldrig ensam signal, WCAG 1.4.1).
    const badge = screen.getByText("Ny");
    expect(badge).toHaveAttribute("data-tag", "new");
    // #485: aria-label på en generisk <span> är ogiltig → borttagen; den rika
    // kontexten bärs av en sr-only-text.
    expect(badge).not.toHaveAttribute("aria-label");
    expect(
      screen.getByText("matchning sedan ditt senaste besök")
    ).toBeInTheDocument();
  });

  it("isNew=false → ingen 'Ny'-indikator", () => {
    render(<MatchList items={[baseItem]} />);
    expect(screen.queryByText("Ny")).toBeNull();
  });

  it("url present → extern länk-knapp; url null → ingen extern länk", () => {
    const { rerender } = render(<MatchList items={[baseItem]} />);
    expect(
      screen.getByRole("link", { name: /Öppna annonsen på externa/ })
    ).toHaveAttribute("href", "https://example.se/ad/1");

    rerender(<MatchList items={[{ ...baseItem, url: null }]} />);
    expect(
      screen.queryByRole("link", { name: /Öppna annonsen på externa/ })
    ).toBeNull();
  });

  it("grad-chip yttar aldrig en siffra/procent (Goodhart-vakt)", () => {
    const { container } = render(
      <MatchList items={[{ ...baseItem, grade: "Top" }]} />
    );
    expect(screen.getByText("Toppmatch")).toBeInTheDocument();
    // Inga procent-tecken i chip-ytan.
    expect(container.textContent ?? "").not.toContain("%");
  });

  it("nyast först-ordning bevaras (komponenten renderar i mottagen ordning)", () => {
    const older = { ...baseItem, jobAdId: "a", title: "Äldre roll" };
    const newer = { ...baseItem, jobAdId: "b", title: "Nyare roll" };
    render(<MatchList items={[newer, older]} />);

    const links = screen.getAllByRole("link", { name: /roll$/ });
    expect(links[0]).toHaveTextContent("Nyare roll");
    expect(links[1]).toHaveTextContent("Äldre roll");
  });

  // #424: the backend caps the list at 50 (#273). A full window must not read as
  // the total — surface the bound + point to the /jobb match filter, honestly.
  it("cap (50 rader) → bounded-window-hint med länk till /jobb-matchningsfiltret", () => {
    const items: MatchListData = Array.from({ length: 50 }, (_, i) => ({
      ...baseItem,
      jobAdId: `id-${i}`,
    }));
    render(<MatchList items={items} />);

    // Konstaterar fönstret (count interpolerad ur FE-konstanten, ingen drift).
    expect(
      screen.getByText("Visar dina 50 senaste matchningar.")
    ).toBeInTheDocument();
    // Länken går till /jobb filtrerat på de FILTRERBARA notifierbara graderna
    // (Good + Strong). Top är honest-by-design ofiltrerbar → aldrig i länken.
    const moreLink = screen.getByRole("link", {
      name: "Hitta fler via matchningsfiltret i jobblistan",
    });
    expect(moreLink).toHaveAttribute("href", "/jobb?matchGrades=Good.Strong");

    // The INVARIANT behind that literal, asserted separately so a future drift
    // back to a handwritten link fails here rather than in a user's session.
    // This link is a page-URL PRODUCER; it used to be handwritten as
    // `?matchGrades=Good&matchGrades=Strong`, and Next's router cache collapses
    // repeated keys to the last value — so its slot was keyed
    // `matchGrades=Strong` while carrying a Good+Strong document, and `<Link>`
    // prefetch poisoned it merely by rendering this list.
    const href = moreLink.getAttribute("href") ?? "";
    const params = new URLSearchParams(href.slice(href.indexOf("?")));
    expect(
      params.getAll("matchGrades"),
      "a repeated axis key here re-introduces the router-cache collision this link's own history is the example of"
    ).toHaveLength(1);
  });

  it("under cap (49 rader) → ingen bounded-window-hint", () => {
    const items: MatchListData = Array.from({ length: 49 }, (_, i) => ({
      ...baseItem,
      jobAdId: `id-${i}`,
    }));
    render(<MatchList items={items} />);

    expect(screen.queryByText(/Visar dina 50 senaste matchningar/)).toBeNull();
    expect(
      screen.queryByRole("link", {
        name: "Hitta fler via matchningsfiltret i jobblistan",
      })
    ).toBeNull();
  });
});
