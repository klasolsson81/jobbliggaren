import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { MatchChip } from "./match-chip";
import type { MatchGrade } from "@/lib/dto/job-ad-match";

describe("MatchChip (F4-13 graderad match-tagg)", () => {
  // Grad → modifier + svensk label-kontrakt (design-reviewer Form A).
  const cases: ReadonlyArray<{
    grade: MatchGrade;
    modifier: string;
    label: string;
  }> = [
    // F4-16 (Klas-bind) — golden-rungen: "Toppmatch" + --top (djupare solid grön).
    { grade: "Top", modifier: "jp-matchchip--top", label: "Toppmatch" },
    { grade: "Strong", modifier: "jp-matchchip--high", label: "Stark match" },
    { grade: "Good", modifier: "jp-matchchip--mid", label: "Bra match" },
    { grade: "Basic", modifier: "jp-matchchip--low", label: "Grundmatch" },
    // #300 PR-5 (ADR 0084) — Related = neutral chip (--related), INTE ett femte
    // grönt blad-steg. Egen modifier + svensk label "Relaterat yrke".
    {
      grade: "Related",
      modifier: "jp-matchchip--related",
      label: "Relaterat yrke",
    },
  ];

  for (const { grade, modifier, label } of cases) {
    it(`renders ${grade} → ${modifier} med label "${label}"`, () => {
      const { container } = render(<MatchChip grade={grade} />);
      const chip = container.querySelector(".jp-matchchip");
      expect(chip).not.toBeNull();
      expect(chip).toHaveClass(modifier);
      expect(chip).toHaveTextContent(label);
    });
  }

  it("renders a decorative dot that is hidden from screen readers (a11y)", () => {
    const { container } = render(<MatchChip grade="Strong" />);
    const dot = container.querySelector(".jp-matchchip__dot");
    expect(dot).not.toBeNull();
    // 1.4.1: pricken upprepar bara graden — namnet bärs av den synliga texten,
    // pricken är aria-hidden så betydelse aldrig vilar på färg ensam.
    expect(dot).toHaveAttribute("aria-hidden", "true");
  });

  it("exposes the visible label as the accessible name (no number, no percent)", () => {
    const { container } = render(<MatchChip grade="Good" />);
    const chip = container.querySelector(".jp-matchchip");
    // Goodhart-vakt: ingen siffra/procent någonstans i den renderade chip:en.
    expect(chip?.textContent).toBe("Bra match");
    expect(chip?.textContent).not.toMatch(/\d/);
  });

  it("golden-rungen (Top) renderar 'Toppmatch' + --top utan siffra (F4-16)", () => {
    const { container } = render(<MatchChip grade="Top" />);
    const chip = container.querySelector(".jp-matchchip");
    expect(chip).toHaveClass("jp-matchchip--top");
    expect(chip?.textContent).toBe("Toppmatch");
    // Goodhart-vakt håller även på golden-rungen.
    expect(chip?.textContent).not.toMatch(/\d/);
  });

  it("Related renderar den NEUTRALA chip:en (--related), INTE ett grönt blad-steg (#300 PR-5)", () => {
    const { container } = render(<MatchChip grade="Related" />);
    const chip = container.querySelector(".jp-matchchip");
    expect(chip).toHaveClass("jp-matchchip--related");
    // Designkontrakt (design-reviewer bind): Related är INTE en femte grön
    // fyllning — den bär aldrig någon av leaf-ramp-modifierarna.
    expect(chip).not.toHaveClass("jp-matchchip--top");
    expect(chip).not.toHaveClass("jp-matchchip--high");
    expect(chip).not.toHaveClass("jp-matchchip--mid");
    expect(chip).not.toHaveClass("jp-matchchip--low");
    expect(chip?.textContent).toBe("Relaterat yrke");
    // Goodhart-vakt håller även på related-rungen.
    expect(chip?.textContent).not.toMatch(/\d/);
  });

  // #379 (CTO bind) — explainability touch: ENBART Related-chip:en bär en
  // supplementär hint (samma reason-copy som modalen visar) så "Relaterat yrke"
  // blir begripligt på kortet utan att den synliga labeln ändras. De fyra gröna
  // graderna är självförklarande och bär ingen title.
  it("Related-chip:en bär en förklarande title (reason-copy), de gröna graderna gör det inte (#379)", () => {
    const { container: related } = render(<MatchChip grade="Related" />);
    const relatedChip = related.querySelector(".jp-matchchip");
    expect(relatedChip).toHaveAttribute(
      "title",
      "Liknande yrke, inte ett du valt. Därför rankas annonsen under dina exakta träffar.",
    );
    // Den synliga labeln är oförändrad (title är supplementär, ej det tillgängliga namnet).
    expect(relatedChip?.textContent).toBe("Relaterat yrke");

    for (const grade of ["Top", "Strong", "Good", "Basic"] as const) {
      const { container } = render(<MatchChip grade={grade} />);
      const chip = container.querySelector(".jp-matchchip");
      expect(chip).not.toHaveAttribute("title");
    }
  });
});
