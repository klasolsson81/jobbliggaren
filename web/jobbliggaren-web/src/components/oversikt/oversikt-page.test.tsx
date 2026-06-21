import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { OversiktPage } from "./oversikt-page";
import type { JobSeekerProfileDto } from "@/lib/dto/me";
import type { ApiResult } from "@/lib/dto/_helpers";

// next/link renderas som <a> i jsdom utan extra mock (Next client Link).
//
// F4-12 PR-B (ADR 0076): setup-nudge ↔ mock-match-notis är ÖMSESIDIGT
// uteslutande, styrt av profile.data.hasStatedDesiredOccupation. Övriga
// data-källor sätts till `error` (degraderar → inga andra notiser) så bara
// matchnings-grenen driver utfallet och testet isolerar den invarianten.

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
};

const errored: ApiResult<never> = { kind: "error" };

function renderOversikt(hasStatedDesiredOccupation: boolean) {
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
    />
  );
}

describe("OversiktPage — matchnings-nudge ömsesidig uteslutning", () => {
  it("hasStatedDesiredOccupation=false → setup-nudge synlig, mock-match-notis frånvarande", () => {
    renderOversikt(false);

    const nudgeCta = screen.getByRole("link", { name: /Ställ in matchning/ });
    expect(nudgeCta).toHaveAttribute("href", "/installningar#matchning");

    // Mock-match-notisen (CTA "Visa annonser") får INTE finnas samtidigt.
    expect(
      screen.queryByRole("link", { name: /Visa annonser/ })
    ).toBeNull();
  });

  it("hasStatedDesiredOccupation=true → mock-match-notis synlig, setup-nudge frånvarande", () => {
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
