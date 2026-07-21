import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { JobTags } from "./job-tags";

describe("JobTags (NY = oläst, #293/#306)", () => {
  it("renders NY when isNew=true", () => {
    render(<JobTags isNew={true} />);
    expect(screen.getByText("Ny")).toBeInTheDocument();
  });

  it("does not render NY when isNew=false (oläst-watermark/kall start)", () => {
    render(<JobTags isNew={false} />);
    expect(screen.queryByText("Ny")).not.toBeInTheDocument();
  });

  // #293/#306 + #485 — a11y-paritet med /matchningar: NY-taggen bär full
  // skärmläsar-kontext via en sr-only-text (aria-label är ogiltig på en generisk
  // <span>/role=generic). Färg är aldrig ensam signal (WCAG 1.4.1) — synlig text
  // "Ny" plus sr-only-kontexten bär betydelsen.
  it("NY-taggen bär skärmläsar-kontext via sr-only text, inte aria-label", () => {
    render(<JobTags isNew={true} />);
    const ny = screen.getByText("Ny");
    expect(ny).toHaveAttribute("data-tag", "new");
    expect(ny).not.toHaveAttribute("aria-label");
    expect(
      screen.getByText("Nytt jobb sedan ditt senaste besök"),
    ).toBeInTheDocument();
  });

  // F4-13 (ADR 0076) — den numeriska matchScore/MATCH_THRESHOLD-taggen är
  // borttagen (Goodhart-förbud). Match-graden renderas nu av MatchChip i
  // JobAdCard, inte här; JobTags känner inte längre till match alls.
  it("does not render a match tag (numeric matchScore-modellen borttagen)", () => {
    const { container } = render(
      <JobTags isNew={true} isSaved={true} isApplied={true} />,
    );
    expect(screen.queryByText("Bra match")).not.toBeInTheDocument();
    expect(container.querySelector('[data-tag="match"]')).toBeNull();
  });

  // #1000-review (2026-07-21) — "5 DAGAR"-färskhetstaggen BORTTAGEN (dublett av
  // meta-radens exakta `Publicerad <datum>`; senior-cto-advisor). Regressionsvakt:
  // ingen freshness-tagg renderas längre.
  it("renderar ingen freshness-tagg (borttagen)", () => {
    const { container } = render(<JobTags isNew={true} isSaved={true} />);
    expect(container.querySelector('[data-tag="freshness"]')).toBeNull();
  });

  it("renders nothing when all tags are absent (no empty container)", () => {
    const { container } = render(<JobTags isNew={false} />);
    expect(container.querySelector(".jp-job-tags")).toBeNull();
  });

  // PR5 — Sparad + Ansökt-taggar (ADR 0063 per-user-overlay).
  it("renders Sparad-tagg när isSaved=true", () => {
    render(<JobTags isNew={false} isSaved={true} />);
    expect(screen.getByText("Sparad")).toBeInTheDocument();
  });

  it("renders Ansökt-tagg när isApplied=true", () => {
    render(<JobTags isNew={false} isApplied={true} />);
    expect(screen.getByText("Ansökt")).toBeInTheDocument();
  });

  it("renderar inga status-taggar när isSaved=false + isApplied=false (default)", () => {
    const { container } = render(<JobTags isNew={false} />);
    // Inget tagg-block ska renderas alls
    expect(container.querySelector(".jp-job-tags")).toBeNull();
  });

  it("renderar både Sparad och Ansökt vid full status", () => {
    const { container } = render(
      <JobTags isNew={false} isSaved={true} isApplied={true} />,
    );
    const tags = container.querySelectorAll(".jp-tag");
    expect(screen.getByText("Sparad")).toBeInTheDocument();
    expect(screen.getByText("Ansökt")).toBeInTheDocument();
    expect(tags).toHaveLength(2);
  });

  it("renderar taggarna i ordning: NY → Sparad → Ansökt", () => {
    const { container } = render(
      <JobTags isNew={true} isSaved={true} isApplied={true} />,
    );
    const tags = container.querySelectorAll(".jp-tag");
    expect(tags).toHaveLength(3);
    expect(tags[0]).toHaveTextContent("Ny");
    expect(tags[1]).toHaveTextContent("Sparad");
    expect(tags[2]).toHaveTextContent("Ansökt");
  });

  // #1000 (V1) — BEVAKAR = du bevakar annonsens arbetsgivare (--jp-follow-axeln).
  it("renders BEVAKAR when isFollowed=true", () => {
    render(<JobTags isNew={false} isFollowed={true} />);
    const tag = screen.getByText("Bevakar");
    expect(tag).toBeInTheDocument();
    expect(tag).toHaveAttribute("data-tag", "followed");
  });

  it("does not render BEVAKAR when isFollowed=false (anon / not following)", () => {
    render(<JobTags isNew={false} isFollowed={false} />);
    expect(screen.queryByText("Bevakar")).not.toBeInTheDocument();
  });

  it("BEVAKAR ensam räcker för att rendera tagg-blocket (ingen annan tagg)", () => {
    const { container } = render(<JobTags isNew={false} isFollowed={true} />);
    expect(container.querySelector(".jp-job-tags")).not.toBeNull();
  });

  it("renderar alla taggar i ordning: NY → BEVAKAR → Sparad → Ansökt", () => {
    const { container } = render(
      <JobTags isNew={true} isFollowed={true} isSaved={true} isApplied={true} />,
    );
    const tags = container.querySelectorAll(".jp-tag");
    expect(tags).toHaveLength(4);
    expect(tags[0]).toHaveTextContent("Ny");
    expect(tags[1]).toHaveTextContent("Bevakar");
    expect(tags[2]).toHaveTextContent("Sparad");
    expect(tags[3]).toHaveTextContent("Ansökt");
  });
});
