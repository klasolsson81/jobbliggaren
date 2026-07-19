import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { JobAdList } from "./job-ad-list";
import type { JobAdDto } from "@/lib/dto/job-ads";
import type { MatchGrade } from "@/lib/dto/job-ad-match";

// publishedAt > 7 dygn så freshness-tagg INTE renderas (skulle annars läggas
// till h3:s accessible name och bryta `getByRole("heading", { name })`).
const sampleAd = (id: string, title: string): JobAdDto => ({
  id,
  title,
  companyName: "Acme AB",
  url: "https://example.com/jobb/" + id,
  source: "Platsbanken",
  status: "Active",
  publishedAt: "2026-04-01T08:00:00Z",
  expiresAt: null,
  createdAt: "2026-04-01T08:01:00Z",
});

describe("JobAdList", () => {
  it("renders empty-state with civic-utility message when list is empty", () => {
    render(<JobAdList jobAds={[]} />);
    expect(screen.getByText("Inga jobb hittades")).toBeInTheDocument();
    expect(
      screen.getByText(/Justera filtren eller töm sökrutan/)
    ).toBeInTheDocument();
  });

  it("empty-state does not duplicate live-region (page.tsx owns it)", () => {
    const { container } = render(<JobAdList jobAds={[]} />);
    expect(container.querySelector("[aria-live]")).toBeNull();
    expect(container.querySelector("[role='status']")).toBeNull();
  });

  it("renders a list with one item per job ad", () => {
    const ads = [
      sampleAd("a1", "Backend-utvecklare"),
      sampleAd("a2", "Frontend-utvecklare"),
    ];
    render(<JobAdList jobAds={ads} />);
    expect(
      screen.getByRole("heading", { name: "Backend-utvecklare" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: "Frontend-utvecklare" })
    ).toBeInTheDocument();
  });

  it("uses a labelled list element for screen reader navigation", () => {
    render(<JobAdList jobAds={[sampleAd("a1", "Job A")]} />);
    expect(screen.getByRole("list", { name: "Jobbannonser" })).toBeInTheDocument();
  });

  // issue #292 — Gate (a) on the badge layer. When the matching axis is OFF,
  // jobb-results.tsx skips the match-tag fetch and passes an empty (or no)
  // matchGradeById → JobAdList must render NO MatchChip on any card. This is
  // the list-level proof of "matchActive=false → no badges" (the toolbar
  // cannot render the list, so badge-gating is asserted here).
  it("renders NO MatchChip when matchGradeById is empty (matchActive=false)", () => {
    const { container } = render(
      <JobAdList
        jobAds={[sampleAd("a1", "Job A"), sampleAd("a2", "Job B")]}
        matchGradeById={new Map<string, MatchGrade>()}
      />,
    );
    expect(container.querySelector(".jp-matchchip")).toBeNull();
  });

  it("renders NO MatchChip when matchGradeById is omitted (anonymous / off)", () => {
    const { container } = render(
      <JobAdList jobAds={[sampleAd("a1", "Job A")]} />,
    );
    expect(container.querySelector(".jp-matchchip")).toBeNull();
  });

  it("renders a MatchChip only for ads present in matchGradeById (matchActive=true)", () => {
    const { container } = render(
      <JobAdList
        jobAds={[sampleAd("a1", "Job A"), sampleAd("a2", "Job B")]}
        matchGradeById={new Map<string, MatchGrade>([["a1", "Strong"]])}
      />,
    );
    // Exactly one card earns a badge (POSITIVE-ONLY); the other has none.
    const chips = container.querySelectorAll(".jp-matchchip");
    expect(chips).toHaveLength(1);
  });

  // #293/#306 — NY = oläst: only ads in newIdSet (createdAt > watermark, computed
  // in JobbResults) render the "Ny" tag. Omitted set = cold start / anon ⇒ no NY.
  it("renders the Ny tag only for ads present in newIdSet", () => {
    const { container } = render(
      <JobAdList
        jobAds={[sampleAd("a1", "Job A"), sampleAd("a2", "Job B")]}
        newIdSet={new Set<string>(["a1"])}
      />,
    );
    const newTags = container.querySelectorAll('[data-tag="new"]');
    expect(newTags).toHaveLength(1);
  });

  it("renders NO Ny tag when newIdSet is omitted (anonymous / cold start)", () => {
    const { container } = render(
      <JobAdList jobAds={[sampleAd("a1", "Job A"), sampleAd("a2", "Job B")]} />,
    );
    expect(container.querySelector('[data-tag="new"]')).toBeNull();
  });
});
