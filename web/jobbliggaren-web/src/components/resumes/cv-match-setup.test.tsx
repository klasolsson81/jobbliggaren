import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";

const {
  updateMock,
  deriveMock,
  cvSuggestMock,
  parsedSuggestMock,
  skillSearchMock,
  skillSuggestMock,
} = vi.hoisted(() => ({
  updateMock: vi.fn(),
  deriveMock: vi.fn(),
  cvSuggestMock: vi.fn(),
  parsedSuggestMock: vi.fn(),
  skillSearchMock: vi.fn(),
  skillSuggestMock: vi.fn(),
}));
vi.mock("@/lib/actions/match-preferences", () => ({
  updateMatchPreferencesAction: updateMock,
  deriveOccupationsAction: deriveMock,
  suggestOccupationsFromCvAction: cvSuggestMock,
  suggestOccupationsFromParsedResumeAction: parsedSuggestMock,
  searchSkillsAction: skillSearchMock,
  suggestSkillsFromParsedResumeAction: skillSuggestMock,
}));

import { CvMatchSetup } from "./cv-match-setup";

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

function renderSetup(
  overrides?: Partial<React.ComponentProps<typeof CvMatchSetup>>
) {
  render(
    <CvMatchSetup
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
      hasPreferences={false}
      showPrompt={false}
      {...overrides}
    />
  );
}

beforeEach(() => {
  updateMock.mockReset();
  cvSuggestMock.mockReset();
  parsedSuggestMock.mockReset();
  skillSearchMock.mockReset();
  skillSuggestMock.mockReset();
  cvSuggestMock.mockResolvedValue({ kind: "noCv" });
  parsedSuggestMock.mockResolvedValue({ kind: "noCv" });
  skillSearchMock.mockResolvedValue({ success: true, options: [] });
  skillSuggestMock.mockResolvedValue({ kind: "noCv" });
});

describe("CvMatchSetup — trigger-copy", () => {
  it("utan preferenser → 'Skapa matchning från ditt CV'", () => {
    renderSetup({ hasPreferences: false });
    expect(
      screen.getByRole("button", { name: "Skapa matchning från ditt CV" })
    ).toBeInTheDocument();
  });

  it("med preferenser → 'Uppdatera matchning'", () => {
    renderSetup({ hasPreferences: true });
    expect(
      screen.getByRole("button", { name: "Uppdatera matchning" })
    ).toBeInTheDocument();
  });
});

describe("CvMatchSetup — post-promote-prompt (design C.3)", () => {
  it("dold när showPrompt=false", () => {
    renderSetup({ showPrompt: false });
    expect(
      screen.queryByText(/Vill du uppdatera din matchning utifrån det här CV/)
    ).toBeNull();
  });

  it("visas när showPrompt=true, utan utropstecken", () => {
    renderSetup({ showPrompt: true });
    const title = screen.getByText(
      "Vill du uppdatera din matchning utifrån det här CV:t?"
    );
    expect(title).toBeInTheDocument();
    expect(title.textContent).not.toContain("!");
  });

  it("kan stängas (dismissbar)", async () => {
    const user = userEvent.setup();
    renderSetup({ showPrompt: true });
    await user.click(screen.getByRole("button", { name: "Stäng" }));
    expect(
      screen.queryByText(/Vill du uppdatera din matchning utifrån det här CV/)
    ).toBeNull();
  });

  it("öppnar modalen från prompt-knappen", async () => {
    const user = userEvent.setup();
    renderSetup({ showPrompt: true });
    await user.click(screen.getByRole("button", { name: "Ställ in matchning" }));
    // Epik #526: rail-modalen öppnar på Yrken (initialStep=1) för /cv-vägen.
    expect(
      await screen.findByRole("heading", { name: "Yrken" })
    ).toBeInTheDocument();
  });
});

describe("CvMatchSetup — sparade skill-labels (#422, #253/#277-regr)", () => {
  // #422: /cv trådade `persistedSkills` (platta ids) men INTE `persistedSkillGroups`
  // (de reverse-resolvade labels-grupperna) → wizardens steg 2/5 renderade råa
  // concept-id:n för en återvändande användares sparade kompetenser på kall laddning.
  // Detta pinnar att gruppen nu trådas hela vägen CvMatchSetup → MatchSetupWizard.
  it("kall laddning: steg 2 visar den svenska labeln, ALDRIG det råa concept-id:t", async () => {
    const user = userEvent.setup();
    renderSetup({
      hasPreferences: true,
      persistedSkills: ["kZdb_kS6_xyz"],
      persistedSkillGroups: [
        {
          conceptId: "kZdb_kS6_xyz",
          label: "Systemutveckling",
          memberConceptIds: ["kZdb_kS6_xyz"],
        },
      ],
    });

    await user.click(
      screen.getByRole("button", { name: "Uppdatera matchning" })
    );
    // Steg 1 → steg 2 (Kompetenser).
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    expect(
      screen.getByRole("heading", { name: "Kompetenser" })
    ).toBeInTheDocument();

    // Chippen bär labeln (seedad via persistedSkillGroups), inte concept-id:t.
    expect(await screen.findByText("Systemutveckling")).toBeInTheDocument();
    expect(screen.queryByText("kZdb_kS6_xyz")).toBeNull();
    // Ingen CV-auto-suggest fyrar (CvMatchSetup skickar aldrig parsedResumeId).
    expect(skillSuggestMock).not.toHaveBeenCalled();
  });
});
