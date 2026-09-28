import { describe, it, expect, vi, beforeEach } from "vitest";
import { render } from "@testing-library/react";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { SkillGroup } from "@/lib/dto/skills";
import type { JobSeekerProfileDto } from "@/lib/types/me";

const getMyProfile = vi.fn<() => Promise<ApiResult<JobSeekerProfileDto>>>();
const resolveSkillLabels = vi.fn<(ids: ReadonlyArray<string>) => Promise<ApiResult<SkillGroup[]>>>();
const launcherProps = vi.fn();

vi.mock("@/lib/auth/session", () => ({
  getServerSession: async () => ({ email: "klas@example.se", roles: [] }),
}));
vi.mock("@/lib/api/me", () => ({ getMyProfile: () => getMyProfile() }));
vi.mock("@/lib/api/skills", () => ({
  resolveSkillLabels: (ids: ReadonlyArray<string>) => resolveSkillLabels(ids),
}));
// The rest of the fan-out is not this file's subject: each source answers with what a failed read
// returns, which the page already degrades around.
vi.mock("@/lib/api/applications", () => ({ getPipeline: async () => ({ kind: "error" }) }));
vi.mock("@/lib/api/saved-job-ads", () => ({ getSavedJobAds: async () => ({ kind: "error" }) }));
vi.mock("@/lib/api/recent-searches", () => ({ getRecentSearches: async () => ({ kind: "error" }) }));
vi.mock("@/lib/api/match-count", () => ({ getMatchCount: async () => ({ kind: "error" }) }));
vi.mock("@/lib/api/company-follows", () => ({
  getCompanyWatches: async () => ({ kind: "error" }),
  getNewFollowedCompanyAdCount: async () => ({ kind: "error" }),
}));
vi.mock("@/lib/api/company-criteria", () => ({
  getCompanyWatchCriteria: async () => ({ kind: "error" }),
  getCriterionReference: async () => ({ kind: "error" }),
}));
vi.mock("@/lib/api/taxonomy", () => ({
  getTaxonomyTree: async () => ({
    kind: "ok",
    data: { occupationFields: [], regions: [], employmentTypes: [] },
  }),
}));
vi.mock("@/lib/onboarding/setup-welcome", () => ({ hasSeenSetupWelcome: async () => false }));
vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
}));
vi.mock("@/components/oversikt/oversikt-page", () => ({ OversiktPage: () => null }));
vi.mock("@/components/dev/reset-my-data-note", () => ({ ResetMyDataNote: () => null }));
vi.mock("@/components/onboarding/match-setup-launcher", () => ({
  MatchSetupLauncher: (props: Record<string, unknown>) => {
    launcherProps(props);
    return null;
  },
}));

import OversiktRoute from "./page";

const savedSkills = ["Sk1l_CsH_arp", "Sk1l_CsH_af"];
const twinPair: SkillGroup = { conceptId: "Sk1l_CsH_arp", label: "C#", memberConceptIds: savedSkills };

function profile(hasStatedDesiredOccupation: boolean): ApiResult<JobSeekerProfileDto> {
  return {
    kind: "ok",
    data: {
      id: "profile-1",
      language: "sv",
      backgroundMatchNotificationsEnabled: false,
      digestCadence: "Weekly",
      followedCompanyNotificationsEnabled: false,
      createdAt: "2026-05-01T08:00:00Z",
      hasStatedDesiredOccupation,
      preferredOccupationGroups: [],
      preferredRegions: [],
      preferredMunicipalities: [],
      preferredRemote: false,
      preferredEmploymentTypes: [],
      preferredSkills: savedSkills,
      experienceYears: null,
      preferredOccupationExperience: [],
    },
  };
}

async function renderRoute() {
  render(await OversiktRoute({ searchParams: Promise.resolve({ matchsetup: "1" }) }));
}

describe("OversiktRoute (/oversikt) — the match-setup rail", () => {
  beforeEach(() => {
    getMyProfile.mockReset();
    resolveSkillLabels.mockReset();
    launcherProps.mockReset();
  });

  it("resolves the saved skills to named groups and hands them to the rail (ADR 0047)", async () => {
    getMyProfile.mockResolvedValue(profile(false));
    resolveSkillLabels.mockResolvedValue({ kind: "ok", data: [twinPair] });

    await renderRoute();

    expect(resolveSkillLabels).toHaveBeenCalledWith(savedSkills);
    expect(launcherProps).toHaveBeenLastCalledWith(
      expect.objectContaining({ persistedSkills: savedSkills, persistedSkillGroups: [twinPair] }),
    );
  });

  it("keeps the rail and its id fallback when the names cannot be read", async () => {
    getMyProfile.mockResolvedValue(profile(false));
    resolveSkillLabels.mockResolvedValue({ kind: "error" });

    await renderRoute();

    expect(launcherProps).toHaveBeenLastCalledWith(
      expect.objectContaining({ persistedSkills: savedSkills, persistedSkillGroups: [] }),
    );
  });

  it("reads no names when the rail is not mounted", async () => {
    getMyProfile.mockResolvedValue(profile(true));

    await renderRoute();

    expect(launcherProps).not.toHaveBeenCalled();
    expect(resolveSkillLabels).not.toHaveBeenCalled();
  });
});
