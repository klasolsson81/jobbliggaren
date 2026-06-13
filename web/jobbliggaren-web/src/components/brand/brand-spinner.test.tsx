import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { BrandSpinner } from "./brand-spinner";

describe("BrandSpinner", () => {
  it("exponerar role=status (aria-live polite) med default-label 'Laddar'", () => {
    const { getByRole } = render(<BrandSpinner />);
    const status = getByRole("status");
    expect(status).not.toBeNull();
    expect(status.getAttribute("aria-live")).toBe("polite");
    expect(status.textContent).toBe("Laddar");
  });

  it("respekterar custom label", () => {
    const { getByRole } = render(<BrandSpinner label="Hämtar annonser" />);
    expect(getByRole("status").textContent).toBe("Hämtar annonser");
  });

  it("SVG är aria-hidden och har sigill-geometri (3 circles + 3 rects + 1 path)", () => {
    const { container } = render(<BrandSpinner />);
    const svg = container.querySelector("svg.jp-brand-spinner")!;
    expect(svg.getAttribute("aria-hidden")).toBe("true");
    expect(svg.querySelectorAll("circle").length).toBe(3);
    expect(svg.querySelectorAll("rect").length).toBe(3);
    expect(svg.querySelectorAll("path").length).toBe(1);
  });

  it("roterande guldbåge har __arc-klass + dash-array", () => {
    const { container } = render(<BrandSpinner />);
    const arc = container.querySelector(".jp-brand-spinner__arc")!;
    expect(arc).not.toBeNull();
    expect(arc.getAttribute("stroke-dasharray")).toBe("58 162");
  });

  it("alla tre raderna har pulse-klassen (staggrad via --2/--3)", () => {
    const { container } = render(<BrandSpinner />);
    expect(container.querySelectorAll(".jp-brand-spinner__row").length).toBe(3);
    expect(container.querySelectorAll(".jp-brand-spinner__row--2").length).toBe(1);
    expect(container.querySelectorAll(".jp-brand-spinner__row--3").length).toBe(1);
  });

  it("default-storlek 48, propagerar size", () => {
    const { container, rerender } = render(<BrandSpinner />);
    expect(container.querySelector("svg")!.getAttribute("width")).toBe("48");
    rerender(<BrandSpinner size={64} />);
    expect(container.querySelector("svg")!.getAttribute("width")).toBe("64");
  });
});
