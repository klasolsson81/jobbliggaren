import { describe, it, expect, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { JobTags, computeFreshnessLabel } from "./job-tags";

const DAY_MS = 24 * 60 * 60 * 1000;

describe("computeFreshnessLabel", () => {
  const now = Date.parse("2026-05-20T12:00:00Z");

  it("returns 'Idag' for same-day publish", () => {
    const published = new Date(now - 2 * 60 * 60 * 1000).toISOString();
    expect(computeFreshnessLabel(published, now)).toBe("Idag");
  });

  it("returns '1 dag' for 1-day-old", () => {
    const published = new Date(now - 1 * DAY_MS).toISOString();
    expect(computeFreshnessLabel(published, now)).toBe("1 dag");
  });

  it("returns 'N dagar' for 2-7 days old (svensk plural)", () => {
    expect(
      computeFreshnessLabel(new Date(now - 2 * DAY_MS).toISOString(), now),
    ).toBe("2 dagar");
    expect(
      computeFreshnessLabel(new Date(now - 5 * DAY_MS).toISOString(), now),
    ).toBe("5 dagar");
    expect(
      computeFreshnessLabel(new Date(now - 7 * DAY_MS).toISOString(), now),
    ).toBe("7 dagar");
  });

  it("returns null when older than 7 days (cutoff)", () => {
    const published = new Date(now - 8 * DAY_MS).toISOString();
    expect(computeFreshnessLabel(published, now)).toBeNull();
  });

  it("returns null for unparseable ISO", () => {
    expect(computeFreshnessLabel("not-an-iso", now)).toBeNull();
  });

  it("returns null for future publishedAt (negative age)", () => {
    const published = new Date(now + 1 * DAY_MS).toISOString();
    expect(computeFreshnessLabel(published, now)).toBeNull();
  });
});

describe("JobTags", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it("renders NY when showNew=true and not previously read", () => {
    render(
      <JobTags
        jobAdId="id-1"
        showNew={true}
        freshnessLabel={null}
        matchScore={undefined}
      />,
    );
    expect(screen.getByText("Ny")).toBeInTheDocument();
  });

  it("does not render NY when showNew=false", () => {
    render(
      <JobTags
        jobAdId="id-2"
        showNew={false}
        freshnessLabel={null}
        matchScore={undefined}
      />,
    );
    expect(screen.queryByText("Ny")).not.toBeInTheDocument();
  });

  it("does not render NY when previously marked read in localStorage", () => {
    window.localStorage.setItem(
      "jp-read-jobads",
      JSON.stringify({ "id-3": true }),
    );
    render(
      <JobTags
        jobAdId="id-3"
        showNew={true}
        freshnessLabel={null}
        matchScore={undefined}
      />,
    );
    expect(screen.queryByText("Ny")).not.toBeInTheDocument();
  });

  it("renders freshness label when provided", () => {
    render(
      <JobTags
        jobAdId="id-4"
        showNew={false}
        freshnessLabel="2 dagar"
        matchScore={undefined}
      />,
    );
    expect(screen.getByText("2 dagar")).toBeInTheDocument();
  });

  it("renders 'Bra match' when matchScore >= 75 (Fas 4 placeholder)", () => {
    render(
      <JobTags
        jobAdId="id-5"
        showNew={false}
        freshnessLabel={null}
        matchScore={80}
      />,
    );
    expect(screen.getByText("Bra match")).toBeInTheDocument();
  });

  it("does not render 'Bra match' when matchScore below threshold", () => {
    render(
      <JobTags
        jobAdId="id-6"
        showNew={false}
        freshnessLabel={null}
        matchScore={74}
      />,
    );
    expect(screen.queryByText("Bra match")).not.toBeInTheDocument();
  });

  it("does not render 'Bra match' when matchScore undefined (Prompt 1 default)", () => {
    render(
      <JobTags
        jobAdId="id-7"
        showNew={false}
        freshnessLabel={null}
        matchScore={undefined}
      />,
    );
    expect(screen.queryByText("Bra match")).not.toBeInTheDocument();
  });

  it("renders nothing when all tags are absent (no empty container)", () => {
    const { container } = render(
      <JobTags
        jobAdId="id-8"
        showNew={false}
        freshnessLabel={null}
        matchScore={undefined}
      />,
    );
    expect(container.querySelector(".jp-job-tags")).toBeNull();
  });

  it("renders all three tags in order: NY → freshness → match", () => {
    const { container } = render(
      <JobTags
        jobAdId="id-9"
        showNew={true}
        freshnessLabel="Idag"
        matchScore={90}
      />,
    );
    const tags = container.querySelectorAll(".jp-tag");
    expect(tags).toHaveLength(3);
    expect(tags[0]).toHaveTextContent("Ny");
    expect(tags[1]).toHaveTextContent("Idag");
    expect(tags[2]).toHaveTextContent("Bra match");
  });
});
