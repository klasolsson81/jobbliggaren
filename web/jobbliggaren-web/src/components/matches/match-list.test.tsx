import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
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
    // nämna BÅDA villkoren — opt-in-kontrollen (av som standard) OCH yrket — så
    // en användare på standardvägen inte tror att hen kvalificerar och väntar
    // förgäves. ADR 0080: konstatera villkoret, värva inte (ingen nudge/banner).
    const emptyBody = screen.getByText(/Bakgrundsmatchningen körs varje natt/);
    expect(emptyBody).toHaveTextContent(/Matchningsnotiser under Inställningar/);
    expect(emptyBody).toHaveTextContent(/avstängt som standard/);
    expect(emptyBody).toHaveTextContent(/angett vilka yrken du söker inom/);
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

  it("isNew=true → 'Ny'-indikator med text (aldrig färg-ensam) + sr-only-kontext, ingen aria-label", () => {
    render(<MatchList items={[{ ...baseItem, isNew: true }]} />);

    // Synlig text "Ny" (färg är aldrig ensam signal, WCAG 1.4.1).
    const badge = screen.getByText("Ny");
    expect(badge).toHaveAttribute("data-tag", "new");
    // #485: aria-label på en generisk <span> är ogiltig → borttagen; den rika
    // kontexten bärs av en sr-only-text.
    expect(badge).not.toHaveAttribute("aria-label");
    expect(
      screen.getByText("Ny matchning sedan ditt senaste besök")
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
    expect(moreLink).toHaveAttribute(
      "href",
      "/jobb?matchGrades=Good&matchGrades=Strong"
    );
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
