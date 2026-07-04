import { describe, it, expect } from "vitest";
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

describe("JobTags (NY = oläst, #293/#306)", () => {
  it("renders NY when isNew=true", () => {
    render(<JobTags isNew={true} freshnessLabel={null} />);
    expect(screen.getByText("Ny")).toBeInTheDocument();
  });

  it("does not render NY when isNew=false (oläst-watermark/kall start)", () => {
    render(<JobTags isNew={false} freshnessLabel={null} />);
    expect(screen.queryByText("Ny")).not.toBeInTheDocument();
  });

  // #293/#306 + #485 — a11y-paritet med /matchningar: NY-taggen bär full
  // skärmläsar-kontext via en sr-only-text (aria-label är ogiltig på en generisk
  // <span>/role=generic). Färg är aldrig ensam signal (WCAG 1.4.1) — synlig text
  // "Ny" plus sr-only-kontexten bär betydelsen.
  it("NY-taggen bär skärmläsar-kontext via sr-only text, inte aria-label", () => {
    render(<JobTags isNew={true} freshnessLabel={null} />);
    const ny = screen.getByText("Ny");
    expect(ny).toHaveAttribute("data-tag", "new");
    expect(ny).not.toHaveAttribute("aria-label");
    expect(
      screen.getByText("Nytt jobb sedan ditt senaste besök"),
    ).toBeInTheDocument();
  });

  it("renders freshness label when provided", () => {
    render(<JobTags isNew={false} freshnessLabel="2 dagar" />);
    expect(screen.getByText("2 dagar")).toBeInTheDocument();
  });

  // F4-13 (ADR 0076) — den numeriska matchScore/MATCH_THRESHOLD-taggen är
  // borttagen (Goodhart-förbud). Match-graden renderas nu av MatchChip i
  // JobAdCard, inte här; JobTags känner inte längre till match alls.
  it("does not render a match tag (numeric matchScore-modellen borttagen)", () => {
    const { container } = render(
      <JobTags
        isNew={true}
        freshnessLabel="Idag"
        isSaved={true}
        isApplied={true}
      />,
    );
    expect(screen.queryByText("Bra match")).not.toBeInTheDocument();
    expect(container.querySelector('[data-tag="match"]')).toBeNull();
  });

  it("renders nothing when all tags are absent (no empty container)", () => {
    const { container } = render(<JobTags isNew={false} freshnessLabel={null} />);
    expect(container.querySelector(".jp-job-tags")).toBeNull();
  });

  it("renders both tags in order: NY → freshness", () => {
    const { container } = render(
      <JobTags isNew={true} freshnessLabel="Idag" />,
    );
    const tags = container.querySelectorAll(".jp-tag");
    expect(tags).toHaveLength(2);
    expect(tags[0]).toHaveTextContent("Ny");
    expect(tags[1]).toHaveTextContent("Idag");
  });

  // PR5 — Sparad + Ansökt-taggar (ADR 0063 per-user-overlay).
  it("renders Sparad-tagg när isSaved=true", () => {
    render(<JobTags isNew={false} freshnessLabel={null} isSaved={true} />);
    expect(screen.getByText("Sparad")).toBeInTheDocument();
  });

  it("renders Ansökt-tagg när isApplied=true", () => {
    render(<JobTags isNew={false} freshnessLabel={null} isApplied={true} />);
    expect(screen.getByText("Ansökt")).toBeInTheDocument();
  });

  it("renderar inga status-taggar när isSaved=false + isApplied=false (default)", () => {
    const { container } = render(
      <JobTags isNew={false} freshnessLabel={null} />,
    );
    // Inget tagg-block ska renderas alls
    expect(container.querySelector(".jp-job-tags")).toBeNull();
  });

  it("renderar både Sparad och Ansökt vid full status", () => {
    const { container } = render(
      <JobTags
        isNew={false}
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

  it("renderar alla 4 taggar i ordning: NY → freshness → Sparad → Ansökt", () => {
    const { container } = render(
      <JobTags
        isNew={true}
        freshnessLabel="Idag"
        isSaved={true}
        isApplied={true}
      />,
    );
    const tags = container.querySelectorAll(".jp-tag");
    expect(tags).toHaveLength(4);
    expect(tags[0]).toHaveTextContent("Ny");
    expect(tags[1]).toHaveTextContent("Idag");
    expect(tags[2]).toHaveTextContent("Sparad");
    expect(tags[3]).toHaveTextContent("Ansökt");
  });
});
