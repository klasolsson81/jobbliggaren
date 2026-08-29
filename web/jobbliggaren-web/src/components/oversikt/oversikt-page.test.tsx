import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { OversiktPage } from "./oversikt-page";

import type { JobSeekerProfileDto } from "@/lib/dto/me";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { ListRecentSearchesResult } from "@/lib/dto/recent-searches";
import type { PipelineGroupDto } from "@/lib/dto/applications";
import type {
  CompanyWatch,
  ListCompanyWatchesResult,
} from "@/lib/dto/company-follows";
import type {
  ListSavedJobAdsResult,
  SavedJobAdDto,
} from "@/lib/dto/saved-job-ads";
import { DEFAULT_SORT_BY } from "@/lib/job-ads/search-params";
import { queryLabel } from "@/test/recent-search-label";
// Sidan renderar NoticeToolbar, vars uppdatera-kontroll kallar `useRouter()` (#1549).
// Utan mock kastar next/navigation "invariant expected app router to be mounted".
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: vi.fn() }),
}));

// next/link renderas som <a> i jsdom utan extra mock (Next client Link).
//
// #726 notiscenter: notiserna byggs per KÄLLA. Setup-kort ↔ match-notis är
// ÖMSESIDIGT uteslutande (profile.data.hasStatedDesiredOccupation). NoticeSection
// är client-lokalt localStorage-backat, så localStorage rensas mellan testen.

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

beforeEach(() => window.localStorage.clear());

describe("OversiktPage — setup-kort ↔ match-notis ömsesidig uteslutning", () => {
  it("hasStatedDesiredOccupation=false → setup-kort synligt, match-notis frånvarande", () => {
    renderOversikt(false);

    const nudgeCta = screen.getByRole("link", { name: /Ställ in matchning/ });
    // Epik #526 — kortet öppnar matchnings-setup-modalen via ?matchsetup=1.
    expect(nudgeCta).toHaveAttribute("href", "/oversikt?matchsetup=1");
    expect(
      screen.queryByRole("link", { name: /Visa annonser/ }),
    ).toBeNull();
  });

  it("hasStatedDesiredOccupation=true → match-notis synlig, setup-kort frånvarande", () => {
    renderOversikt(true);

    expect(
      screen.getByRole("link", { name: /Visa annonser/ }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: /Ställ in matchning/ }),
    ).toBeNull();
  });
});

describe("OversiktPage — live match-count (ADR 0079 STEG 6)", () => {
  it("count > 0 → live-copy med siffran", () => {
    const { container } = renderOversikt(true, { matchCount: 42 });

    expect(
      screen.getByText(/Det finns/, { selector: ".jp-notice__text" }),
    ).toBeInTheDocument();
    expect(screen.getByText("42")).toBeInTheDocument();
    const text = container.textContent ?? "";
    expect(text).not.toContain("143");
    expect(text).not.toContain("Mjukvaru- och systemutvecklare");
  });

  it("count > 0 → länken bär de sparade facetterna som hårda filter, INGA matchGrades (H2)", () => {
    renderOversikt(true, {
      matchCount: 42,
      profileOverrides: {
        preferredOccupationGroups: ["grp_dev"],
        preferredRegions: ["region_AB"],
        // TVÅ kommuner, inte en. Notisens href byggs genom `buildJobbHref` och
        // ÄRVER därför axel-serialiseringen (2026-08-01) utan en enda diff-rad
        // här — men vid ett värde per axel är den joinade formen byte-identisk
        // med den upprepade, så fixturen var blind för själva formskiftet
        // (design-reviewer, #1144). Detta är den enda ytan PR:en ändrade utan
        // att röra den.
        preferredMunicipalities: ["kommun_0180", "kommun_0181"],
        preferredEmploymentTypes: ["et_fast"],
      },
    });

    const cta = screen.getByRole("link", { name: /Visa annonser/ });
    expect(cta).toHaveAttribute(
      "href",
      "/jobb?occupationGroup=grp_dev&region=region_AB&municipality=kommun_0180.kommun_0181&employmentType=et_fast",
    );
  });

  it("count === 0 → nollstate-copy, notisen NOT dold, länken kvar", () => {
    renderOversikt(true, { matchCount: 0 });

    expect(
      screen.getByText(/inga annonser som matchar dina val just nu/),
    ).toBeInTheDocument();
    const cta = screen.getByRole("link", { name: /Visa annonser/ });
    expect(cta).toHaveAttribute("href", "/jobb");
  });

  it("count === null (fetch degraderade) → match-notis utelämnas, resten renderar", () => {
    renderOversikt(true, { matchCount: null });

    expect(
      screen.queryByRole("link", { name: /Visa annonser/ }),
    ).toBeNull();
    // Sidan renderar fortfarande — sektionshuvudena finns.
    expect(
      screen.getByRole("heading", { name: "Jobbannonser" }),
    ).toBeInTheDocument();
  });
});

describe("OversiktPage — deadline-notis (riktig expiresAt, #726)", () => {
  it("sparad annons med deadline inom fönstret → notis med företagsnamn och CTA till /sparade", () => {
    // Relativt today = new Date() i komponenten: +3 dagar ligger inom 7-dagarsfönstret.
    const soon = new Date(Date.now() + 3 * 86_400_000).toISOString();
    renderOversikt(true, {
      matchCount: null, // utelämna match-notisen så "Visa annonser" inte krockar
      savedJobAds: { kind: "ok", data: [makeSaved("Klarna", soon)] },
    });

    const cta = screen.getByRole("link", { name: /Visa sparade/ });
    expect(cta).toHaveAttribute("href", "/sparade");
    const row = cta.closest("li");
    expect(row).toHaveTextContent(/inom 7 dagar/);
    expect(row).toHaveTextContent("Klarna");
  });

  it("bara passerade deadlines → ingen deadline-notis", () => {
    const past = new Date(Date.now() - 3 * 86_400_000).toISOString();
    renderOversikt(true, {
      matchCount: null,
      savedJobAds: { kind: "ok", data: [makeSaved("Gammal", past)] },
    });
    expect(
      screen.queryByRole("link", { name: /Visa sparade/ }),
    ).toBeNull();
  });
});

describe("OversiktPage — företagsbevaknings-notis (#726)", () => {
  it("newFollowedCompanyAdCount > 0 → notis med CTA till /foretag", () => {
    renderOversikt(false, { newFollowedCompanyAdCount: 5 });

    const cta = screen.getByRole("link", { name: /Visa annonser/ });
    expect(cta).toHaveAttribute("href", "/foretag");
    const row = cta.closest("li");
    expect(row).toHaveTextContent("5");
    expect(row).toHaveTextContent(/nya annonser/);
  });

  it("newFollowedCompanyAdCount === 0 → ingen företagsbevaknings-notis", () => {
    renderOversikt(false, { newFollowedCompanyAdCount: 0 });
    expect(
      screen.queryByRole("link", { name: /Visa annonser/ }),
    ).toBeNull();
  });
});

describe("OversiktPage — senaste-sök-notis (#294, A′-relabel #726)", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("featurar senaste recent-search med replay-CTA", () => {
    // Notistexten hämtar counten lazy; en aldrig-resolvande stub håller den i
    // no-count-grenen så testet isolerar wiring (namn + href).
    vi.stubGlobal(
      "fetch",
      vi.fn(() => new Promise(() => {})),
    );

    renderOversikt(true, {
      matchCount: null, // utelämna match-notisen så CTA-namnen inte krockar
      recentSearches: {
        kind: "ok",
        data: [makeRecent({ label: queryLabel("Backend Stockholm"), q: "backend" })],
      },
    });

    const cta = screen.getByRole("link", { name: /Kör sökning/ });
    const href = cta.getAttribute("href") ?? "";
    expect(href).toMatch(/^\/jobb\?/);
    expect(href).toContain("q=backend");
    expect(screen.getByText(/Din senaste sökning:/)).toBeInTheDocument();
    expect(
      screen.getByText("Backend Stockholm", {
        selector: ".jp-notice__text b",
      }),
    ).toBeInTheDocument();
  });

  // #1548 var en KOMPOSITIONSdefekt: sammanfattningen fanns inte på sidan alls.
  // application-summary.test.tsx målar komponenten isolerat och skulle förblir
  // grön om summary-propen togs bort här eller om NoticeSection slutade rendera
  // sloten. Dessa två asserterar inkopplingen, inne i rätt sektion.
  it("ansökningssammanfattningen renderas inuti Mina ansökningar", () => {
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

    const section = screen.getByRole("region", { name: "Mina ansökningar" });
    expect(
      within(section).getByText("3 ansökningar · 2 aktiva"),
    ).toBeInTheDocument();
    expect(
      within(section).getByRole("list", { name: "Ansökningar per steg" }),
    ).toBeInTheDocument();
  });

  it("degraderad pipeline ger ingen siffra i sammanfattningen", () => {
    renderOversikt(true, { matchCount: null });

    const section = screen.getByRole("region", { name: "Mina ansökningar" });
    expect(
      within(section).getByText(/kunde inte hämtas/),
    ).toBeInTheDocument();
    expect(within(section).queryByText(/ansökningar ·/)).toBeNull();
  });

  // design-reviewer Major 1+2: sektionen får inte säga två saker om samma
  // olästa data. När sammanfattningen bär sektionens tillstånd ska notislistans
  // eget tomt-läge utebli — och vid en misslyckad hämtning även oläst-räknaren,
  // som annars räknar notiser som aldrig lästes.
  it("degraderad pipeline: varken oläst-räknare eller tomt-läge i sektionen", () => {
    renderOversikt(true, { matchCount: null });

    const section = screen.getByRole("region", { name: "Mina ansökningar" });
    expect(within(section).queryByText(/olästa/)).toBeNull();
    expect(
      within(section).queryByText(/Vi säger till när något händer/),
    ).toBeNull();
    expect(within(section).getByText(/kunde inte hämtas/)).toBeInTheDocument();
    // En tom <ul> renderar som en naken hårlinje; den ska inte finnas.
    expect(section.querySelector("ul.jp-notice-list")).toBeNull();
  });

  it("tomt konto: tomt-läget utelämnas, men oläst-räknaren står kvar", () => {
    renderOversikt(true, { matchCount: null, pipeline: { kind: "ok", data: [] } });

    const section = screen.getByRole("region", { name: "Mina ansökningar" });
    expect(
      within(section).queryByText(/Vi säger till när något händer/),
    ).toBeNull();
    expect(within(section).getByText("Du har inga ansökningar än")).toBeInTheDocument();
    // Källan lästes och höll inget, så noll olästa är ett MÄTT påstående.
    expect(within(section).getByText(/olästa/)).toBeInTheDocument();
  });

  it("populerat konto: notislistan bär sitt eget tomt-läge som förut", () => {
    renderOversikt(true, {
      matchCount: null,
      pipeline: {
        kind: "ok",
        data: [{ status: "Submitted", count: 2, applications: [] }],
      },
    });

    const section = screen.getByRole("region", { name: "Mina ansökningar" });
    expect(
      within(section).getByText(/Vi säger till när något händer/),
    ).toBeInTheDocument();
  });

  // #1558, samma kompositionsdefekt som #1548: company-summary.test.tsx målar
  // komponenten isolerat och förblir grön om `summary`-propen tas bort här. Dessa
  // asserterar inkopplingen, inne i rätt sektion.
  // Egen brytpunkt mot testet nedan: HÄR finns det en riktig notis (delta 5), så det här
  // mäter att sammanfattningen står TILLSAMMANS med en notisrad. Testet nedan mäter det
  // motsatta fallet (delta 0). Utan den skillnaden vore det ena en delmängd av det andra
  // och kunde inte falla av eget skäl (code-reviewer Minor 2).
  it("sammanfattningen står tillsammans med en notisrad, inte i stället för den", () => {
    renderOversikt(true, {
      matchCount: null,
      newFollowedCompanyAdCount: 5,
      companyWatches: { kind: "ok", data: [makeWatch()] },
    });

    const section = screen.getByRole("region", { name: "Företagsbevakning" });
    expect(
      within(section).getByText("1 bevakat företag · 136 aktiva annonser"),
    ).toBeInTheDocument();
    expect(within(section).getByText(/publicerat/)).toBeInTheDocument();
    expect(within(section).getByText(/1 oläst/)).toBeInTheDocument();
  });

  // Defekten issuet stänger, mätt på sidan: watermarken är avancerad (delta 0) men
  // kontot bevakar ett företag med 136 aktiva annonser. Före #1558 var sektionens enda
  // innehåll tomt-läget.
  it("delta 0 men levande bevakning: sektionen påstår inte längre att inget finns", () => {
    renderOversikt(true, {
      matchCount: null,
      newFollowedCompanyAdCount: 0,
      companyWatches: { kind: "ok", data: [makeWatch()] },
    });

    const section = screen.getByRole("region", { name: "Företagsbevakning" });
    expect(
      within(section).getByText("1 bevakat företag · 136 aktiva annonser"),
    ).toBeInTheDocument();
    expect(
      within(section).getByRole("link", { name: "Visa bevakade företag" }),
    ).toBeInTheDocument();
  });

  it("noll bevakningar: sammanfattningen äger tomt-läget, notislistans utelämnas", () => {
    renderOversikt(true, {
      matchCount: null,
      companyWatches: { kind: "ok", data: [] },
    });

    const section = screen.getByRole("region", { name: "Företagsbevakning" });
    expect(
      within(section).getByText("Du bevakar inga företag än"),
    ).toBeInTheDocument();
    expect(
      within(section).queryByText(/Händelser från dina bevakade företag/),
    ).toBeNull();
    // Källan lästes och höll inget, så noll olästa är ett mätt påstående.
    expect(within(section).getByText(/olästa/)).toBeInTheDocument();
  });

  // Skillnaden mot ansökningssektionen, och den är avsiktlig: notiserna och
  // sammanfattningen läser SKILDA källor här, så en fallen bevakningshämtning får inte
  // dölja oläst-räknaren — den räknar notiser vars egen källa lästes.
  it("oläsbara bevakningar: oläst-räknaren står kvar och notislistan behåller sitt tomt-läge", () => {
    renderOversikt(true, {
      matchCount: null,
      companyWatches: { kind: "error" },
    });

    const section = screen.getByRole("region", { name: "Företagsbevakning" });
    expect(
      within(section).getByText(/Bevakade företag kunde inte hämtas/),
    ).toBeInTheDocument();
    expect(within(section).getByText(/olästa/)).toBeInTheDocument();
    expect(
      within(section).getByText(/Händelser från dina bevakade företag/),
    ).toBeInTheDocument();
  });

  it("ingen recent-search → ingen senaste-sök-notis", () => {
    renderOversikt(true, {
      matchCount: null,
      recentSearches: { kind: "ok", data: [] },
    });
    expect(
      screen.queryByRole("link", { name: /Kör sökning/ }),
    ).toBeNull();
  });
});
