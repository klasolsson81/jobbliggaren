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

  it("renders a visible 'Söker bland annonser…' message inside the status live-region", () => {
    render(<JobAdListSkeleton />);
    // Synlig DOM-text — seende användare ser exakt samma signal som
    // aria-live="polite" annonserar via live-region-uppdateringen när
    // statusen mountas. Ingen sr-only-divergens.
    const text = screen.getByText("Söker bland annonser…");
    expect(text).toBeInTheDocument();
    // Texten ligger inuti role="status" så live-region:en når den.
    const status = screen.getByRole("status");
    expect(status).toContainElement(text);
    // ARIA 1.2 — role=status har `nameFrom: author`: aria-label/aria-labelledby
    // skulle ÖVERSKUGGA den synliga texten i accessible-name-beräkningen.
    // Vi vill att SR läser exakt den synliga texten via live-regionen — alltså
    // INGEN aria-label/aria-labelledby.
    expect(status).not.toHaveAttribute("aria-label");
    expect(status).not.toHaveAttribute("aria-labelledby");
  });

  it("renders no global id (safe to render multiple times)", () => {
    const { container } = render(<JobAdListSkeleton />);
    expect(container.querySelector("[id]")).toBeNull();
  });

  it("renders six skeleton rows", () => {
    const { container } = render(<JobAdListSkeleton />);
    expect(container.querySelectorAll(".jp-job-skeleton")).toHaveLength(6);
  });

  it("renders the toolbar row with visible status text and a sort placeholder", () => {
    const { container } = render(<JobAdListSkeleton />);
    // M1: toolbaren ligger innanför Suspense-gränsen — raden måste finnas
    // i skeleton:en så resultat-ytan inte hoppar när data landar.
    const toolbar = container.querySelector(".jp-results-toolbar");
    expect(toolbar).not.toBeNull();
    // Vänster slot: synlig "Söker…"-text där träffräknaren landar.
    expect(toolbar?.querySelector(".jp-skeleton__status-text")).not.toBeNull();
    // Höger slot: sort-platshållare som speglar select:ens mått.
    expect(toolbar?.querySelector(".jp-skeleton--sort")).not.toBeNull();
  });

  it("hides only the decorative skeleton blocks from assistive tech", () => {
    const { container } = render(<JobAdListSkeleton />);
    // Skeleton-listan ska inte läsas upp som tomma element.
    expect(container.querySelector("ul")).toHaveAttribute(
      "aria-hidden",
      "true"
    );
    // Sort-platshållaren är rent dekorativ.
    expect(container.querySelector(".jp-skeleton--sort")).toHaveAttribute(
      "aria-hidden",
      "true"
    );
    // Toolbaren själv är INTE aria-hidden längre — den innehåller den
    // synliga statustexten som role="status" måste kunna läsa.
    expect(container.querySelector(".jp-results-toolbar")).not.toHaveAttribute(
      "aria-hidden"
    );
  });
});
