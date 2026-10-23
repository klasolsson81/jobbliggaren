import { createHash } from "node:crypto";
import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobbHeroFilters } from "./jobb-hero-filters";
import { ForetagSokSearchbar } from "@/components/company-criteria/foretag-sok-searchbar";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import type { CriterionReference } from "@/lib/dto/company-criteria";
import { CheckItem } from "@/components/settings/section-helpers";
import { OccupationSearchResults } from "@/components/occupation-search/occupation-search-results";
import { ruleAnswer, IT_GROUP, USK_VARD_GROUP } from "@/test/fixtures/occupation-search";

/**
 * One-off characterization for #2198 PR 2: the open Ort popover's markup on /jobb and on
 * /foretag/sok, and the markup of `CheckItem` and `OccupationSearchResults` without counts, hashed
 * before this PR changes them and asserted unchanged at every commit after it. Deleted by the PR's
 * last code commit.
 */

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock("@/lib/actions/company-follows", () => ({
  followCompanyAction: vi.fn(),
  unfollowCompanyAction: vi.fn(),
}));

const taxonomy: TaxonomyTree = {
  employmentTypes: [],
  worktimeExtents: [],
  regions: [
    {
      conceptId: "CifL_Rzy_Mku",
      label: "Stockholms län",
      municipalities: [
        { conceptId: "AvNB_uwa_6n6", label: "Stockholm" },
        { conceptId: "zHxw_uJZ_NNh", label: "Solna" },
      ],
    },
    {
      conceptId: "oDpK_oQy_3Zc",
      label: "Västra Götalands län",
      municipalities: [{ conceptId: "PVZL_BQT_XtL", label: "Göteborg" }],
    },
  ],
  occupationFields: [
    {
      conceptId: "apaJ_2ja_LuF",
      label: "Data/IT",
      occupationGroups: [{ conceptId: "MVqp_eS8_kDZ", label: "Systemutvecklare" }],
    },
  ],
};

const reference: CriterionReference = {
  sniVersion: "2025",
  kommunVersion: "2025",
  sni: [],
  lan: [
    {
      code: "01",
      name: "Stockholms län",
      kommuner: [
        { code: "0180", name: "Stockholm" },
        { code: "0181", name: "Södertälje" },
      ],
    },
  ],
};

function digest(html: string): string {
  const normalised = html
    .replace(/«[^»]*»/g, "«id»")
    .replace(/_r_[0-9a-z]+_/g, "_r_id_")
    .replace(/:r[0-9a-z]+:/g, ":rid:");
  return createHash("sha256").update(normalised).digest("hex");
}

describe("the open Ort popover's markup (#2198 PR 2 characterization)", () => {
  it("/jobb Ort, a region open, a municipality selected, counts loaded", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input);
        const body = url.includes("dimension=Municipality")
          ? { zHxw_uJZ_NNh: 12, AvNB_uwa_6n6: 340 }
          : url.includes("dimension=Region")
            ? { CifL_Rzy_Mku: 1500 }
            : {};
        return new Response(JSON.stringify(body), { status: 200 });
      }),
    );
    try {
      const user = userEvent.setup();
      render(
        <JobbHeroFilters
          taxonomy={taxonomy}
          initialOccupationGroup={[]}
          initialRegion={[]}
          initialMunicipality={["zHxw_uJZ_NNh"]}
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
      await user.click(screen.getByRole("button", { name: /^Ort/ }));
      const dialog = screen.getByRole("dialog", { name: "Ort" });
      await user.click(within(dialog).getByText("Stockholms län"));
      expect(await within(dialog).findByText("(12)")).toBeInTheDocument();

      expect(digest(dialog.outerHTML)).toBe(JOBB_ORT_BASE);
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it("/foretag/sok Ort, a län open, a kommun selected", async () => {
    const user = userEvent.setup();
    render(
      <ForetagSokSearchbar reference={reference} referenceOk namn="" sni={[]} kommun={["0180"]} />,
    );
    await user.click(screen.getByRole("button", { name: "Välj ort eller län" }));
    const dialog = screen.getByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Stockholms län" }));

    expect(digest(dialog.outerHTML)).toBe(FORETAG_ORT_BASE);
  });
});

describe("CheckItem and OccupationSearchResults without counts (#2198 PR 2 characterization)", () => {
  it("CheckItem, every one-line and two-line form PR 1's callers use", () => {
    const { container } = render(
      <div>
        <CheckItem label="Systemutvecklare" checked={false} onToggle={() => {}} />
        <CheckItem label="Systemutvecklare" checked onToggle={() => {}} />
        <CheckItem label="Välj alla" checked={false} indeterminate isAll onToggle={() => {}} />
        <CheckItem label="Distans" checked={false} describedBy="hint" onToggle={() => {}} />
        <CheckItem label={IT_GROUP.label} secondary="träff på Backend-utvecklare" checked onToggle={() => {}} />
        <CheckItem label="Projektledare, IT" secondary="Yrkesgrupp: X" describedBy="hint" checked={false} onToggle={() => {}} />
      </div>,
    );
    expect(digest(container.innerHTML)).toBe(CHECKITEM_BASE);
  });

  it("OccupationSearchResults, Group rows, a Word row opened, a cap line, a selection", async () => {
    const user = userEvent.setup();
    const { container, rerender } = render(
      <OccupationSearchResults result={ruleAnswer.sjukskoterska} selected={[]} onToggle={() => {}} />,
    );
    const sjukskoterska = container.innerHTML;
    rerender(<OccupationSearchResults result={ruleAnswer.chef} selected={[]} onToggle={() => {}} />);
    const chef = container.innerHTML;
    rerender(
      <OccupationSearchResults result={ruleAnswer.underskoterska} selected={[USK_VARD_GROUP.id]} onToggle={() => {}} />,
    );
    await user.click(screen.getByRole("button", { name: "Undersköterska" }));
    const underskoterska = container.innerHTML;
    rerender(<OccupationSearchResults result={null} selected={[]} onToggle={() => {}} />);
    const pending = container.innerHTML;

    expect(digest([sjukskoterska, chef, underskoterska, pending].join("|"))).toBe(RESULTS_BASE);
  });
});

const CHECKITEM_BASE = "add79e6f9b7b582c04891ec585001e848b2eff04726e57c3686f4ea78b5a8e6e";
const RESULTS_BASE = "cfdb9a6e3e5c4270aa03519d73aca9910760d0e503a516fa179a900ced1af06e";
const JOBB_ORT_BASE = "151a87dcd90b4fecfc4b41feac77864f29407f26de627666051d85af8592587f";
const FORETAG_ORT_BASE = "c2527f70f9d5d801a78b3475c6a650c00223f70b1f7df09cbaac8bb79720dfdd";
