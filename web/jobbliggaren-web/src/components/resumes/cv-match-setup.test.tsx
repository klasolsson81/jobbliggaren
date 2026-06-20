import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";

const { updateMock, deriveMock, cvSuggestMock } = vi.hoisted(() => ({
  updateMock: vi.fn(),
  deriveMock: vi.fn(),
  cvSuggestMock: vi.fn(),
}));
vi.mock("@/lib/actions/match-preferences", () => ({
  updateMatchPreferencesAction: updateMock,
  deriveOccupationsAction: deriveMock,
  suggestOccupationsFromCvAction: cvSuggestMock,
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
      persistedEmploymentTypes={[]}
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
  cvSuggestMock.mockResolvedValue({ kind: "noCv" });
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

  it("öppnar wizarden från prompt-knappen", async () => {
    const user = userEvent.setup();
    renderSetup({ showPrompt: true });
    await user.click(screen.getByRole("button", { name: "Ställ in matchning" }));
    // Wizardens steg 1 monteras.
    expect(await screen.findByText("Steg 1 av 4")).toBeInTheDocument();
  });
});
