/**
 * The fixture backend's data for the job modal's navigation harness (#1963). Fictional ads and people
 * only. The user either has stated an occupation or has not, and the profile and every match detail
 * say the same: a grade exists only when Yrke is Match, and a user who has stated nothing gets every
 * dimension NotAssessed for that cause (`MatchScorer`). The user has confirmed no skills, so the three
 * skill rows are NotAssessed for both users.
 */

const id = (n: number) => `19630000-0000-4000-8000-${String(n).padStart(12, "0")}`;

export type HarnessAd = {
  readonly id: string;
  readonly title: string;
  readonly company: string;
  readonly appliedBeforeVisit: boolean;
  readonly saved: boolean;
};

export const ADS = {
  open: { id: id(101), title: "Systemutvecklare till kundportalen", company: "Logikfabriken AB", appliedBeforeVisit: false, saved: false },
  applied: { id: id(102), title: "Backendutvecklare, betalningar", company: "Västkod AB", appliedBeforeVisit: true, saved: false },
  saved: { id: id(103), title: "Testledare inom e-handel", company: "Nordlager Logistik AB", appliedBeforeVisit: false, saved: true },
} as const satisfies Record<string, HarnessAd>;

export const ALL_ADS: readonly HarnessAd[] = Object.values(ADS);

const OCCUPATION = { conceptId: "DJh5_yyF_hEM", label: "Mjukvaru- och systemutvecklare m.fl." };
const REGION = "zdoY_6u5_Krt";
const MUNICIPALITY = { conceptId: "PVZL_BQT_XtL", label: "Göteborg" };
const PERMANENT = "PFZr_Syz_cUq";

const notAssessed = { verdict: "NotAssessed", matched: [], missing: [] };
const skillsNotAssessed = { ...notAssessed, conceptEvidence: { matched: [], missing: [] } };
const unstatedRegister = { ...notAssessed, cause: "PreferenceUnstated" };

export function matchDetail(occupationStated: boolean) {
  return occupationStated
    ? {
        grade: "Good",
        ssykOverlap: { verdict: "Match", matched: [OCCUPATION], missing: [], cause: null },
        titleSimilarity: notAssessed,
        regionFit: { verdict: "Match", matched: [MUNICIPALITY], missing: [], cause: null },
        employmentFit: { verdict: "Match", matchedConceptIds: [PERMANENT], missingConceptIds: [], cause: null },
        skillOverlap: skillsNotAssessed,
        mustHaveCoverage: skillsNotAssessed,
        niceToHaveCoverage: skillsNotAssessed,
      }
    : {
        grade: null,
        ssykOverlap: unstatedRegister,
        titleSimilarity: notAssessed,
        regionFit: unstatedRegister,
        employmentFit: { verdict: "NotAssessed", matchedConceptIds: [], missingConceptIds: [], cause: "PreferenceUnstated" },
        skillOverlap: skillsNotAssessed,
        mustHaveCoverage: skillsNotAssessed,
        niceToHaveCoverage: skillsNotAssessed,
      };
}

export const USER = { userId: id(900), email: "harness@example.test", roles: ["JobSeeker"] };

export function profile(occupationStated: boolean) {
  return {
    id: id(901),
    displayName: "Testperson",
    language: "sv",
    backgroundMatchNotificationsEnabled: false,
    digestCadence: "Weekly",
    followedCompanyNotificationsEnabled: false,
    createdAt: "2026-05-12T09:30:00+02:00",
    hasStatedDesiredOccupation: occupationStated,
    preferredOccupationGroups: occupationStated ? [OCCUPATION.conceptId] : [],
    preferredRegions: [],
    preferredMunicipalities: occupationStated ? [MUNICIPALITY.conceptId] : [],
    preferredRemote: false,
    preferredEmploymentTypes: occupationStated ? [PERMANENT] : [],
    preferredSkills: [],
    experienceYears: null,
    preferredOccupationExperience: [],
  };
}

export const TAXONOMY = {
  regions: [{ conceptId: REGION, label: "Västra Götalands län", municipalities: [MUNICIPALITY] }],
  occupationFields: [
    { conceptId: "apaJ_2ja_LuF", label: "Data/IT", occupationGroups: [{ conceptId: OCCUPATION.conceptId, label: "Mjukvaru- och systemutvecklare" }] },
  ],
  employmentTypes: [{ conceptId: PERMANENT, label: "Vanlig anställning" }],
  worktimeExtents: [{ conceptId: "6YE1_gAC_R2G", label: "Heltid" }],
};

export const DESCRIPTION = "Du utvecklar och förvaltar en tjänst som många använder varje dag.";
export const PUBLISHED_AT = "2026-10-01T08:00:00Z";
export const EXPIRES_AT = "2026-11-01T22:59:59Z";
