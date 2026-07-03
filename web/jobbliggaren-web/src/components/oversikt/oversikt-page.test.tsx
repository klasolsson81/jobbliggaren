import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { OversiktPage } from "./oversikt-page";
import type { JobSeekerProfileDto } from "@/lib/dto/me";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { ListRecentSearchesResult } from "@/lib/dto/recent-searches";
import { DEFAULT_SORT_BY } from "@/lib/job-ads/search-params";

// next/link renderas som <a> i jsdom utan extra mock (Next client Link).
//
// F4-12 PR-B (ADR 0076): setup-nudge ↔ match-notis är ÖMSESIDIGT uteslutande,
// styrt av profile.data.hasStatedDesiredOccupation. Övriga data-källor sätts
// till `error` (degraderar → inga andra notiser) så bara matchnings-grenen
// driver utfallet och testet isolerar den invarianten.
//
// ADR 0079 STEG 6: notisens siffra är nu LIVE (prop `matchCount`), inte mock-143.
// Länkens grad-set MÅSTE vara counten:s grad-set (Good+Strong) — trust-invariant.

const baseProfile: JobSeekerProfileDto = {
  id: "22222222-2222-2222-2222-222222222222",
  displayName: "Anna",
  language: "sv",
  backgroundMatchNotificationsEnabled: false,
  digestCadence: "Weekly",
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

function renderOversikt(
  hasStatedDesiredOccupation: boolean,
  matchCount: number | null = 42,
  newMatchCount = 0,
  recentSearches: ApiResult<ListRecentSearchesResult> = errored,
  profileOverrides: Partial<JobSeekerProfileDto> = {}
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
      savedJobAds={errored}
      recentSearches={recentSearches}
      resumes={errored}
      landingStats={null}
      matchCount={matchCount}
      newMatchCount={newMatchCount}
    />
  );
}

function makeRecent(
  overrides: Partial<ListRecentSearchesResult[number]> = {}
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

describe("OversiktPage — matchnings-nudge ömsesidig uteslutning", () => {
  it("hasStatedDesiredOccupation=false → setup-nudge synlig, match-notis frånvarande", () => {
    renderOversikt(false);

    const nudgeCta = screen.getByRole("link", { name: /Ställ in matchning/ });
    // Epik #526 — notisen öppnar matchnings-setup-modalen via ?matchsetup=1.
    expect(nudgeCta).toHaveAttribute("href", "/oversikt?matchsetup=1");

    // Match-notisen (CTA "Visa annonser") får INTE finnas samtidigt.
    expect(
      screen.queryByRole("link", { name: /Visa annonser/ })
    ).toBeNull();
  });

  it("hasStatedDesiredOccupation=true → match-notis synlig, setup-nudge frånvarande", () => {
    renderOversikt(true);

    expect(
      screen.getByRole("link", { name: /Visa annonser/ })
    ).toBeInTheDocument();

    // Setup-nudgen (CTA "Ställ in matchning") får INTE finnas samtidigt.
    expect(
      screen.queryByRole("link", { name: /Ställ in matchning/ })
    ).toBeNull();
  });
});

describe("OversiktPage — live match-count (ADR 0079 STEG 6)", () => {
  it("count > 0 → live-copy med siffran, ingen mock-143 / 'sedan i tisdags' / segment", () => {
    const { container } = renderOversikt(true, 42);

    expect(
      screen.getByText(/Det finns/, { selector: ".jp-notice__text" })
    ).toBeInTheDocument();
    // Live-siffran renderas (i <b>).
    expect(screen.getByText("42")).toBeInTheDocument();

    const text = container.textContent ?? "";
    // Mock-spår får inte finnas kvar.
    expect(text).not.toContain("143");
    expect(text).not.toContain("sedan i tisdags");
    expect(text).not.toContain("Mjukvaru- och systemutvecklare");
  });

  it("count > 0 → länken bär de sparade facetterna som hårda filter, INGA matchGrades (H2)", () => {
    renderOversikt(true, 42, 0, errored, {
      preferredOccupationGroups: ["grp_dev"],
      preferredRegions: ["region_AB"],
      preferredMunicipalities: ["kommun_0180"],
      preferredEmploymentTypes: ["et_fast"],
    });

    const cta = screen.getByRole("link", { name: /Visa annonser/ });
    // Trust-invariant (harmoniserad 2026-07-03, Klas "samma siffra"; CTO H2):
    // länken bär EXAKT samma facetter som backend-counten hård-filtrerar på och
    // inga matchGrades — /jobb-landningens TotalCount == notis-talet == setup-
    // räknaren per konstruktion.
    expect(cta).toHaveAttribute(
      "href",
      "/jobb?occupationGroup=grp_dev&region=region_AB&municipality=kommun_0180&employmentType=et_fast"
    );
  });

  it("count === 0 → nollstate-copy, notisen NOT dold, länken kvar", () => {
    renderOversikt(true, 0);

    expect(
      screen.getByText(/inga annonser som matchar dina val just nu/)
    ).toBeInTheDocument();
    // Notisen ska fortfarande renderas med en fungerande länk (tomma facetter →
    // ren /jobb; profilen i detta test har inga sparade orter/former).
    const cta = screen.getByRole("link", { name: /Visa annonser/ });
    expect(cta).toHaveAttribute("href", "/jobb");
  });

  it("count === null (fetch degraderade) → match-notis utelämnas, resten renderar", () => {
    renderOversikt(true, null);

    // Match-notisen finns inte (varken live- eller nollstate-copy).
    expect(
      screen.queryByRole("link", { name: /Visa annonser/ })
    ).toBeNull();
    // Men sidan renderar fortfarande (Sammanfattnings-rubriken finns).
    expect(
      screen.getByRole("heading", { name: /Sammanfattning/ })
    ).toBeInTheDocument();
  });

  it("hasStatedDesiredOccupation=false → setup-nudge oförändrad även med live count", () => {
    renderOversikt(false, 42);

    expect(
      screen.getByRole("link", { name: /Ställ in matchning/ })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: /Visa annonser/ })
    ).toBeNull();
  });
});

describe("OversiktPage — Sammanfattnings-rad 'Nya matchningar' (ADR 0080 Vag 4)", () => {
  it("renderar live newMatchCount och länkar raden till /matchningar", () => {
    renderOversikt(true, 42, 7);

    const row = screen.getByRole("link", { name: /Nya matchningar/ });
    expect(row).toHaveAttribute("href", "/matchningar");
    expect(row).toHaveTextContent("7");
    // Inte längre länkad till /jobb och ingen "i dag"-etikett (mock-spår borta).
    expect(row).not.toHaveAttribute("href", "/jobb");
    expect(row).not.toHaveTextContent(/i dag/i);
  });

  it("newMatchCount === 0 (honest fallback) → raden visar 0, länken kvar", () => {
    renderOversikt(true, 42, 0);

    const row = screen.getByRole("link", { name: /Nya matchningar/ });
    expect(row).toHaveAttribute("href", "/matchningar");
    expect(row).toHaveTextContent("0");
  });

  it("4-siffrig newMatchCount → svensk tusenavgränsning '1 234' (inte '1234')", () => {
    // Regression: rendered-verify 2026-06-24 fann att raden saknade
    // tusenavgränsaren (renderade "1234") medan syskon-raden formaterade.
    renderOversikt(true, 42, 1234);

    const row = screen.getByRole("link", { name: /Nya matchningar/ });
    // `not.toHaveTextContent("1234")` är den bitande assertionen (gammal kod
    // renderade "1234" → failar).
    expect(row).not.toHaveTextContent("1234");
    // Lås separator-TYPEN, inte bara grupperingen: jest-dom normaliserar
    // whitespace i toHaveTextContent (U+00A0 → " "), så assertera rått
    // textContent direkt mot en non-breaking space (CLAUDE.md §10).
    const value = row.querySelector(".jp-summary__row__value");
    expect(value?.textContent).toBe("1 234");
  });

  it("mock-28 ('matchCountToday') yttas inte längre i Sammanfattningen", () => {
    const { container } = renderOversikt(true, 42, 7);
    // The old matchCountToday mock (28) must not leak into the SUMMARY as a value.
    // Assert the row VALUE cells, not the section's full textContent: the section
    // carries a "registrerat per YYYY-MM-DD" sub-header that legitimately contains
    // the day-of-month — which is "28" on the 28th of any month. The #303 fix
    // scoped to the section but the date header lives INSIDE it, so the flake
    // survived (red FE-CI for the whole team on the 28th). Scoping to the
    // `.jp-summary__row__value` cells excludes the date header entirely and keeps
    // the assertion's real intent: no summary row surfaces the mock value 28.
    const summary = container.querySelector(
      '[aria-labelledby="oversikt-sammanfattning"]',
    );
    expect(summary).not.toBeNull();
    const rowValues = Array.from(
      summary?.querySelectorAll(".jp-summary__row__value") ?? [],
    ).map((el) => el.textContent ?? "");
    expect(rowValues).not.toContain("28");
  });
});

describe("OversiktPage — sparad-sök-notis (#294)", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("featurar senaste recent-search med replay-CTA (kör sökningen, ej /sokningar, ej mock-namn)", () => {
    // The notice text lazily fetches the count; a never-resolving stub keeps it
    // in the no-count branch so this test isolates the wiring (name + href).
    vi.stubGlobal(
      "fetch",
      vi.fn(() => new Promise(() => {})),
    );

    renderOversikt(true, 42, 0, {
      kind: "ok",
      data: [makeRecent({ label: "Backend Stockholm", q: "backend" })],
    });

    const cta = screen.getByRole("link", { name: /Kör sökning/ });
    const href = cta.getAttribute("href") ?? "";
    // CTA now RUNS the search on /jobb (replay href) — not the old wrong
    // destination /sokningar, and not a double-step.
    expect(href).toMatch(/^\/jobb\?/);
    expect(href).toContain("q=backend");
    expect(href).not.toBe("/sokningar");

    // Real recent-search name shown in the notice (the name also appears in the
    // Summary, so scope to the notice text); the old hardcoded mock name is gone.
    expect(screen.getByText(/Din senaste sökning:/)).toBeInTheDocument();
    expect(
      screen.getByText("Backend Stockholm", {
        selector: ".jp-notice__text b",
      }),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Remote \/ Distansjobb/)).toBeNull();
  });

  it("ingen recent-search → ingen sparad-sök-notis", () => {
    renderOversikt(true, 42, 0, { kind: "ok", data: [] });
    expect(
      screen.queryByRole("link", { name: /Kör sökning/ }),
    ).toBeNull();
  });
});
