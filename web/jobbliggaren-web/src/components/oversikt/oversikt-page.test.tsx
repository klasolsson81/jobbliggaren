import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OversiktPage } from "./oversikt-page";
import messages from "../../../messages/sv";

import type { JobSeekerProfileDto } from "@/lib/dto/me";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { ListRecentSearchesResult } from "@/lib/dto/recent-searches";
import type { ApplicationDto, PipelineGroupDto } from "@/lib/dto/applications";
import type {
  CompanyWatchCriterion,
  ListCompanyWatchCriteriaResult,
} from "@/lib/dto/company-criteria";
import type {
  CompanyWatch,
  ListCompanyWatchesResult,
} from "@/lib/dto/company-follows";
import type {
  ListSavedJobAdsResult,
  SavedJobAdDto,
} from "@/lib/dto/saved-job-ads";
import { buildCompanyJobsHref } from "@/lib/job-ads/company-jobs-href";
import { buildCriterionAdsHref } from "@/lib/company-criteria/criterion-ads-href";
import { DEFAULT_SORT_BY } from "@/lib/job-ads/search-params";
import { queryLabel } from "@/test/recent-search-label";
// Sidan renderar NoticeToolbar, vars uppdatera-kontroll kallar `useRouter()` (#1549).
// Utan mock kastar next/navigation "invariant expected app router to be mounted".
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: vi.fn() }),
}));

// next/link renderas som <a> i jsdom utan extra mock (Next client Link).
//
// ADR 0140: sidan är sex kort i ett rutnät. Notiserna byggs som förut per källa och delas på
// KIND: allt utom `info` går till Kräver dig, `info` till Senaste händelser. De fyra stående
// tillstånden är egna kort. Setup-läge ↔ matchtal är ÖMSESIDIGT uteslutande
// (profile.data.hasStatedDesiredOccupation). Listkorten är client-lokalt localStorage-backade,
// så localStorage rensas mellan testen.

const COPY = messages.oversikt;

const baseProfile: JobSeekerProfileDto = {
  id: "22222222-2222-2222-2222-222222222222",
  displayName: "Anna",
  language: "sv",
  backgroundMatchNotificationsEnabled: false,
  digestCadence: "Weekly",
  followedCompanyNotificationsEnabled: false,
  createdAt: "2026-05-11T10:00:00Z",
  hasStatedDesiredOccupation: false,
  preferredOccupationGroups: [],
  preferredRegions: [],
  preferredMunicipalities: [],
  preferredRemote: false,
  preferredEmploymentTypes: [],
  preferredSkills: [],
  experienceYears: null,
  preferredOccupationExperience: [],
};

const errored: ApiResult<never> = { kind: "error" };

interface RenderOpts {
  readonly matchCount?: number | null;
  readonly recentSearches?: ApiResult<ListRecentSearchesResult>;
  readonly savedJobAds?: ApiResult<ListSavedJobAdsResult>;
  readonly newFollowedCompanyAdCount?: number;
  readonly companyWatches?: ApiResult<ListCompanyWatchesResult>;
  readonly criteria?: ApiResult<ListCompanyWatchCriteriaResult>;
  readonly profileOverrides?: Partial<JobSeekerProfileDto>;
  readonly pipeline?: ApiResult<PipelineGroupDto[]>;
}

function renderOversikt(
  hasStatedDesiredOccupation: boolean,
  {
    matchCount = 42,
    recentSearches = errored,
    savedJobAds = errored,
    newFollowedCompanyAdCount = 0,
    companyWatches = errored,
    // `errored` som default, parity `companyWatches`: de här testerna mäter notiserna och de
    // andra korten, och en degraderad läsning ger ett kort med en en-dash i stället för ett
    // vars innehåll skulle sippra in i deras textassertions.
    criteria = errored,
    profileOverrides = {},
    pipeline = errored,
  }: RenderOpts = {},
) {
  const profile: ApiResult<JobSeekerProfileDto> = {
    kind: "ok",
    data: { ...baseProfile, hasStatedDesiredOccupation, ...profileOverrides },
  };
  return render(
    <OversiktPage
      email="anna@example.se"
      displayName="Anna"
      profile={profile}
      pipeline={pipeline}
      savedJobAds={savedJobAds}
      recentSearches={recentSearches}
      matchCount={matchCount}
      newFollowedCompanyAdCount={newFollowedCompanyAdCount}
      companyWatches={companyWatches}
      criteria={criteria}
      criterionReference={null}
    />,
  );
}

function makeRecent(
  overrides: Partial<ListRecentSearchesResult[number]> = {},
): ListRecentSearchesResult[number] {
  return {
    id: "33333333-3333-3333-3333-333333333333",
    q: "backend",
    occupationGroupList: [],
    municipalityList: [],
    regionList: [],
    employmentTypeList: [],
    worktimeExtentList: [],
    employerList: [],
    remote: false,
    occupationGroupLabels: [],
    municipalityLabels: [],
    regionLabels: [],
    sortBy: DEFAULT_SORT_BY,
    label: queryLabel("Backend Stockholm"),
    currentCount: 0,
    newCount: 0,
    lastViewedAt: "2026-06-27T10:00:00Z",
    ...overrides,
  };
}

function makeWatch(overrides: Partial<CompanyWatch> = {}): CompanyWatch {
  return {
    id: "44444444-4444-4444-4444-444444444444",
    organizationNumber: "5566524301",
    isProtectedIdentity: false,
    companyName: "Friday Väst AB",
    followedAt: "2026-07-01T10:00:00Z",
    activeAdCount: 136,
    matchingAdCount: 9,
    filter: null,
    ...overrides,
  };
}

function makeCriterion(overrides: Partial<CompanyWatchCriterion> = {}): CompanyWatchCriterion {
  return {
    id: "aaaa1111-0000-4000-8000-000000000001",
    sniCodes: ["62010"],
    municipalityCodes: ["1480"],
    label: "Utveckling i Göteborg",
    createdAt: "2026-08-01T08:00:00+00:00",
    updatedAt: "2026-08-01T08:00:00+00:00",
    ads: { magnitude: 42, saturated: false, tooBroad: false, notMaterialised: false },
    matching: { count: 7, tooBroad: false, notMaterialised: false },
    ...overrides,
  };
}

function makeSaved(company: string, expiresAt: string): SavedJobAdDto {
  return {
    id: `saved-${company}`,
    jobAdId: "ad-1",
    savedAt: "2026-05-01T00:00:00Z",
    jobAd: {
      jobAdId: "ad-1",
      title: `Roll hos ${company}`,
      company,
      url: null,
      source: "Platsbanken",
      publishedAt: null,
      expiresAt,
    },
  };
}

// An application in InterviewScheduled updated today — `findRecentInterviews` admits it
// (daysSince <= 1), so the page emits the one `brand`-kind notice there is.
function makeInterviewApp(): ApplicationDto {
  return {
    id: "55555555-5555-5555-5555-555555555555",
    jobSeekerId: baseProfile.id,
    jobAdId: "ad-2",
    status: "InterviewScheduled",
    createdAt: "2026-09-01T08:00:00Z",
    updatedAt: new Date().toISOString(),
    jobAd: {
      jobAdId: "ad-2",
      title: "Systemutvecklare",
      company: "Stena Line",
      url: null,
      source: "Platsbanken",
      publishedAt: null,
      expiresAt: null,
    },
  };
}

function card(name: string) {
  return screen.getByRole("region", { name });
}
function bigNumber(name: string): string {
  return (card(name).querySelector<HTMLElement>(".jp-ov-num__value")?.textContent ?? "").trim();
}
function text(el: Element | null): string {
  return (el?.textContent ?? "").replace(/\s+/g, " ").trim();
}

beforeEach(() => window.localStorage.clear());

describe("OversiktPage — kompositionen (ADR 0140)", () => {
  it("renderar sex kort i rutnätet, i handoffens ordning, var och ett namngivet ur katalogen", () => {
    const { container } = renderOversikt(true);
    const headings = [
      ...container.querySelectorAll<HTMLElement>(".jp-ov-grid > section.jp-ov-card > .jp-ov-card__head h2"),
    ].map((h) => h.textContent);
    expect(headings).toEqual([
      COPY.cards.requiresYou,
      COPY.cards.applications,
      COPY.cards.matching,
      COPY.companySummary.heading,
      COPY.criteriaSummary.heading,
      COPY.cards.events,
    ]);
    // No ledger sections survive on the app surface — they are the guest demo's now.
    expect(container.querySelector<HTMLElement>("section.jp-section")).toBeNull();
  });

  it("ETT kugghjul, i toolbar-raden, med alla nio typerna under sina tre källor", async () => {
    const user = userEvent.setup();
    const { container } = renderOversikt(true);
    const gears = screen.getAllByRole("button", { name: COPY.notices.settingsAria });
    expect(gears).toHaveLength(1);
    expect(container.querySelector<HTMLElement>(".jp-oversikt-toolbar .jp-section__gear")).toBe(gears[0]);

    await user.click(gears[0]!);
    const panel = screen.getByRole("group", { name: COPY.notices.settingsAria });
    expect(within(panel).getAllByRole("checkbox")).toHaveLength(9);
    expect(
      [...panel.querySelectorAll<HTMLElement>(".jp-notice-prefs__grouptitle")].map((e) => e.textContent),
    ).toEqual([
      COPY.notices.sectionApplications,
      COPY.notices.sectionJobAds,
      COPY.notices.sectionCompanies,
    ]);
  });

  it("'Markera alla' ligger EFTER rutnätet i DOM-ordning (#1557)", () => {
    const { container } = renderOversikt(true, { matchCount: 42 });
    const row = container.querySelector<HTMLElement>(".jp-notice-bulk");
    expect(row).not.toBeNull();
    expect(screen.getByRole("button", { name: /Markera alla som lästa/ })).toBeInTheDocument();
    const grid = container.querySelector<HTMLElement>(".jp-ov-grid")!;
    expect(grid.compareDocumentPosition(row!) & Node.DOCUMENT_POSITION_FOLLOWING).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    );
    expect(grid.contains(row!)).toBe(false);
  });
});

describe("OversiktPage — kind-splitten mellan Kräver dig och Senaste händelser", () => {
  it("varning och brand (intervju) går till Kräver dig; info går till händelserna", () => {
    const soon = new Date(Date.now() + 3 * 86_400_000).toISOString();
    renderOversikt(true, {
      matchCount: 42,
      savedJobAds: { kind: "ok", data: [makeSaved("Klarna", soon)] },
      pipeline: {
        kind: "ok",
        data: [{ status: "InterviewScheduled", count: 1, applications: [makeInterviewApp()] }],
      },
    });

    const requires = card(COPY.cards.requiresYou);
    const events = card(COPY.cards.events);
    expect(within(requires).getByRole("link", { name: /Visa sparade/ })).toBeInTheDocument();
    expect(within(requires).getByRole("link", { name: /Öppna ärende/ })).toBeInTheDocument();
    expect(within(requires).queryByRole("link", { name: /^Visa annonser/ })).toBeNull();
    expect(within(events).getByRole("link", { name: /^Visa annonser/ })).toBeInTheDocument();
    expect(within(events).queryByRole("link", { name: /Visa sparade/ })).toBeNull();
    expect(within(requires).getByText("2 olästa")).toBeInTheDocument();
    expect(within(events).getByText("1 oläst")).toBeInTheDocument();
  });

  it("utan notiser står båda listkorten kvar med sin tomrad, och Kräver dig tappar varningskanten", () => {
    renderOversikt(true, { matchCount: null });
    const requires = card(COPY.cards.requiresYou);
    expect(within(requires).getByText(COPY.cards.requiresYouEmpty)).toBeInTheDocument();
    expect(requires).toHaveAttribute("data-empty", "true");
    expect(within(card(COPY.cards.events)).getByText(COPY.cards.eventsEmpty)).toBeInTheDocument();
  });
});

describe("OversiktPage — setup-läge ↔ matchtal ömsesidig uteslutning (ADR 0076)", () => {
  it("hasStatedDesiredOccupation=false → setup-callouten tar Matchning-kortet; inget matchtal, ingen match-notis", () => {
    renderOversikt(false);

    const matching = card(COPY.cards.matching);
    const nudgeCta = within(matching).getByRole("link", { name: /Ställ in matchning/ });
    // Epik #526 — kortet öppnar matchnings-setup-modalen via ?matchsetup=1.
    expect(nudgeCta).toHaveAttribute("href", "/oversikt?matchsetup=1");
    expect(matching.querySelector<HTMLElement>(".jp-ov-num")).toBeNull();
    expect(screen.queryByRole("link", { name: COPY.cards.matchingCta })).toBeNull();
    expect(screen.queryByRole("link", { name: /^Visa annonser/ })).toBeNull();
  });

  it("hasStatedDesiredOccupation=true → matchtal + solid CTA i kortet, match-notis i händelserna, ingen setup-länk", () => {
    renderOversikt(true);

    expect(within(card(COPY.cards.matching)).getByRole("link", { name: COPY.cards.matchingCta })).toBeInTheDocument();
    expect(within(card(COPY.cards.events)).getByRole("link", { name: /^Visa annonser/ })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Ställ in matchning/ })).toBeNull();
  });
});

describe("OversiktPage — live match-count (ADR 0079 STEG 6)", () => {
  it("count > 0 → talet i kortet och live-copyn i notisen", () => {
    const { container } = renderOversikt(true, { matchCount: 42 });

    expect(bigNumber(COPY.cards.matching)).toBe("42");
    expect(screen.getByText(/Det finns/, { selector: ".jp-ov-event__text" })).toBeInTheDocument();
    const pageText = container.textContent ?? "";
    expect(pageText).not.toContain("143");
    expect(pageText).not.toContain("Mjukvaru- och systemutvecklare");
  });

  it("kortets CTA och notisens CTA bär SAMMA länk: de sparade facetterna som hårda filter, INGA matchGrades (H2)", () => {
    renderOversikt(true, {
      matchCount: 42,
      profileOverrides: {
        preferredOccupationGroups: ["grp_dev"],
        preferredRegions: ["region_AB"],
        // TVÅ kommuner, inte en: vid ett värde per axel är den joinade formen byte-identisk med
        // den upprepade, så fixturen vore blind för formskiftet (design-reviewer, #1144).
        preferredMunicipalities: ["kommun_0180", "kommun_0181"],
        preferredEmploymentTypes: ["et_fast"],
      },
    });

    const expected =
      "/jobb?occupationGroup=grp_dev&region=region_AB&municipality=kommun_0180.kommun_0181&employmentType=et_fast";
    expect(within(card(COPY.cards.matching)).getByRole("link", { name: COPY.cards.matchingCta })).toHaveAttribute(
      "href",
      expected,
    );
    expect(within(card(COPY.cards.events)).getByRole("link", { name: /^Visa annonser/ })).toHaveAttribute(
      "href",
      expected,
    );
  });

  it("count === 0 → 0 i kortet med nollcopyn, notisen INTE dold, länkarna kvar", () => {
    renderOversikt(true, { matchCount: 0 });

    expect(bigNumber(COPY.cards.matching)).toBe("0");
    expect(screen.getAllByText(/inga annonser som matchar dina val just nu/).length).toBeGreaterThanOrEqual(2);
    expect(within(card(COPY.cards.matching)).getByRole("link", { name: COPY.cards.matchingCta })).toHaveAttribute(
      "href",
      "/jobb",
    );
    expect(within(card(COPY.cards.events)).getByRole("link", { name: /^Visa annonser/ })).toHaveAttribute(
      "href",
      "/jobb",
    );
  });

  it("count === null (fetch degraderade) → en-dash utan CTA i kortet, ingen match-notis, resten renderar", () => {
    renderOversikt(true, { matchCount: null });

    expect(bigNumber(COPY.cards.matching)).toBe(COPY.cards.unmeasured);
    expect(within(card(COPY.cards.matching)).queryByRole("link")).toBeNull();
    expect(screen.queryByRole("link", { name: /^Visa annonser/ })).toBeNull();
    expect(screen.getByRole("heading", { name: COPY.cards.events })).toBeInTheDocument();
  });
});

describe("OversiktPage — deadline-notis (riktig expiresAt, #726)", () => {
  it("sparad annons med deadline inom fönstret → rad i Kräver dig med företagsnamn och CTA till /sparade", () => {
    // Relativt today = new Date() i komponenten: +3 dagar ligger inom 7-dagarsfönstret.
    const soon = new Date(Date.now() + 3 * 86_400_000).toISOString();
    renderOversikt(true, {
      matchCount: null,
      savedJobAds: { kind: "ok", data: [makeSaved("Klarna", soon)] },
    });

    const cta = within(card(COPY.cards.requiresYou)).getByRole("link", { name: /Visa sparade/ });
    expect(cta).toHaveAttribute("href", "/sparade");
    const row = cta.closest("li");
    expect(row).toHaveTextContent(/inom 7 dagar/);
    expect(row).toHaveTextContent("Klarna");
    expect(row).toHaveAttribute("data-kind", "warning");
  });

  it("bara passerade deadlines → ingen deadline-notis", () => {
    const past = new Date(Date.now() - 3 * 86_400_000).toISOString();
    renderOversikt(true, {
      matchCount: null,
      savedJobAds: { kind: "ok", data: [makeSaved("Gammal", past)] },
    });
    expect(screen.queryByRole("link", { name: /Visa sparade/ })).toBeNull();
  });
});

describe("OversiktPage — företagsbevaknings-notis (#726, #1547, #1576, ADR 0140)", () => {
  it("newFollowedCompanyAdCount > 0 → notis i händelserna vars CTA namnger de nya annonserna och går dit", () => {
    // ADR 0140: the CTA used to name the catalogue (/foretag/bevakade) while the number in the
    // text already went to the new ads (#1576). One notice, one destination now.
    renderOversikt(false, { newFollowedCompanyAdCount: 5 });

    const cta = within(card(COPY.cards.events)).getByRole("link", { name: COPY.notices.companiesCta });
    expect(cta).toHaveAttribute("href", "/foretag/bevakade/nya");
    const row = cta.closest("li");
    expect(row).toHaveTextContent("5");
    expect(row).toHaveTextContent(/nya annonser/);
  });

  it("talet självt är fortfarande länken till annonserna det räknar", () => {
    renderOversikt(false, { newFollowedCompanyAdCount: 5 });
    const countLink = within(card(COPY.cards.events)).getByRole("link", { name: "5 nya annonser" });
    expect(countLink).toHaveAttribute("href", "/foretag/bevakade/nya");
  });

  it("notisen bär inget org.nr och ingen employer-axel", () => {
    // Scoped to the row: the notice carries a scalar count only (ADR 0087 D8).
    renderOversikt(false, { newFollowedCompanyAdCount: 5 });
    const row = within(card(COPY.cards.events))
      .getByRole("link", { name: COPY.notices.companiesCta })
      .closest("li")!;
    expect(row.innerHTML).not.toContain("employer=");
    expect(row.innerHTML).not.toMatch(/\d{10}/);
  });

  it("newFollowedCompanyAdCount === 0 → ingen notis och ingen pill", () => {
    renderOversikt(false, {
      newFollowedCompanyAdCount: 0,
      companyWatches: { kind: "ok", data: [makeWatch()] },
    });
    expect(screen.queryByText(/Dina bevakade företag har publicerat/)).toBeNull();
    expect(card(COPY.companySummary.heading).querySelector<HTMLElement>(".jp-ov-card__pill")).toBeNull();
  });

  it("talet når också kortets pill, som länkar dit talet i notisen redan går", () => {
    renderOversikt(false, {
      newFollowedCompanyAdCount: 5,
      companyWatches: { kind: "ok", data: [makeWatch()] },
    });
    const pill = within(card(COPY.companySummary.heading)).getByRole("link", {
      name: /^5 nya annonser från bevakade företag/,
    });
    expect(pill).toHaveAttribute("href", "/foretag/bevakade/nya");
  });
});

describe("OversiktPage — senaste-sök-notis (#294, A′-relabel #726)", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("featurar senaste recent-search med replay-CTA i händelserna", () => {
    // Notistexten hämtar counten lazy; en aldrig-resolvande stub håller den i
    // no-count-grenen så testet isolerar wiring (namn + href).
    vi.stubGlobal(
      "fetch",
      vi.fn(() => new Promise(() => {})),
    );

    renderOversikt(true, {
      matchCount: null,
      recentSearches: {
        kind: "ok",
        data: [makeRecent({ label: queryLabel("Backend Stockholm"), q: "backend" })],
      },
    });

    const cta = within(card(COPY.cards.events)).getByRole("link", { name: /Kör sökning/ });
    const href = cta.getAttribute("href") ?? "";
    expect(href).toMatch(/^\/jobb\?/);
    expect(href).toContain("q=backend");
    expect(screen.getByText(/Din senaste sökning:/)).toBeInTheDocument();
    expect(
      screen.getByText("Backend Stockholm", { selector: ".jp-ov-event__text b" }),
    ).toBeInTheDocument();
  });

  it("ingen recent-search → ingen senaste-sök-notis", () => {
    renderOversikt(true, {
      matchCount: null,
      recentSearches: { kind: "ok", data: [] },
    });
    expect(screen.queryByRole("link", { name: /Kör sökning/ })).toBeNull();
  });
});

// ── WIRING. The card suites pass their props themselves and cannot see the call site, so a
// regression in `oversikt-page.tsx` — a wrong Result, a dropped prop, a stale href — survives all
// of them (#1115, #1717: measured by mutation). These pin the composition on the REAL page.

describe("OversiktPage — Mina ansökningar-kortets inkoppling", () => {
  it("talet, staplarna och CTA:n kommer ur pipeline-propen", () => {
    renderOversikt(true, {
      matchCount: null,
      pipeline: {
        kind: "ok",
        data: [
          { status: "Submitted", count: 2, applications: [] },
          { status: "Rejected", count: 1, applications: [] },
        ],
      },
    });
    const applications = card(COPY.cards.applications);
    expect(bigNumber(COPY.cards.applications)).toBe("2");
    expect(text(applications.querySelector<HTMLElement>(".jp-ov-num__unit"))).toBe("aktiva av 3 ansökningar");
    expect(within(applications).getByRole("list", { name: COPY.summary.stepsAriaLabel })).toBeInTheDocument();
    expect(within(applications).getByRole("link", { name: COPY.summary.link })).toHaveAttribute(
      "href",
      "/ansokningar",
    );
  });

  it("degraderad pipeline ger en en-dash, aldrig en nolla", () => {
    renderOversikt(true, { matchCount: null });
    expect(bigNumber(COPY.cards.applications)).toBe(COPY.cards.unmeasured);
    expect(within(card(COPY.cards.applications)).getByText(/kunde inte hämtas/)).toBeInTheDocument();
  });

  it("tomt konto: tomt-läget med sidans enda skapa-länk", () => {
    renderOversikt(true, { matchCount: null, pipeline: { kind: "ok", data: [] } });
    const applications = card(COPY.cards.applications);
    expect(within(applications).getByText(COPY.summary.emptyTitle)).toBeInTheDocument();
    expect(within(applications).getByRole("link", { name: COPY.summary.emptyCta })).toHaveAttribute(
      "href",
      "/ny-ansokan",
    );
    expect(screen.getAllByRole("link", { name: COPY.summary.emptyCta })).toHaveLength(1);
  });
});

describe("OversiktPage — Bevakade företag-kortets inkoppling", () => {
  it("summorna och länkarna kommer ur companyWatches-propen", () => {
    renderOversikt(true, {
      matchCount: null,
      companyWatches: { kind: "ok", data: [makeWatch()] },
    });
    const companies = card(COPY.companySummary.heading);
    expect(bigNumber(COPY.companySummary.heading)).toBe("9");
    expect(text(companies.querySelector<HTMLElement>(".jp-ov-sub"))).toBe("1 bevakat företag · 136 aktiva annonser");
    expect(within(companies).getByRole("link", { name: "136 aktiva annonser" })).toHaveAttribute(
      "href",
      buildCompanyJobsHref(["5566524301"], "all"),
    );
    expect(within(companies).getByRole("link", { name: COPY.cards.matchingCta })).toHaveAttribute(
      "href",
      buildCompanyJobsHref(["5566524301"], "matching"),
    );
  });

  it("noll bevakningar: kortet äger tomt-läget", () => {
    renderOversikt(true, { matchCount: null, companyWatches: { kind: "ok", data: [] } });
    expect(within(card(COPY.companySummary.heading)).getByText(COPY.companySummary.emptyTitle)).toBeInTheDocument();
  });

  it("oläsbara bevakningar: en-dash och ohämtbar-copy — en ohämtbar-rad är också information", () => {
    renderOversikt(true, { matchCount: null, companyWatches: { kind: "error" } });
    expect(bigNumber(COPY.companySummary.heading)).toBe(COPY.cards.unmeasured);
    expect(within(card(COPY.companySummary.heading)).getByText(COPY.companySummary.unavailable)).toBeInTheDocument();
  });
});

describe("OversiktPage — Branschbevakning-kortets inkoppling och reflow", () => {
  it("EN bevakning: span 4 med talet, namnet och länkarna; syskonen span 4", () => {
    renderOversikt(true, { matchCount: null, criteria: { kind: "ok", data: [makeCriterion()] } });
    const criteria = card(COPY.criteriaSummary.heading);
    expect(criteria).toHaveAttribute("data-span", "4");
    expect(bigNumber(COPY.criteriaSummary.heading)).toBe("7");
    expect(within(criteria).getByText("Utveckling i Göteborg")).toBeInTheDocument();
    expect(within(criteria).getByRole("link", { name: COPY.cards.matchingCta })).toHaveAttribute(
      "href",
      buildCriterionAdsHref("aaaa1111-0000-4000-8000-000000000001", 1, "matching"),
    );
    expect(card(COPY.cards.matching)).toHaveAttribute("data-span", "4");
    expect(card(COPY.companySummary.heading)).toHaveAttribute("data-span", "4");
  });

  it("TVÅ bevakningar: kortet tar hela raden med en rad per bevakning och ingen summa; syskonen breddas till span 6", () => {
    renderOversikt(true, {
      matchCount: null,
      criteria: {
        kind: "ok",
        data: [makeCriterion({ id: "a", label: "Första" }), makeCriterion({ id: "b", label: "Andra" })],
      },
    });
    const criteria = card(COPY.criteriaSummary.heading);
    expect(criteria).toHaveAttribute("data-span", "12");
    expect(criteria.querySelectorAll<HTMLElement>(".jp-ov-criteria__row")).toHaveLength(2);
    expect(criteria.querySelector<HTMLElement>(".jp-ov-num")).toBeNull();
    expect(criteria.textContent).not.toMatch(/\b14\b/);
    expect(card(COPY.cards.matching)).toHaveAttribute("data-span", "6");
    expect(card(COPY.companySummary.heading)).toHaveAttribute("data-span", "6");
  });

  it("oläsbara bevakningar: en-dash, och syskonen förblir span 4", () => {
    renderOversikt(true, { matchCount: null, criteria: { kind: "error" } });
    expect(bigNumber(COPY.criteriaSummary.heading)).toBe(COPY.cards.unmeasured);
    expect(card(COPY.cards.matching)).toHaveAttribute("data-span", "4");
  });
});

describe("OversiktPage — notis-id:ts dygnsgräns (#1557)", () => {
  it("stämplar notis-id med LÄSARENS dygn, inte UTC:s", async () => {
    // 2026-08-29T22:30:00Z är 2026-08-30 00:30 i Sverige (CEST): läsarens dygn har
    // vänt, UTC:s inte. Utan den här mätningen är ANROPSSTÄLLET omätt — alla
    // enhetstesterna för `swedishDateSlug` går gröna även om den här filen aldrig
    // anropar den.
    //
    // Id:t når inget DOM-attribut, så avfärdandets rundtur genom localStorage är enda
    // observabeln — klicket går alltså inte att undvika. Fake timers är skopade till det
    // här testet: deadline-testerna bygger sina fixturer ur riktig `Date.now()`.
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      vi.setSystemTime(new Date("2026-08-29T22:30:00Z"));
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
      // Allt utom matchningen är errored/noll i defaulterna, så match-notisen är
      // sidans enda avfärdbara rad och kontrollen är entydig.
      renderOversikt(true, { matchCount: 42 });

      await user.click(screen.getByRole("button", { name: "Markera som läst" }));

      const stored = JSON.parse(
        window.localStorage.getItem("jp-oversikt-dismissed-notices") ?? "[]",
      ) as string[];
      expect(stored).toContain("n-match-2026-08-30");
      expect(stored).not.toContain("n-match-2026-08-29");
    } finally {
      vi.useRealTimers();
    }
  });
});
