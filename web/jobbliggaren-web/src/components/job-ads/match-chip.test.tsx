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
    { grade: "Strong", modifier: "jp-matchchip--high", label: "Stark match" },
    { grade: "Good", modifier: "jp-matchchip--mid", label: "Bra match" },
    { grade: "Basic", modifier: "jp-matchchip--low", label: "Grundmatch" },
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
});
