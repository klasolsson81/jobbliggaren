import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { SavedJobAdRow } from "./saved-job-ad-row";
import type { SavedJobAdDto } from "@/lib/dto/saved-job-ads";

vi.mock("@/lib/actions/saved-job-ads", () => ({
  unsaveJobAdAction: vi.fn(),
}));

const JOB_AD_ID = "19630000-0000-4000-8000-000000000103";

function makeDto(withJobAd = true): SavedJobAdDto {
  return {
    id: "5a7e0000-0000-4000-8000-000000000103",
    jobAdId: JOB_AD_ID,
    savedAt: "2026-10-01T08:00:00Z",
    jobAd: withJobAd
      ? {
          jobAdId: JOB_AD_ID,
          title: "Testledare inom e-handel",
          company: "Nordlager Logistik AB",
          url: "https://example.test/annons/103",
          source: "Platsbanken",
          publishedAt: "2026-10-01T08:00:00Z",
          expiresAt: "2026-11-01T22:59:59Z",
        }
      : null,
  };
}

function renderRow(item: SavedJobAdDto) {
  return render(
    <ul>
      <SavedJobAdRow item={item} onUnsaved={() => undefined} onUnsaveFailed={() => undefined} />
    </ul>,
  );
}

// #2012 — the card's pointer and hover border promise a click, so the card is the target: the
// title is the row's one link to the ad, stretched over the card. #2014 — no plate.
describe("SavedJobAdRow", () => {
  it("makes the title the card's one link to the ad, with no aria-label", () => {
    const { container } = renderRow(makeDto());
    expect(container.querySelectorAll('a[href^="/jobb/"]')).toHaveLength(1);
    const link = screen.getByRole("link", { name: "Testledare inom e-handel" });
    expect(link).toHaveAttribute("href", `/jobb/${JOB_AD_ID}`);
    expect(link).toHaveClass("jp-job__rowlink", { exact: true });
    // The modal's focus return re-finds a restored opener by its href and whether it has an
    // aria-label (useInformationModalFocus).
    expect(link).not.toHaveAttribute("aria-label");
  });

  it("describes the link with the company, then the dates", () => {
    renderRow(makeDto());
    const link = screen.getByRole("link", { name: "Testledare inom e-handel" });
    const parts = (link.getAttribute("aria-describedby") ?? "")
      .split(" ")
      .map((id) => document.getElementById(id));
    expect(parts.map((part) => part?.className)).toEqual(["jp-job__company", "jp-job__meta"]);
    expect(parts[0]).toHaveTextContent("Nordlager Logistik AB");
  });

  it("keeps each date's label and its space in one text node", () => {
    // Chrome drops a whitespace-only node from a computed description ("Publicerad1 okt. 2026").
    const { container } = renderRow(makeDto());
    const spans = [...container.querySelectorAll(".jp-job__meta > span")];
    expect(spans).toHaveLength(3);
    for (const span of spans) {
      expect(span.childNodes).toHaveLength(2);
      const [label, value] = [...span.childNodes];
      expect(label?.nodeType).toBe(Node.TEXT_NODE);
      expect(label?.textContent).toMatch(/\S $/);
      expect(value?.nodeName).toBe("B");
    }
  });

  it("keeps each control outside the link, as a direct child of the card's actions, and renders no plate", () => {
    const { container } = renderRow(makeDto());
    expect(screen.getByRole("article")).toHaveClass("jp-job", { exact: true });
    expect(container.querySelector(".jp-job__match")).toBeNull();
    const link = screen.getByRole("link", { name: "Testledare inom e-handel" });
    expect(link.querySelector("a, button")).toBeNull();
    const external = screen.getByRole("link", { name: "Öppna annonsen på externa webbplatsen" });
    const remove = screen.getByRole("button", { name: "Ta bort bokmärke för Testledare inom e-handel" });
    expect(link.contains(external) || link.contains(remove)).toBe(false);
    // The lift above the row link's overlay selects a control only in this shape (globals.css).
    for (const control of [external, remove]) {
      expect(control.parentElement).toHaveClass("jp-job__actions");
      expect(control.parentElement?.parentElement).toBe(screen.getByRole("article"));
    }
  });

  it("renders an erased ad as a static row, with no link and no plate", () => {
    const { container } = renderRow(makeDto(false));
    expect(screen.getByRole("article")).toHaveClass("jp-job jp-job--static", { exact: true });
    expect(container.querySelector("a")).toBeNull();
    expect(container.querySelector(".jp-job__match")).toBeNull();
    expect(screen.getByRole("button", { name: "Ta bort bokmärke" })).toBeInTheDocument();
  });
});
