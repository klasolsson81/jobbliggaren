import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { JobAdListSkeleton } from "./job-ad-list-skeleton";

describe("JobAdListSkeleton", () => {
  it("exposes a polite status live-region for screen readers", () => {
    render(<JobAdListSkeleton />);
    const status = screen.getByRole("status");
    expect(status).toHaveAttribute("aria-live", "polite");
    expect(status).toHaveAttribute("aria-busy", "true");
  });

  it("announces a plain, civic-utility loading message via aria-label", () => {
    render(<JobAdListSkeleton />);
    // M2: accessible name sätts via aria-label direkt på status-wrappern
    // — inte via aria-labelledby mot ett separat id-element — så
    // komponenten är fri från DOM-id-kollisionsrisk.
    const status = screen.getByRole("status", {
      name: "Söker bland annonser…",
    });
    expect(status).toBeInTheDocument();
    expect(status).toHaveAttribute("aria-label", "Söker bland annonser…");
    expect(status).not.toHaveAttribute("aria-labelledby");
  });

  it("renders no global id (safe to render multiple times)", () => {
    const { container } = render(<JobAdListSkeleton />);
    // M2: inget hårt `id` får finnas — flera samtidiga instanser annars
    // kolliderar i DOM:en.
    expect(container.querySelector("[id]")).toBeNull();
  });

  it("renders six skeleton rows", () => {
    const { container } = render(<JobAdListSkeleton />);
    expect(container.querySelectorAll(".jp-job-skeleton")).toHaveLength(6);
  });

  it("renders a toolbar placeholder so the layout does not shift", () => {
    const { container } = render(<JobAdListSkeleton />);
    // M1: toolbaren är data-beroende och ligger innanför Suspense-gränsen
    // — skeleton:en speglar toolbar-raden (träffräknare + sortering) så
    // resultat-ytan inte hoppar när data landar.
    const toolbar = container.querySelector(".jp-results-toolbar");
    expect(toolbar).not.toBeNull();
    expect(toolbar?.querySelector(".jp-skeleton--count")).not.toBeNull();
    expect(toolbar?.querySelector(".jp-skeleton--sort")).not.toBeNull();
  });

  it("hides the decorative skeleton blocks from assistive tech", () => {
    const { container } = render(<JobAdListSkeleton />);
    // Skeleton-listan ska inte läsas upp som tomma element — bara den
    // korta status-meningen annonseras.
    expect(container.querySelector("ul")).toHaveAttribute(
      "aria-hidden",
      "true"
    );
    // Toolbar-platshållaren är likaså rent dekorativ.
    expect(container.querySelector(".jp-results-toolbar")).toHaveAttribute(
      "aria-hidden",
      "true"
    );
  });
});
