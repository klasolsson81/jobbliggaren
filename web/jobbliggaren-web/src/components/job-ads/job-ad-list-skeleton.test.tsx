import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { Announcer } from "@/components/common/announcer";
import { JobAdListSkeleton } from "./job-ad-list-skeleton";

const region = (c: HTMLElement) => c.querySelector('p[role="status"]');

describe("JobAdListSkeleton", () => {
  it("keeps the visible sentence but carries NO live region of its own", () => {
    const { container } = render(<JobAdListSkeleton />);

    expect(screen.getByText("Söker bland annonser…")).toBeInTheDocument();
    // #1505 — the regression this guards: restoring `role="status"` here would re-create the
    // unreliable shape AND double the announcement once the surface region is in place.
    expect(container.querySelector('[role="status"]')).toBeNull();
    expect(container.querySelector("[aria-live]")).toBeNull();
    // `aria-busy` describes this subtree's own state and is unrelated to the announcement.
    expect(container.querySelector('[aria-busy="true"]')).not.toBeNull();
  });

  it("puts that same sentence into the surface region when hosted by one", () => {
    const { container } = render(
      <Announcer>
        <JobAdListSkeleton />
      </Announcer>,
    );

    expect(region(container)).toHaveTextContent("Söker bland annonser…");
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
    // Toolbaren själv är INTE aria-hidden — den bär den synliga "Söker…"-texten,
    // som är vanligt innehåll och ska nå en skärmläsare som sådant.
    expect(container.querySelector(".jp-results-toolbar")).not.toHaveAttribute(
      "aria-hidden"
    );
  });
});
