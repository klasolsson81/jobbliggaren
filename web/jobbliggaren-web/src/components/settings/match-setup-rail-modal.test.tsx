import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";

// Section server-actions mockade (no-ops) — vi driver rail-modalens EGNA beteenden
// (steg-navigering, save→Klart, skip, räknaren), inte sektionernas interna sök.
const {
  updateMock,
  countMock,
  cvSuggestMock,
  parsedSuggestMock,
  skillSearchMock,
  skillSuggestMock,
} = vi.hoisted(() => ({
  updateMock: vi.fn(),
  countMock: vi.fn(),
  cvSuggestMock: vi.fn(),
  parsedSuggestMock: vi.fn(),
  skillSearchMock: vi.fn(),
  skillSuggestMock: vi.fn(),
}));
vi.mock("@/lib/actions/match-preferences", () => ({
  updateMatchPreferencesAction: updateMock,
  deriveOccupationsAction: vi.fn(),
  suggestOccupationsFromCvAction: cvSuggestMock,
  suggestOccupationsFromParsedResumeAction: parsedSuggestMock,
  searchSkillsAction: skillSearchMock,
  suggestSkillsFromParsedResumeAction: skillSuggestMock,
}));
vi.mock("@/lib/hooks/use-draft-match-count", () => ({
  useDraftMatchCount: () => countMock(),
}));
// CvUploadForm (Start-steget) anropar useRouter → mocka next/navigation.
vi.mock("next/navigation", () => ({
  useRouter: () => ({
    push: vi.fn(),
    replace: vi.fn(),
    refresh: vi.fn(),
    prefetch: vi.fn(),
    back: vi.fn(),
  }),
  useSearchParams: () => new URLSearchParams(),
  usePathname: () => "/",
}));

import { MatchSetupRailModal } from "./match-setup-rail-modal";

const occupationFields: ReadonlyArray<TaxonomyOccupationField> = [
  {
    conceptId: "field_data",
    label: "Data/IT",
    occupationGroups: [{ conceptId: "grp_backend", label: "Backendutvecklare" }],
  },
];
const regions: ReadonlyArray<TaxonomyRegion> = [
  { conceptId: "region_sthlm", label: "Stockholms län", municipalities: [] },
];
const employmentTypes: ReadonlyArray<TaxonomyOption> = [
  { conceptId: "et_fast", label: "Tillsvidareanställning" },
];

function renderModal(
  overrides?: Partial<React.ComponentProps<typeof MatchSetupRailModal>>,
) {
  const onOpenChange = vi.fn();
  render(
    <MatchSetupRailModal
      open
      onOpenChange={onOpenChange}
      occupationFields={occupationFields}
      regions={regions}
      employmentTypes={employmentTypes}
      persistedOccupationGroups={[]}
      persistedRegions={[]}
      persistedMunicipalities={[]}
      persistedEmploymentTypes={[]}
      persistedSkills={[]}
      persistedOccupationExperience={[]}
      importCvHref="/cv/importera"
      {...overrides}
    />,
  );
  return { onOpenChange };
}

beforeEach(() => {
  updateMock.mockReset();
  countMock.mockReset();
  countMock.mockReturnValue({ count: 42, loading: false });
  // Section auto-suggest (autoSuggestFromCv) — no-CV shapes så sektionerna inte
  // kraschar på undefined (samma som cv-match-setup-testet).
  cvSuggestMock.mockResolvedValue({ kind: "noCv" });
  parsedSuggestMock.mockResolvedValue({ kind: "noCv" });
  skillSearchMock.mockResolvedValue({ success: true, options: [] });
  skillSuggestMock.mockResolvedValue({ kind: "noCv" });
});

describe("MatchSetupRailModal — steg-navigering", () => {
  it("öppnar på Start-steget (välkomst-pitch + Fortsätt)", () => {
    renderModal();
    expect(
      screen.getByRole("heading", { name: "Välkommen till Jobbliggaren" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Vi matchar Platsbankens annonser mot din profil"),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Fortsätt" })).toBeInTheDocument();
  });

  it("Nästa/Fortsätt stegar Start → Yrken", async () => {
    const user = userEvent.setup();
    renderModal();
    await user.click(screen.getByRole("button", { name: "Fortsätt" }));
    expect(screen.getByRole("heading", { name: "Yrken" })).toBeInTheDocument();
  });

  it("rail-raden hoppar direkt till valt steg (fri navigering)", async () => {
    const user = userEvent.setup();
    renderModal();
    // Rail-knappen "Granska" (label + meta i samma knapp).
    const granskaRail = screen
      .getAllByRole("button")
      .find((b) => b.textContent?.includes("Granska"));
    expect(granskaRail).toBeDefined();
    await user.click(granskaRail!);
    expect(
      screen.getByRole("heading", { name: "Granska och spara" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Spara matchning" }),
    ).toBeInTheDocument();
  });
});

describe("MatchSetupRailModal — skip och stäng", () => {
  it("'Gör det senare' på Start stänger utan att spara", async () => {
    const user = userEvent.setup();
    const { onOpenChange } = renderModal();
    await user.click(screen.getByRole("button", { name: "Gör det senare" }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(updateMock).not.toHaveBeenCalled();
  });
});

describe("MatchSetupRailModal — ett save på slutet → Klart-läget", () => {
  it("Spara matchning kallar full-replace-PUT och visar Klart", async () => {
    updateMock.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    renderModal({ persistedOccupationGroups: ["grp_backend"] });

    const granskaRail = screen
      .getAllByRole("button")
      .find((b) => b.textContent?.includes("Granska"));
    await user.click(granskaRail!);
    await user.click(screen.getByRole("button", { name: "Spara matchning" }));

    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(1));
    // Full-replace-payloaden bär alla dimensioner (draften seedad från persisted).
    expect(updateMock.mock.calls[0]![0]).toMatchObject({
      preferredOccupationGroups: ["grp_backend"],
    });
    // Klart-läget efter lyckad save.
    await waitFor(() =>
      expect(
        screen.getByRole("heading", { name: "Matchningen är sparad" }),
      ).toBeInTheDocument(),
    );
    // Footer-"Stäng" finns (Radix-kryssets aria-label är också "Stäng" → getAll).
    expect(
      screen.getAllByRole("button", { name: "Stäng" }).length,
    ).toBeGreaterThan(0);
  });
});

describe("MatchSetupRailModal — live räknare", () => {
  it("visar talet när räknaren har ett värde", () => {
    countMock.mockReturnValue({ count: 42, loading: false });
    renderModal();
    expect(screen.getByText("MATCHAR NU")).toBeInTheDocument();
    // Två live-regioner (rail-kort + mobil-remsa; CSS döljer en per layout,
    // jsdom applicerar ingen media-query) → någon av dem bär talet.
    const statuses = screen.getAllByRole("status");
    expect(statuses.some((s) => s.textContent?.includes("42"))).toBe(true);
  });

  it("visar en neutral platshållare (aldrig 0) när räknaren är null", () => {
    countMock.mockReturnValue({ count: null, loading: true });
    renderModal();
    expect(screen.getByText("MATCHAR NU")).toBeInTheDocument();
    const statuses = screen.getAllByRole("status");
    expect(statuses.every((s) => !s.textContent?.includes("0"))).toBe(true);
  });
});
