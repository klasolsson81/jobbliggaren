import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobbHeroFilters } from "./jobb-hero-filters";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";

/**
 * #2212: an open /jobb filter popover closes when keyboard focus leaves it. The panels follow the
 * pills in the DOM, so Tab past a popover's last control lands on the page under it; measured
 * 2026-10-24 on 5ad51c9c1 at 1280x720, Tab past Ort's footer focused the results sort select,
 * covered by the still open popover.
 */

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));

const taxonomy: TaxonomyTree = {
  employmentTypes: [],
  worktimeExtents: [],
  regions: [
    {
      conceptId: "CifL_Rzy_Mku",
      label: "Stockholms län",
      municipalities: [{ conceptId: "AvNB_uwa_6n6", label: "Stockholm" }],
    },
  ],
  occupationFields: [],
};

function renderPage() {
  render(
    <>
      <JobbHeroFilters
        taxonomy={taxonomy}
        initialOccupationGroup={[]}
        initialOccupationName={[]}
        initialRegion={[]}
        initialMunicipality={[]}
        initialRemote={false}
        initialEmploymentType={[]}
        initialWorktimeExtent={[]}
        initialMatchGrades={[]}
        initialMatchningOff={false}
        initialIncludeRelated={false}
        initialHideApplied={false}
        initialOnlyMatched={false}
        hasStatedDesiredOccupation
        hasSeeker={false}
        q=""
        employer={[]}
        sortBy="PublishedAtDesc"
      />
      {/* The results toolbar's first control, which follows the hero on the page. */}
      <select aria-label="Sortera">
        <option>Senast publicerad</option>
      </select>
    </>,
  );
}

describe("a /jobb filter popover closes when focus leaves it (#2212)", () => {
  it("Tab past Ort's footer closes the popover and leaves focus on the next control", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    within(screen.getByRole("dialog", { name: "Ort" })).getByRole("button", { name: /^Visa/ }).focus();

    await user.tab();

    expect(screen.queryByRole("dialog", { name: "Ort" })).toBeNull();
    expect(screen.getByRole("combobox", { name: "Sortera" })).toHaveFocus();
  });

  it("an InfoDialog opened from Matchning leaves the popover open under it", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole("button", { name: /^Matchning/ }));
    const popover = screen.getByRole("dialog", { name: "Matchning" });

    await user.click(within(popover).getByRole("button", { name: "Vad betyder matchning?" }));

    expect(screen.getByRole("dialog", { name: "Om matchningsfiltret" })).toBeInTheDocument();
    // The modal dialog hides the rest of the page from assistive tech while it is open, the
    // popover with it; hidden is not closed.
    expect(screen.getByRole("dialog", { name: "Matchning", hidden: true })).toBeInTheDocument();
  });
});
