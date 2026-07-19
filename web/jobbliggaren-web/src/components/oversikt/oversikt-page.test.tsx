import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { OversiktPage } from "./oversikt-page";
import type { JobSeekerProfileDto } from "@/lib/dto/me";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { ListRecentSearchesResult } from "@/lib/dto/recent-searches";
import type {
  ListSavedJobAdsResult,
  SavedJobAdDto,
} from "@/lib/dto/saved-job-ads";
import { DEFAULT_SORT_BY } from "@/lib/job-ads/search-params";

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
  readonly profileOverrides?: Partial<JobSeekerProfileDto>;
}

function renderOversikt(
  hasStatedDesiredOccupation: boolean,
  {
    matchCount = 42,
    recentSearches = errored,
    savedJobAds = errored,
    newFollowedCompanyAdCount = 0,
    profileOverrides = {},
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
      pipeline={errored}
      savedJobAds={savedJobAds}
      recentSearches={recentSearches}
      matchCount={matchCount}
      newFollowedCompanyAdCount={newFollowedCompanyAdCount}
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
    occupationGroupLabels: [],
    municipalityLabels: [],
    regionLabels: [],
    sortBy: DEFAULT_SORT_BY,
    label: "Backend Stockholm",
    currentCount: 0,
    newCount: 0,
    lastViewedAt: "2026-06-27T10:00:00Z",
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
        preferredMunicipalities: ["kommun_0180"],
        preferredEmploymentTypes: ["et_fast"],
      },
    });

    const cta = screen.getByRole("link", { name: /Visa annonser/ });
    expect(cta).toHaveAttribute(
      "href",
      "/jobb?occupationGroup=grp_dev&region=region_AB&municipality=kommun_0180&employmentType=et_fast",
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
        data: [makeRecent({ label: "Backend Stockholm", q: "backend" })],
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
