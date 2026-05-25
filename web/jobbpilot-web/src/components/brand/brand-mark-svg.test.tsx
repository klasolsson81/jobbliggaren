import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { BrandMarkSvg } from "./brand-mark-svg";

describe("BrandMarkSvg", () => {
  it("renderar 4 polygons + 1 circle (compass-stjärna)", () => {
    const { container } = render(
      <BrandMarkSvg width={32} height={32} primaryFill="#0A2647" accentFill="#FFCD00" />
    );
    expect(container.querySelectorAll("polygon").length).toBe(4);
    expect(container.querySelectorAll("circle").length).toBe(1);
  });

  it("applicerar primaryFill på alla 4 polygons", () => {
    const { container } = render(
      <BrandMarkSvg width={32} height={32} primaryFill="currentColor" accentFill="#FFCD00" />
    );
    container.querySelectorAll("polygon").forEach((p) => {
      expect(p.getAttribute("fill")).toBe("currentColor");
    });
  });

  it("applicerar accentFill på center-circle", () => {
    const { container } = render(
      <BrandMarkSvg width={32} height={32} primaryFill="#0A2647" accentFill="var(--jp-brand-accent)" />
    );
    expect(container.querySelector("circle")!.getAttribute("fill")).toBe(
      "var(--jp-brand-accent)"
    );
  });

  it("har konstant viewBox 0 0 100 100", () => {
    const { container } = render(
      <BrandMarkSvg width={180} height={180} primaryFill="#FFFFFF" accentFill="#FFCD00" />
    );
    expect(container.querySelector("svg")!.getAttribute("viewBox")).toBe("0 0 100 100");
  });

  it("propagerar width/height", () => {
    const { container } = render(
      <BrandMarkSvg width={240} height={240} primaryFill="#0A2647" accentFill="#FFCD00" />
    );
    const svg = container.querySelector("svg")!;
    expect(svg.getAttribute("width")).toBe("240");
    expect(svg.getAttribute("height")).toBe("240");
  });

  it("propagerar className + ariaLabel + ariaHidden", () => {
    const { container } = render(
      <BrandMarkSvg
        width={32}
        height={32}
        primaryFill="#0A2647"
        accentFill="#FFCD00"
        className="jp-brand__mark"
        ariaLabel="JobbPilot"
        ariaHidden={false}
      />
    );
    const svg = container.querySelector("svg")!;
    expect(svg.getAttribute("class")).toBe("jp-brand__mark");
    expect(svg.getAttribute("aria-label")).toBe("JobbPilot");
    expect(svg.getAttribute("aria-hidden")).toBe("false");
  });

  it("center-circle har cx=50 cy=50 r=5 (geometri-lock)", () => {
    const { container } = render(
      <BrandMarkSvg width={32} height={32} primaryFill="#0A2647" accentFill="#FFCD00" />
    );
    const circle = container.querySelector("circle")!;
    expect(circle.getAttribute("cx")).toBe("50");
    expect(circle.getAttribute("cy")).toBe("50");
    expect(circle.getAttribute("r")).toBe("5");
  });
});
