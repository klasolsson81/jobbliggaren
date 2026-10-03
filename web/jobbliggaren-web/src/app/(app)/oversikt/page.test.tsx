import { describe, it, expect, vi, beforeEach } from "vitest";
import { render } from "@testing-library/react";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { SkillGroup } from "@/lib/dto/skills";
import type { JobSeekerProfileDto } from "@/lib/types/me";

const getMyProfile = vi.fn<() => Promise<ApiResult<JobSeekerProfileDto>>>();
const resolveSkillLabels = vi.fn<(ids: ReadonlyArray<string>) => Promise<ApiResult<SkillGroup[]>>>();
const launcherProps = vi.fn();
const overviewProps = vi.fn();
const hasSeenSetupWelcome = vi.fn();
const getTaxonomyTree = vi.fn();

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
  getTaxonomyTree: () => getTaxonomyTree(),
}));
vi.mock("@/lib/onboarding/setup-welcome", () => ({ hasSeenSetupWelcome: () => hasSeenSetupWelcome() }));
vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
}));
vi.mock("@/components/oversikt/oversikt-page", () => ({ OversiktPage: (props: Record<string, unknown>) => { overviewProps(props); return null; } }));
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
      preferredOccupationGroups: hasStatedDesiredOccupation ? ["grp_dev"] : [],
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

async function renderRoute(matchsetup?: string) {
  render(await OversiktRoute({ searchParams: Promise.resolve({ matchsetup }) }));
}

describe("OversiktRoute (/oversikt) — the match-setup rail", () => {
  beforeEach(() => {
    getMyProfile.mockReset();
    resolveSkillLabels.mockReset();
    launcherProps.mockReset();
    overviewProps.mockReset();
    hasSeenSetupWelcome.mockReset().mockResolvedValue(false);
    getTaxonomyTree.mockReset().mockResolvedValue({ kind: "ok", data: { occupationFields: [], regions: [], employmentTypes: [] } });
  });

  it("resolves the saved skills to named groups and hands them to the rail (ADR 0047)", async () => {
    getMyProfile.mockResolvedValue(profile(false));
    resolveSkillLabels.mockResolvedValue({ kind: "ok", data: [twinPair] });

    await renderRoute("1");

    expect(resolveSkillLabels).toHaveBeenCalledWith(savedSkills);
    expect(launcherProps).toHaveBeenLastCalledWith(
      expect.objectContaining({ request: "resume", data: expect.objectContaining({ persistedSkills: savedSkills, persistedSkillGroups: [twinPair], resumeStep: 1 }) }),
    );
  });

  it("keeps the rail and its id fallback when the names cannot be read", async () => {
    getMyProfile.mockResolvedValue(profile(false));
    resolveSkillLabels.mockResolvedValue({ kind: "error" });

    await renderRoute();

    expect(launcherProps).toHaveBeenLastCalledWith(
      expect.objectContaining({ request: "welcome", data: expect.objectContaining({ persistedSkills: savedSkills, persistedSkillGroups: [] }) }),
    );
  });

  it("keeps the launcher mounted without opening data for a configured profile, including without CV", async () => {
    getMyProfile.mockResolvedValue(profile(true));

    await renderRoute();

    expect(launcherProps).toHaveBeenLastCalledWith(expect.objectContaining({ request: null, data: null }));
    expect(resolveSkillLabels).not.toHaveBeenCalled();
  });

  it("dismissal prevents automatic welcome but does not prevent explicit resume", async () => {
    hasSeenSetupWelcome.mockResolvedValue(true);
    getMyProfile.mockResolvedValue(profile(false));
    resolveSkillLabels.mockResolvedValue({ kind: "ok", data: [twinPair] });
    await renderRoute();
    expect(launcherProps).toHaveBeenLastCalledWith(expect.objectContaining({ request: null, data: null }));
    await renderRoute("1");
    expect(launcherProps).toHaveBeenLastCalledWith(expect.objectContaining({ request: "resume", data: expect.objectContaining({ resumeStep: 1 }) }));
  });

  it("an empty persisted profile resumes at Start", async () => {
    const empty = profile(false);
    if (empty.kind !== "ok") throw new Error("Expected fixture profile");
    empty.data = { ...empty.data, preferredSkills: [] };
    getMyProfile.mockResolvedValue(empty);
    resolveSkillLabels.mockResolvedValue({ kind: "ok", data: [] });
    await renderRoute("1");
    expect(launcherProps).toHaveBeenLastCalledWith(expect.objectContaining({ data: expect.objectContaining({ resumeStep: 0 }) }));
  });

  it.each([
    { preferredRegions: ["region_AB"] },
    { preferredMunicipalities: ["municipality_0180"] },
    { preferredRemote: true },
    { preferredEmploymentTypes: ["permanent"] },
    { experienceYears: 0 },
  ])("resumes saved matching choices at occupations: %j", async (choices) => {
    const partial = profile(false);
    if (partial.kind !== "ok") throw new Error("Expected fixture profile");
    partial.data = { ...partial.data, preferredSkills: [], ...choices };
    getMyProfile.mockResolvedValue(partial);
    resolveSkillLabels.mockResolvedValue({ kind: "ok", data: [] });
    await renderRoute("1");
    expect(launcherProps).toHaveBeenLastCalledWith(expect.objectContaining({ data: expect.objectContaining({ resumeStep: 1 }) }));
  });

  it.each(["error", "forbidden"] as const)("a %s profile never opens incomplete setup", async (kind) => {
    getMyProfile.mockResolvedValue({ kind });
    await renderRoute("1");
    expect(launcherProps).toHaveBeenLastCalledWith(expect.objectContaining({ request: null, data: null }));
    expect(overviewProps).toHaveBeenLastCalledWith(expect.objectContaining({ profile: { kind }, setupUnavailable: false }));
    expect(resolveSkillLabels).not.toHaveBeenCalled();
  });

  it("reports a failed taxonomy launch and still leaves the launcher for a successful retry", async () => {
    getMyProfile.mockResolvedValue(profile(false));
    resolveSkillLabels.mockResolvedValue({ kind: "ok", data: [] });
    getTaxonomyTree.mockResolvedValue({ kind: "error" });
    await renderRoute("1");
    expect(overviewProps).toHaveBeenLastCalledWith(expect.objectContaining({ setupUnavailable: true }));
    expect(launcherProps).toHaveBeenLastCalledWith(expect.objectContaining({ request: "resume", data: null }));
    getTaxonomyTree.mockResolvedValue({ kind: "ok", data: { occupationFields: [], regions: [], employmentTypes: [] } });
    await renderRoute("1");
    expect(overviewProps).toHaveBeenLastCalledWith(expect.objectContaining({ setupUnavailable: false }));
    expect(launcherProps).toHaveBeenLastCalledWith(expect.objectContaining({ request: "resume", data: expect.any(Object) }));
  });
});
