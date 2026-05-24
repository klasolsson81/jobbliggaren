import { describe, it, expect, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { JobTags } from "./job-tags";
import { computeFreshnessLabel } from "./freshness";

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

  // Klas-observation 2026-05-24 — låser kalender-dag-jämförelse: en annons
  // publicerad strax före midnatt UTC visas som "1 dag" ~3h senare, INTE som
  // "Idag" (vilket 24h-fönster-implementationen tidigare gjorde och som
  // krockade med `formatPublishedAtWithTime`-copy som sa "igår, kl. 23:37").
  it("returns '1 dag' when published before midnight UTC and inspected next day (kalender-gräns)", () => {
    const inspectedAt = Date.parse("2026-05-24T02:30:00Z");
    const publishedJustBeforeMidnight = "2026-05-23T23:37:00Z";
    expect(computeFreshnessLabel(publishedJustBeforeMidnight, inspectedAt)).toBe(
      "1 dag",
    );
  });

  it("returns 'Idag' for any publish time on same UTC calendar day", () => {
    const inspectedAt = Date.parse("2026-05-24T23:50:00Z");
    const publishedSameDayEarly = "2026-05-24T00:11:00Z";
    expect(computeFreshnessLabel(publishedSameDayEarly, inspectedAt)).toBe(
      "Idag",
    );
  });
});

describe("JobTags (high-water-mark NY-modell)", () => {
  const RECENT_MS = Date.parse("2026-05-19T12:00:00Z");
  const OLD_MS = Date.parse("2026-04-01T12:00:00Z");

  beforeEach(() => {
    window.localStorage.clear();
  });

  it("renders NY when showNew=true and never-visited (lastSeen=0)", () => {
    render(
      <JobTags
        showNew={true}
        publishedAtMs={RECENT_MS}
        freshnessLabel={null}
        matchScore={undefined}
      />,
    );
    expect(screen.getByText("Ny")).toBeInTheDocument();
  });

  it("does not render NY when showNew=false (server-cap >7d)", () => {
    render(
      <JobTags
        showNew={false}
        publishedAtMs={RECENT_MS}
        freshnessLabel={null}
        matchScore={undefined}
      />,
    );
    expect(screen.queryByText("Ny")).not.toBeInTheDocument();
  });

  it("does not render NY when lastSeen >= publishedAtMs (visited after publish)", () => {
    window.localStorage.setItem(
      "jp-jobb-last-seen",
      String(Date.parse("2026-05-20T00:00:00Z")),
    );
    render(
      <JobTags
        showNew={true}
        publishedAtMs={RECENT_MS}
        freshnessLabel={null}
        matchScore={undefined}
      />,
    );
    expect(screen.queryByText("Ny")).not.toBeInTheDocument();
  });

  it("renders NY when lastSeen < publishedAtMs (published after last visit)", () => {
    window.localStorage.setItem(
      "jp-jobb-last-seen",
      String(Date.parse("2026-05-15T00:00:00Z")),
    );
    render(
      <JobTags
        showNew={true}
        publishedAtMs={RECENT_MS}
        freshnessLabel={null}
        matchScore={undefined}
      />,
    );
    expect(screen.getByText("Ny")).toBeInTheDocument();
  });

  it("server-cap (showNew=false) overrides high-water-mark even when lastSeen=0", () => {
    render(
      <JobTags
        showNew={false}
        publishedAtMs={OLD_MS}
        freshnessLabel={null}
        matchScore={undefined}
      />,
    );
    expect(screen.queryByText("Ny")).not.toBeInTheDocument();
  });

  it("renders freshness label when provided", () => {
    render(
      <JobTags
        showNew={false}
        publishedAtMs={RECENT_MS}
        freshnessLabel="2 dagar"
        matchScore={undefined}
      />,
    );
    expect(screen.getByText("2 dagar")).toBeInTheDocument();
  });

  it("renders 'Bra match' when matchScore >= 75 (Fas 4 placeholder)", () => {
    render(
      <JobTags
        showNew={false}
        publishedAtMs={RECENT_MS}
        freshnessLabel={null}
        matchScore={80}
      />,
    );
    expect(screen.getByText("Bra match")).toBeInTheDocument();
  });

  it("does not render 'Bra match' when matchScore below threshold", () => {
    render(
      <JobTags
        showNew={false}
        publishedAtMs={RECENT_MS}
        freshnessLabel={null}
        matchScore={74}
      />,
    );
    expect(screen.queryByText("Bra match")).not.toBeInTheDocument();
  });

  it("does not render 'Bra match' when matchScore undefined (Prompt 1 default)", () => {
    render(
      <JobTags
        showNew={false}
        publishedAtMs={RECENT_MS}
        freshnessLabel={null}
        matchScore={undefined}
      />,
    );
    expect(screen.queryByText("Bra match")).not.toBeInTheDocument();
  });

  it("renders nothing when all tags are absent (no empty container)", () => {
    const { container } = render(
      <JobTags
        showNew={false}
        publishedAtMs={RECENT_MS}
        freshnessLabel={null}
        matchScore={undefined}
      />,
    );
    expect(container.querySelector(".jp-job-tags")).toBeNull();
  });

  it("renders all three tags in order: NY → freshness → match", () => {
    const { container } = render(
      <JobTags
        showNew={true}
        publishedAtMs={RECENT_MS}
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

  // PR5 — Sparad + Ansökt-taggar (ADR 0063 per-user-overlay).
  it("renders Sparad-tagg när isSaved=true", () => {
    render(
      <JobTags
        showNew={false}
        publishedAtMs={RECENT_MS}
        freshnessLabel={null}
        isSaved={true}
      />,
    );
    expect(screen.getByText("Sparad")).toBeInTheDocument();
  });

  it("renders Ansökt-tagg när isApplied=true", () => {
    render(
      <JobTags
        showNew={false}
        publishedAtMs={RECENT_MS}
        freshnessLabel={null}
        isApplied={true}
      />,
    );
    expect(screen.getByText("Ansökt")).toBeInTheDocument();
  });

  it("renderar inga status-taggar när isSaved=false + isApplied=false (default)", () => {
    const { container } = render(
      <JobTags
        showNew={false}
        publishedAtMs={RECENT_MS}
        freshnessLabel={null}
      />,
    );
    // Inget tagg-block ska renderas alls
    expect(container.querySelector(".jp-job-tags")).toBeNull();
  });

  it("renderar både Sparad och Ansökt vid full status", () => {
    const { container } = render(
      <JobTags
        showNew={false}
        publishedAtMs={RECENT_MS}
        freshnessLabel={null}
        isSaved={true}
        isApplied={true}
      />,
    );
    const tags = container.querySelectorAll(".jp-tag");
    expect(screen.getByText("Sparad")).toBeInTheDocument();
    expect(screen.getByText("Ansökt")).toBeInTheDocument();
    expect(tags).toHaveLength(2);
  });

  it("renderar alla 5 taggar i ordning: NY → freshness → Sparad → Ansökt → match", () => {
    const { container } = render(
      <JobTags
        showNew={true}
        publishedAtMs={RECENT_MS}
        freshnessLabel="Idag"
        matchScore={90}
        isSaved={true}
        isApplied={true}
      />,
    );
    const tags = container.querySelectorAll(".jp-tag");
    expect(tags).toHaveLength(5);
    expect(tags[0]).toHaveTextContent("Ny");
    expect(tags[1]).toHaveTextContent("Idag");
    expect(tags[2]).toHaveTextContent("Sparad");
    expect(tags[3]).toHaveTextContent("Ansökt");
    expect(tags[4]).toHaveTextContent("Bra match");
  });
});
