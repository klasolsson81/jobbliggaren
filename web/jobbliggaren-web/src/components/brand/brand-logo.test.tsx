import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { BrandLogo } from "./brand-logo";

describe("BrandLogo", () => {
  it("renderar mark + wordmark som default (variant=full)", () => {
    const { container, getByText } = render(<BrandLogo />);
    const svg = container.querySelector("svg.jp-brand__mark");
    expect(svg).not.toBeNull();
    expect(svg!.getAttribute("aria-hidden")).toBe("true");
    expect(getByText("Jobbliggaren")).not.toBeNull();
  });

  it("renderar bara mark vid variant=mark + sätter aria-label", () => {
    const { container, queryByText } = render(<BrandLogo variant="mark" />);
    const svg = container.querySelector("svg.jp-brand__mark");
    expect(svg).not.toBeNull();
    expect(svg!.getAttribute("aria-label")).toBe("Jobbliggaren");
    expect(svg!.getAttribute("aria-hidden")).toBeNull();
    expect(queryByText("Jobbliggaren")).toBeNull();
  });

  it("respekterar markSize-prop på SVG-dimensioner", () => {
    const { container } = render(<BrandLogo markSize={48} />);
    const svg = container.querySelector("svg.jp-brand__mark");
    expect(svg!.getAttribute("width")).toBe("48");
    expect(svg!.getAttribute("height")).toBe("48");
  });

  it("har default markSize=32", () => {
    const { container } = render(<BrandLogo />);
    const svg = container.querySelector("svg.jp-brand__mark");
    expect(svg!.getAttribute("width")).toBe("32");
  });

  it("innehåller 4 compass-diamonds + 1 center-dot", () => {
    const { container } = render(<BrandLogo variant="mark" />);
    expect(container.querySelectorAll("polygon").length).toBe(4);
    expect(container.querySelectorAll("circle").length).toBe(1);
  });
});
