import type { ApiResult } from "@/lib/dto/_helpers";
import type { JobSeekerProfileDto } from "@/lib/dto/me";

export type SetupState = "incomplete" | "configured" | "unavailable";

export function getSetupState(profile: ApiResult<JobSeekerProfileDto>): SetupState {
  if (profile.kind !== "ok") return "unavailable";
  return profile.data.hasStatedDesiredOccupation ? "configured" : "incomplete";
}

export function hasSavedMatchingChoices(profile: JobSeekerProfileDto): boolean {
  return (
    profile.preferredOccupationGroups.length > 0 ||
    profile.preferredSkills.length > 0 ||
    profile.preferredRegions.length > 0 ||
    profile.preferredMunicipalities.length > 0 ||
    profile.preferredRemote ||
    profile.preferredEmploymentTypes.length > 0 ||
    profile.experienceYears !== null
  );
}
