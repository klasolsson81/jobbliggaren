import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { OversiktPage } from "./oversikt-page";
import type { JobSeekerProfileDto } from "@/lib/dto/me";
import type { ApiResult } from "@/lib/dto/_helpers";

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
  emailNotifications: true,
  weeklySummary: false,
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
  newMatchCount = 0
) {
  const profile: ApiResult<JobSeekerProfileDto> = {
    kind: "ok",
    data: { ...baseProfile, hasStatedDesiredOccupation },
  };
  return render(
    <OversiktPage
      email="anna@example.se"
      displayName="Anna"
      profile={profile}
      pipeline={errored}
      savedJobAds={errored}
      recentSearches={errored}
      resumes={errored}
      landingStats={null}
      matchCount={matchCount}
      newMatchCount={newMatchCount}
    />
  );
}

describe("OversiktPage — matchnings-nudge ömsesidig uteslutning", () => {
  it("hasStatedDesiredOccupation=false → setup-nudge synlig, match-notis frånvarande", () => {
    renderOversikt(false);

    const nudgeCta = screen.getByRole("link", { name: /Ställ in matchning/ });
    expect(nudgeCta).toHaveAttribute("href", "/installningar#matchning");

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

  it("count > 0 → länken bär grad-koherent enum-namn-URL (Good+Strong)", () => {
    renderOversikt(true, 42);

    const cta = screen.getByRole("link", { name: /Visa annonser/ });
    // Trust-invariant: länkens grad-set = counten:s grad-set (HeadlineGrades).
    expect(cta).toHaveAttribute(
      "href",
      "/jobb?matchGrades=Good&matchGrades=Strong"
    );
  });

  it("count === 0 → nollstate-copy, notisen NOT dold, länken kvar", () => {
    renderOversikt(true, 0);

    expect(
      screen.getByText(/inga jobb som matchar din profil just nu/)
    ).toBeInTheDocument();
    // Notisen ska fortfarande renderas med en fungerande länk.
    const cta = screen.getByRole("link", { name: /Visa annonser/ });
    expect(cta).toHaveAttribute(
      "href",
      "/jobb?matchGrades=Good&matchGrades=Strong"
    );
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

  it("mock-28 ('matchCountToday') yttas inte längre i Sammanfattningen", () => {
    const { container } = renderOversikt(true, 42, 7);
    const text = container.textContent ?? "";
    expect(text).not.toContain("28");
  });
});
