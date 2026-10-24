import { createHash } from "node:crypto";
import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobbHeroFilters } from "./jobb-hero-filters";
import { ForetagSokSearchbar } from "@/components/company-criteria/foretag-sok-searchbar";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import type { CriterionReference } from "@/lib/dto/company-criteria";

/**
 * One-off characterization for #2212: the open Ort popover's markup on /jobb and on /foretag/sok,
 * hashed before the positioning is merged into one hook and the popover shell is shared, and
 * asserted unchanged at every commit after it. Deleted by the commit that adds the phone sheet.
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

describe("the open Ort popover's markup (#2212 characterization)", () => {
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

const JOBB_ORT_BASE = "151a87dcd90b4fecfc4b41feac77864f29407f26de627666051d85af8592587f";
const FORETAG_ORT_BASE = "c2527f70f9d5d801a78b3475c6a650c00223f70b1f7df09cbaac8bb79720dfdd";
