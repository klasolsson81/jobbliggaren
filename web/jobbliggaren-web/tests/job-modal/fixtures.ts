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
  /** #2191 — the employer's application page; served on the detail route only, never on the list. */
  readonly applicationUrl?: string;
};

export const ADS = {
  open: { id: id(101), title: "Systemutvecklare till kundportalen", company: "Logikfabriken AB", appliedBeforeVisit: false, saved: false, applicationUrl: "https://karriar.example.test/jobb/101" },
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
    notificationMailEnabled: true,
    topMatchMailEnabled: true,
    matchSummaryMailEnabled: true,
    notificationFeedMatchesEnabled: true,
    notificationFeedFollowedAdsEnabled: true,
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

/** `/matchningar`'s list: one background match, for the open ad. */
export const MATCHES = [
  { jobAdId: ADS.open.id, title: ADS.open.title, company: ADS.open.company, url: `https://example.test/annons/${ADS.open.id.slice(-3)}`, grade: "Good", createdAt: PUBLISHED_AT, isNew: false },
];

// ── #1663 PR 2 (ADR 0175 D7): Branschbevakningar on Översikt and on /foretag/branschbevakningar/[id]/nya ──
//
// Every shape below is one the API produces: `CompanyWatchCriterionDto` with the ad numbers its handler builds,
// `CriterionNewAdCountsDto` (null unless the last scan answered Scanned), and `NewCriterionAdsDto` as
// `ListNewCriterionAdsQueryHandler` returns it — `Refused`, `NotScanned` and `Nothing` carry no rows, no token and no
// truncation, and a cut window carries its token. The codes are real SCB codes, the labels and ads fictional.

export const criterionId = (n: number) => `16630000-0000-4000-8000-${String(n).padStart(12, "0")}`;
const criterionAdId = (n: number) => `16630000-0000-4000-8000-${String(n + 100_000).padStart(12, "0")}`;

const SNI = { programming: "62100", consulting: "62201", hosting: "63100" } as const;
const KOMMUN = { goteborg: "1480", harryda: "1401", stockholm: "0180" } as const;

/** The SCB reference tree the headings are derived from: one section, two divisions, three kommuner. */
export const CRITERION_REFERENCE = {
  sniVersion: "2025.v1",
  kommunVersion: "2026-01-01.v1",
  aliasVersion: "2025.alias.v1",
  demandVersion: "2026-09-05.v1",
  sni: [
    {
      code: "K",
      name: "TELEKOMMUNIKATION, DATAPROGRAMMERING, DATAKONSULTVERKSAMHET, DATAINFRASTRUKTUR OCH ANNAN INFORMATIONSVERKSAMHET",
      aliases: [],
      divisions: [
        {
          code: "62",
          name: "Dataprogrammering, datakonsultverksamhet o.d.",
          aliases: [],
          leaves: [
            { code: SNI.programming, name: "Dataprogrammering", aliases: [] },
            { code: SNI.consulting, name: "Datakonsultverksamhet", aliases: [] },
          ],
        },
        {
          code: "63",
          name: "Datainfrastruktur, databehandling, hosting och annan informationsverksamhet",
          aliases: [],
          leaves: [{ code: SNI.hosting, name: "Datainfrastruktur, databehandling, hosting o.d.", aliases: [] }],
        },
      ],
    },
  ],
  lan: [
    { code: "01", name: "Stockholm", kommuner: [{ code: KOMMUN.stockholm, name: "Stockholm" }] },
    { code: "14", name: "Västra Götaland", kommuner: [{ code: KOMMUN.harryda, name: "Härryda" }, { code: KOMMUN.goteborg, name: "Göteborg" }] },
  ],
};

/** A criterion whose company set the breadth gate materialised: both numbers exact. */
const countedNumbers = (ads: number, matching: number) => ({
  ads: { magnitude: ads, saturated: false, tooBroad: false, notMaterialised: false },
  matching: { count: matching, tooBroad: false, notMaterialised: false },
});

/** Refused by the breadth gate (`SetTooLarge`): no number on either arm, and its last scan answered TooBroad. */
const refusedNumbers = {
  ads: { magnitude: null, saturated: false, tooBroad: true, notMaterialised: false },
  matching: { count: null, tooBroad: true, notMaterialised: false },
};

type CriterionFixture = {
  readonly id: string;
  readonly label: string | null;
  readonly sniCodes: readonly string[];
  readonly municipalityCodes: readonly string[];
};

export const CRITERIA = {
  it: { id: criterionId(1), label: "IT-konsulter i Göteborg", sniCodes: [SNI.consulting], municipalityCodes: [KOMMUN.goteborg] },
  hosting: { id: criterionId(2), label: "Drift och hosting i Stockholm", sniCodes: [SNI.hosting], municipalityCodes: [KOMMUN.stockholm] },
  broad: { id: criterionId(3), label: "Hela it-branschen", sniCodes: [SNI.programming, SNI.consulting, SNI.hosting], municipalityCodes: [KOMMUN.goteborg, KOMMUN.harryda, KOMMUN.stockholm] },
  // DESIGN-reviewer D-4: a label at the domain's LabelMaxLength (120).
  longLabel: { id: criterionId(4), label: "Systemutvecklare och it-konsulter hos medelstora företag i Göteborg, Härryda och Stockholm som arbetar med molnplattform", sniCodes: [SNI.programming], municipalityCodes: [KOMMUN.goteborg] },
} as const satisfies Record<string, CriterionFixture>;

/** One row of `GET /api/v1/me/company-watch-criteria`: refused or counted, as its last scan answered. */
export function criterionListRow(criterion: CriterionFixture, refused = false) {
  return {
    id: criterion.id,
    sniCodes: criterion.sniCodes,
    municipalityCodes: criterion.municipalityCodes,
    label: criterion.label,
    createdAt: "2026-09-20T08:00:00+00:00",
    updatedAt: "2026-09-20T08:00:00+00:00",
    ...(refused ? refusedNumbers : countedNumbers(42, 5)),
  };
}

const identity = (criterion: CriterionFixture) => ({
  id: criterion.id,
  sniCodes: criterion.sniCodes,
  municipalityCodes: criterion.municipalityCodes,
  label: criterion.label,
});

const TITLES = ["Systemutvecklare", "Backendutvecklare", "Driftstekniker", "It-konsult", "Testledare", "Molnarkitekt"];
const COMPANIES = ["Logikfabriken AB", "Västkod AB", "Nordlager Data AB", "Kodkontoret i Göteborg AB"];

/** One new ad, n-th oldest; `matchesYou` as `ReadTimeMatching` graded it, or null when the user stated no occupation. */
function newAdRow(n: number, matchesYou: boolean | null) {
  const minute = String(n % 60).padStart(2, "0");
  const at = `2026-10-17T${String(6 + Math.floor(n / 60)).padStart(2, "0")}:${minute}:00+00:00`;
  return {
    ad: {
      id: criterionAdId(n),
      title: `${TITLES[n % TITLES.length]} ${n}`,
      companyName: COMPANIES[n % COMPANIES.length]!,
      url: `https://example.test/annons/k${n}`,
      source: "Platsbanken",
      status: "Active",
      publishedAt: at,
      expiresAt: EXPIRES_AT,
      createdAt: at,
    },
    matchesYou,
  };
}

/** A cut window, newest first, as the handler orders it. `matching` rows (of `count`) match; null = not assessed. */
function window(criterion: CriterionFixture, count: number, matching: number | null, token: string, truncated = false) {
  const rows = Array.from({ length: count }, (_, i) => newAdRow(i + 1, matching === null ? null : i < matching))
    .reverse();
  return { criterion: identity(criterion), rows, matchingAssessed: matching !== null, acknowledgedThrough: token, truncated, tooBroad: false, notMaterialised: false };
}

const closed = (criterion: CriterionFixture, tooBroad: boolean, notMaterialised: boolean) => ({
  criterion: identity(criterion),
  rows: [],
  matchingAssessed: false,
  acknowledgedThrough: null,
  truncated: false,
  tooBroad,
  notMaterialised,
});

/** The criterion each /nya state is read for. The long "m.fl." heading is the derived one: no label, two of each axis. */
const NYA = {
  assessed: { id: criterionId(11), label: null, sniCodes: [SNI.programming, SNI.hosting], municipalityCodes: [KOMMUN.goteborg, KOMMUN.stockholm] },
  noneMatching: { id: criterionId(12), label: "Backend i Göteborg", sniCodes: [SNI.programming], municipalityCodes: [KOMMUN.goteborg] },
  notAssessed: { id: criterionId(13), label: "Konsulter i Härryda", sniCodes: [SNI.consulting], municipalityCodes: [KOMMUN.harryda] },
  truncatedAssessed: { id: criterionId(14), label: "Programmering i Stockholm", sniCodes: [SNI.programming], municipalityCodes: [KOMMUN.stockholm] },
  truncatedNotAssessed: { id: criterionId(15), label: "Programmering i Göteborg", sniCodes: [SNI.programming], municipalityCodes: [KOMMUN.goteborg] },
  nothing: { id: criterionId(16), label: "Hosting i Göteborg", sniCodes: [SNI.hosting], municipalityCodes: [KOMMUN.goteborg] },
  tooBroad: { id: criterionId(17), label: "Hela it-branschen", sniCodes: [SNI.programming, SNI.consulting, SNI.hosting], municipalityCodes: [KOMMUN.goteborg, KOMMUN.harryda, KOMMUN.stockholm] },
  notMaterialised: { id: criterionId(18), label: "Nyss ändrad bevakning", sniCodes: [SNI.consulting], municipalityCodes: [KOMMUN.stockholm] },
  neverScanned: { id: criterionId(19), label: "Nyss skapad bevakning", sniCodes: [SNI.hosting], municipalityCodes: [KOMMUN.harryda] },
  error: { id: criterionId(20), label: "Läsningen misslyckas", sniCodes: [SNI.consulting], municipalityCodes: [KOMMUN.goteborg] },
  rateLimited: { id: criterionId(21), label: "Läsningen strypt", sniCodes: [SNI.consulting], municipalityCodes: [KOMMUN.goteborg] },
  foreign: { id: criterionId(22), label: "En annan användares bevakning", sniCodes: [SNI.consulting], municipalityCodes: [KOMMUN.goteborg] },
  slow: { id: criterionId(23), label: "Långsam läsning", sniCodes: [SNI.consulting], municipalityCodes: [KOMMUN.goteborg] },
} as const satisfies Record<string, CriterionFixture>;

type SiblingReads = Record<"companies" | "ad-count" | "ads", unknown>;
type CriterionScan = { readonly kind: "counted"; readonly ads: number; readonly matching: number | null } | { readonly kind: "refused" } | { readonly kind: "unmaterialised" };

/**
 * The reads behind a criterion's links, which a production build prefetches: the detail page (`/{id}/companies`,
 * `/{id}/ad-count`) behind `/nya`'s back link and the catalogue's rows, and `/{id}/ads` behind "Visa alla annonser".
 * Nothing is asserted on them; they are answered so that a page starting to read something new still shows up as a
 * miss. Each matches its criterion's scan state: refused and not materialised carry no ad number, not assessed
 * (`matching: null`) no matching one.
 */
function siblingReads(criterion: CriterionFixture, scan: CriterionScan): SiblingReads {
  const adNumbers = scan.kind === "refused"
    ? refusedNumbers
    : scan.kind === "unmaterialised"
      ? {
          ads: { magnitude: null, saturated: false, tooBroad: false, notMaterialised: true },
          matching: { count: null, tooBroad: false, notMaterialised: true },
        }
      : {
          ads: { magnitude: scan.ads, saturated: false, tooBroad: false, notMaterialised: false },
          matching: { count: scan.matching, tooBroad: false, notMaterialised: false },
        };
  const refused = scan.kind === "refused";
  // The register browse is a live read, so a refused criterion still pages its companies, at the saturated cap.
  const companies = Array.from({ length: refused ? 20 : 1 }, (_, i) => ({
    organizationNumber: `55900${String(i + 10).padStart(5, "0")}`,
    isProtectedIdentity: false,
    name: `${COMPANIES[i % COMPANIES.length]!.replace(/ AB$/, "")} ${i + 1} AB`,
    seatMunicipalityCode: criterion.municipalityCodes[0]!,
    seatMunicipalityName: null,
    sniCodes: [criterion.sniCodes[0]!],
  }));
  const total = scan.kind === "counted" ? scan.ads : 0;
  const shown = Array.from({ length: Math.min(total, 20) }, (_, i) => newAdRow(i + 1, null).ad);
  return {
    companies: {
      criterion: identity(criterion),
      companies: refused
        ? { items: companies, totalCount: 2000, page: 1, pageSize: 20, totalPages: 100 }
        : { items: companies, totalCount: 1, page: 1, pageSize: 20, totalPages: 1 },
      magnitude: refused ? { magnitude: 2000, saturated: true } : { magnitude: 1, saturated: false },
    },
    "ad-count": adNumbers,
    ads: {
      ads: { items: shown, totalCount: total, page: 1, pageSize: 20, totalPages: Math.ceil(total / 20) },
      magnitude: adNumbers.ads,
      matching: null,
      criterion: identity(criterion),
    },
  };
}

/** Every criterion's sibling reads, by id: the Översikt and catalogue watches, and each /nya state's but the foreign one. */
export function criterionSiblingAnswers(): Map<string, SiblingReads> {
  const counted = (matching: number | null): CriterionScan => ({ kind: "counted", ads: 2, matching });
  const scans: Record<Exclude<NyaState, "foreign">, CriterionScan> = {
    assessed: counted(1),
    noneMatching: counted(1),
    notAssessed: counted(null),
    truncatedAssessed: counted(1),
    truncatedNotAssessed: counted(null),
    nothing: counted(1),
    tooBroad: { kind: "refused" },
    notMaterialised: { kind: "unmaterialised" },
    neverScanned: { kind: "unmaterialised" },
    error: counted(1),
    rateLimited: counted(1),
    slow: counted(1),
  };
  const listed: Record<keyof typeof CRITERIA, CriterionScan> = {
    it: { kind: "counted", ads: 42, matching: 5 },
    hosting: { kind: "counted", ads: 42, matching: 5 },
    broad: { kind: "refused" },
    longLabel: { kind: "counted", ads: 42, matching: 5 },
  };
  return new Map([
    ...(Object.entries(scans) as [Exclude<NyaState, "foreign">, CriterionScan][]).map(
      ([state, scan]): [string, SiblingReads] => [NYA[state].id, siblingReads(NYA[state], scan)],
    ),
    ...(Object.entries(listed) as [keyof typeof CRITERIA, CriterionScan][]).map(
      ([key, scan]): [string, SiblingReads] => [CRITERIA[key].id, siblingReads(CRITERIA[key], scan)],
    ),
  ]);
}

/** The window tokens, microsecond-precise as the scan clock writes them: a token re-serialised through Date would lose them. */
export const NYA_TOKENS = {
  assessed: "2026-10-18T03:55:12.123456+00:00",
  noneMatching: "2026-10-18T03:55:13.000001+00:00",
  notAssessed: "2026-10-18T03:55:14.654321+00:00",
  truncatedAssessed: "2026-10-18T03:55:15.100001+00:00",
  truncatedNotAssessed: "2026-10-18T03:55:16.200002+00:00",
  slow: "2026-10-18T03:55:17.300003+00:00",
} as const;

export type NyaState = keyof typeof NYA;
export const NYA_IDS = Object.fromEntries(Object.entries(NYA).map(([state, c]) => [state, c.id])) as Record<NyaState, string>;

/** One answer of the fixture backend: the status, the body for a 200, and how long it waits first. */
export type FixtureAnswer = { readonly status: number; readonly body?: unknown; readonly delayMs?: number; readonly headers?: Record<string, string> };

/** `GET /{id}/new-ads` per criterion id: each /nya state, and the three refusals the route answers with a status. */
export function newCriterionAdsAnswers(): Map<string, FixtureAnswer> {
  const ok = (body: unknown, delayMs = 0): FixtureAnswer => ({ status: 200, body, delayMs });
  return new Map<string, FixtureAnswer>([
    [NYA.assessed.id, ok(window(NYA.assessed, 5, 2, NYA_TOKENS.assessed))],
    [NYA.noneMatching.id, ok(window(NYA.noneMatching, 3, 0, NYA_TOKENS.noneMatching))],
    [NYA.notAssessed.id, ok(window(NYA.notAssessed, 4, null, NYA_TOKENS.notAssessed))],
    [NYA.truncatedAssessed.id, ok(window(NYA.truncatedAssessed, 100, 7, NYA_TOKENS.truncatedAssessed, true))],
    [NYA.truncatedNotAssessed.id, ok(window(NYA.truncatedNotAssessed, 100, null, NYA_TOKENS.truncatedNotAssessed, true))],
    [NYA.nothing.id, ok(closed(NYA.nothing, false, false))],
    [NYA.tooBroad.id, ok(closed(NYA.tooBroad, true, false))],
    [NYA.notMaterialised.id, ok(closed(NYA.notMaterialised, false, true))],
    [NYA.neverScanned.id, ok(closed(NYA.neverScanned, false, true))],
    [NYA.error.id, { status: 500, body: { title: "Internal Server Error", status: 500 } }],
    [NYA.rateLimited.id, { status: 429, body: { title: "Too Many Requests", status: 429 }, headers: { "Retry-After": "60" } }],
    [NYA.foreign.id, { status: 404, body: { title: "Not Found", status: 404 } }],
    [NYA.slow.id, ok(window(NYA.slow, 2, 1, NYA_TOKENS.slow), 4_000)],
  ]);
}

/** A followed company with active and matching ads, so Bevakade företag carries its own follow pill. */
export const FOLLOWED_COMPANY = {
  id: "16630000-0000-4000-8000-000000000900",
  organizationNumber: "5590000001",
  isProtectedIdentity: false,
  companyName: "Logikfabriken AB",
  followedAt: "2026-09-01T08:00:00+00:00",
  activeAdCount: 4,
  matchingAdCount: 2,
  filter: null,
};
