import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobbHeroFilters } from "./jobb-hero-filters";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));

const taxonomy: TaxonomyTree = {
  employmentTypes: [{ conceptId: "PFZr_Syz_cUq", label: "Vanlig anställning" }],
  worktimeExtents: [{ conceptId: "6YE1_gAC_R2G", label: "Heltid" }],
  regions: [
    {
      conceptId: "CifL_Rzy_Mku",
      label: "Stockholms län",
      municipalities: [{ conceptId: "AvNB_uwa_6n6", label: "Stockholm" }],
    },
  ],
  occupationFields: [
    {
      conceptId: "apaJ_2ja_LuF",
      label: "Data/IT",
      occupationGroups: [{ conceptId: "DJh5_yyF_hEM", label: "Mjukvaru- och systemutvecklare m.fl." }],
    },
  ],
};

describe("JobbHeroFilters — the results footer returns focus to its pill (#2198 PR 2)", () => {
  // Measured on origin/main 515b7424b: pressing "Visa annonser" from the keyboard closed the
  // popover and left focus on <body>.
  it.each([
    ["Ort", "Ort"],
    ["Yrke", "Yrke"],
    ["Filter", "Filter"],
  ])("closing %s from its footer by keyboard puts focus back on the %s pill", async (pillName, dialogName) => {
    const user = userEvent.setup();
    render(
      <JobbHeroFilters
        taxonomy={taxonomy}
        initialOccupationGroup={[]}
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
        hasStatedDesiredOccupation={false}
        hasSeeker={false}
        q=""
        employer={[]}
        sortBy="PublishedAtDesc"
      />,
    );
    const pill = screen.getByRole("button", { name: new RegExp(`^${pillName}`) });
    await user.click(pill);
    const footer = within(screen.getByRole("dialog", { name: dialogName })).getByRole("button", {
      name: /^Visa /,
    });

    footer.focus();
    await user.keyboard("{Enter}");

    expect(screen.queryByRole("dialog", { name: dialogName })).toBeNull();
    expect(pill).toHaveFocus();
  });
});
