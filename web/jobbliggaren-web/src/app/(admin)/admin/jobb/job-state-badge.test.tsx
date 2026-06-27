import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { JobStateBadge } from "./job-state-badge";

describe("JobStateBadge", () => {
  it("renders the Swedish 'Lyckades' label for Succeeded", () => {
    render(<JobStateBadge state="Succeeded" />);
    expect(screen.getByText("Lyckades")).toBeInTheDocument();
  });

  it("renders the Swedish 'Misslyckades' label for Failed", () => {
    render(<JobStateBadge state="Failed" />);
    expect(screen.getByText("Misslyckades")).toBeInTheDocument();
  });

  it("renders the Swedish 'Pågår' label for Processing", () => {
    render(<JobStateBadge state="Processing" />);
    expect(screen.getByText("Pågår")).toBeInTheDocument();
  });

  it("renders 'Aldrig körd' for a null state (never run)", () => {
    render(<JobStateBadge state={null} />);
    expect(screen.getByText("Aldrig körd")).toBeInTheDocument();
  });

  it("falls back to 'Aldrig körd' for an unknown state rather than echoing it raw", () => {
    render(<JobStateBadge state="Scheduled" />);
    expect(screen.getByText("Aldrig körd")).toBeInTheDocument();
    expect(screen.queryByText("Scheduled")).not.toBeInTheDocument();
  });

  it("uses semantic color tokens per state (not color-only — label carries meaning)", () => {
    const { rerender } = render(<JobStateBadge state="Succeeded" />);
    expect(screen.getByText("Lyckades").className).toContain("text-success-700");
    rerender(<JobStateBadge state="Failed" />);
    expect(screen.getByText("Misslyckades").className).toContain(
      "text-danger-700",
    );
  });

  it("does not use role=status (would announce N times on a table)", () => {
    const { container } = render(<JobStateBadge state="Succeeded" />);
    expect(container.querySelector("[role='status']")).toBeNull();
  });

  it("applies a passed className", () => {
    render(<JobStateBadge state="Succeeded" className="extra-class" />);
    expect(screen.getByText("Lyckades").className).toContain("extra-class");
  });
});
