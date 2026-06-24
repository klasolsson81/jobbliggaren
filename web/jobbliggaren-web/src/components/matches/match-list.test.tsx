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
  it("tom lista → honest civic nollstate-copy", () => {
    render(<MatchList items={[]} />);

    expect(screen.getByText("Du har inga matchningar än")).toBeInTheDocument();
    expect(screen.getByText(/Bakgrundsmatchningen körs varje natt/)).toBeInTheDocument();
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

  it("isNew=true → 'Ny'-indikator med text (aldrig färg-ensam) + aria-label", () => {
    render(<MatchList items={[{ ...baseItem, isNew: true }]} />);

    const badge = screen.getByText("Ny");
    expect(badge).toBeInTheDocument();
    expect(badge).toHaveAttribute(
      "aria-label",
      "Ny matchning sedan ditt senaste besök"
    );
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
});
