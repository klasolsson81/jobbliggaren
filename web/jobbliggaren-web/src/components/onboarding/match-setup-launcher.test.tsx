import { describe, it, expect, vi } from "vitest";
import { render } from "@testing-library/react";
import type { SkillGroup } from "@/lib/dto/skills";

const railProps = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: vi.fn(), refresh: vi.fn() }),
}));
vi.mock("@/lib/onboarding/setup-welcome-actions", () => ({ markSetupWelcomeSeen: vi.fn() }));
// The rail is pinned in its own tests; here it only records what the launcher hands it.
vi.mock("@/components/settings/match-setup-rail-modal", () => ({
  MatchSetupRailModal: (props: Record<string, unknown>) => {
    railProps(props);
    return null;
  },
}));

import { MatchSetupLauncher } from "./match-setup-launcher";

const twinPair: SkillGroup = {
  conceptId: "Sk1l_CsH_arp",
  label: "C#",
  memberConceptIds: ["Sk1l_CsH_arp", "Sk1l_CsH_af"],
};

describe("MatchSetupLauncher", () => {
  it("hands the rail the saved skills as named groups (ADR 0047)", () => {
    render(
      <MatchSetupLauncher
        autoOpen
        occupationFields={[]}
        regions={[]}
        employmentTypes={[]}
        persistedOccupationGroups={[]}
        persistedRegions={[]}
        persistedMunicipalities={[]}
        persistedRemote={false}
        persistedEmploymentTypes={[]}
        persistedSkills={twinPair.memberConceptIds}
        persistedSkillGroups={[twinPair]}
        persistedOccupationExperience={[]}
        importCvHref="/cv/importera"
      />,
    );

    expect(railProps).toHaveBeenLastCalledWith(
      expect.objectContaining({
        persistedSkills: twinPair.memberConceptIds,
        persistedSkillGroups: [twinPair],
      }),
    );
  });
});
