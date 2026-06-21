import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import type { CvSuggestResult } from "@/lib/actions/match-preferences";

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

import { MatchSetupWizard } from "./match-setup-wizard";

const occupationFields: ReadonlyArray<TaxonomyOccupationField> = [
  {
    conceptId: "field_data",
    label: "Data/IT",
    occupationGroups: [
      { conceptId: "grp_backend", label: "Backendutvecklare" },
      { conceptId: "grp_frontend", label: "Frontendutvecklare" },
    ],
  },
];
const regions: ReadonlyArray<TaxonomyRegion> = [
  { conceptId: "region_sthlm", label: "Stockholms län", municipalities: [] },
];
const employmentTypes: ReadonlyArray<TaxonomyOption> = [
  { conceptId: "et_fast", label: "Tillsvidareanställning" },
];

function renderWizard(
  overrides?: Partial<React.ComponentProps<typeof MatchSetupWizard>>
) {
  const onSaved = vi.fn();
  const onOpenChange = vi.fn();
  render(
    <MatchSetupWizard
      open
      onOpenChange={onOpenChange}
      occupationFields={occupationFields}
      regions={regions}
      employmentTypes={employmentTypes}
      persistedOccupationGroups={[]}
      persistedRegions={[]}
      persistedEmploymentTypes={[]}
      onSaved={onSaved}
      importCvHref="/cv/importera"
      {...overrides}
    />
  );
  return { onSaved, onOpenChange };
}

beforeEach(() => {
  updateMock.mockReset();
  deriveMock.mockReset();
  cvSuggestMock.mockReset();
  updateMock.mockResolvedValue({ success: true });
  deriveMock.mockResolvedValue({ success: true, candidates: [] });
  // Default: inget CV (auto-suggest på steg 1 ger en lugn tom-state).
  cvSuggestMock.mockResolvedValue({ kind: "noCv" } satisfies CvSuggestResult);
});

describe("MatchSetupWizard — steg-navigering", () => {
  it("startar på steg 1 av 4 med mono-stegräknare och Yrken-rubrik", () => {
    renderWizard();
    expect(screen.getByText("Steg 1 av 4")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Yrken" })).toBeInTheDocument();
  });

  it("Tillbaka är dolt på steg 1 men finns från steg 2", async () => {
    const user = userEvent.setup();
    renderWizard();
    expect(screen.queryByRole("button", { name: "Tillbaka" })).toBeNull();
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    expect(screen.getByText("Steg 2 av 4")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Tillbaka" })
    ).toBeInTheDocument();
  });

  it("Nästa går framåt och Tillbaka går bakåt genom alla fyra steg", async () => {
    const user = userEvent.setup();
    renderWizard();
    // 1 → 2 → 3 → 4
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    expect(screen.getByRole("heading", { name: "Regioner" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    expect(
      screen.getByRole("heading", { name: "Anställningsformer" })
    ).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    expect(screen.getByText("Steg 4 av 4")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Klart" })).toBeInTheDocument();
    // sista steget: primär = Spara matchning (ej Nästa)
    expect(
      screen.getByRole("button", { name: "Spara matchning" })
    ).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Nästa" })).toBeNull();
    // 4 → 3
    await user.click(screen.getByRole("button", { name: "Tillbaka" }));
    expect(
      screen.getByRole("heading", { name: "Anställningsformer" })
    ).toBeInTheDocument();
  });

  it("Hoppa över går framåt utan att röra valet (skip ≠ nästa, separat affordans)", async () => {
    const user = userEvent.setup();
    renderWizard();
    const skip = screen.getByRole("button", {
      name: "Hoppa över det här steget",
    });
    const next = screen.getByRole("button", { name: "Nästa" });
    expect(skip).not.toBe(next);
    await user.click(skip);
    expect(screen.getByRole("heading", { name: "Regioner" })).toBeInTheDocument();
  });

  it("Hoppa över finns inte på sista steget (det ÄR klart-steget)", async () => {
    const user = userEvent.setup();
    renderWizard();
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    expect(
      screen.queryByRole("button", { name: "Hoppa över det här steget" })
    ).toBeNull();
  });

  it("flyttar fokus till stegrubriken vid stegbyte (WCAG 2.4.3), aldrig body", async () => {
    const user = userEvent.setup();
    renderWizard();
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await waitFor(() => {
      const heading = screen.getByRole("heading", { name: "Regioner" });
      expect(heading).toHaveFocus();
    });
  });
});

describe("MatchSetupWizard — utbildnings-steg är OUT v1 (ADR 0077 Klas-fork)", () => {
  it("renderar aldrig ett Utbildning-steg", async () => {
    const user = userEvent.setup();
    renderWizard();
    for (let i = 0; i < 3; i++) {
      const next = screen.queryByRole("button", { name: "Nästa" });
      if (next) await user.click(next);
    }
    expect(screen.queryByText(/Utbildning/i)).toBeNull();
    expect(screen.queryByText("Steg 5 av")).toBeNull();
  });
});

describe("MatchSetupWizard — CV-prefill in i steg 1", () => {
  it("kör CV-förslaget automatiskt vid montering och visar kandidater pre-kryssade", async () => {
    cvSuggestMock.mockResolvedValue({
      kind: "candidates",
      candidates: [
        {
          occupationGroupConceptId: "grp_backend",
          occupationGroupLabel: "Backendutvecklare",
        },
      ],
    } satisfies CvSuggestResult);
    renderWizard();

    await waitFor(() => expect(cvSuggestMock).toHaveBeenCalledTimes(1));
    const group = await screen.findByRole("group", {
      name: "Föreslagna yrkesgrupper",
    });
    expect(
      within(group).getByRole("checkbox", { name: "Backendutvecklare" })
    ).toBeInTheDocument();
    // Deterministisk copy — aldrig "AI".
    expect(screen.queryByText(/AI/)).toBeNull();
  });

  it("att kryssa en CV-kandidat pinnar den som borttagbar chip i Yrken", async () => {
    const user = userEvent.setup();
    cvSuggestMock.mockResolvedValue({
      kind: "candidates",
      candidates: [
        {
          occupationGroupConceptId: "grp_frontend",
          occupationGroupLabel: "Frontendutvecklare",
        },
      ],
    } satisfies CvSuggestResult);
    renderWizard();

    const group = await screen.findByRole("group", {
      name: "Föreslagna yrkesgrupper",
    });
    await user.click(
      within(group).getByRole("checkbox", { name: "Frontendutvecklare" })
    );
    expect(
      screen.getByRole("button", { name: "Ta bort Frontendutvecklare" })
    ).toBeInTheDocument();
  });
});

describe("MatchSetupWizard — ett enda save på slutet", () => {
  it("skriver inget förrän sista steget; Spara skriver alla tre dimensionerna en gång", async () => {
    const user = userEvent.setup();
    const { onSaved, onOpenChange } = renderWizard({
      persistedEmploymentTypes: ["et_fast"],
    });

    // Steg 1: lägg till ett yrke via kaskaden.
    await user.click(screen.getByRole("option", { name: /Data\/IT/ }));
    await user.click(
      screen.getByRole("checkbox", { name: "Backendutvecklare" })
    );
    expect(updateMock).not.toHaveBeenCalled();

    // Steg 2: lägg till en region.
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("checkbox", { name: "Stockholms län" }));
    expect(updateMock).not.toHaveBeenCalled();

    // Steg 3 → 4.
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("button", { name: "Nästa" }));

    // Sista steget: ett enda PUT med alla tre dimensionerna.
    await user.click(screen.getByRole("button", { name: "Spara matchning" }));

    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(1));
    expect(updateMock).toHaveBeenCalledWith({
      preferredOccupationGroups: ["grp_backend"],
      preferredRegions: ["region_sthlm"],
      preferredEmploymentTypes: ["et_fast"],
    });
    expect(onSaved).toHaveBeenCalledWith({
      occupations: ["grp_backend"],
      regions: ["region_sthlm"],
      employment: ["et_fast"],
    });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("klart-steget visar valda chips och tom-rad för en oangiven dimension (ärligt)", async () => {
    const user = userEvent.setup();
    renderWizard({ persistedRegions: ["region_sthlm"] });

    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("button", { name: "Nästa" }));

    const reviewRegions = screen.getByRole("group", { name: "Regioner" });
    expect(
      within(reviewRegions).getByRole("button", {
        name: "Ta bort Stockholms län",
      })
    ).toBeInTheDocument();
    // Oangivet yrke → ärlig "inget valt"-rad, aldrig fejkat.
    expect(
      screen.getByText("Alla yrken (inget valt)")
    ).toBeInTheDocument();
  });

  it("misslyckad save visar role=alert utan att stänga", async () => {
    const user = userEvent.setup();
    updateMock.mockResolvedValue({ success: false, error: "Serverfel" });
    const { onOpenChange } = renderWizard();

    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("button", { name: "Spara matchning" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Serverfel");
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});

describe("MatchSetupWizard — a11y (Radix description-wiring)", () => {
  // Regression: explicit aria-describedby på DialogContent + explicit id på
  // DialogDescription besegrade Radix auto-wiring och gav konsolvarningen
  // "Missing `Description` or `aria-describedby={undefined}`". Radix kopplar nu själv.
  it("renderar utan Radix missing-description-varning", () => {
    const warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    renderWizard();
    const logged = [...warnSpy.mock.calls, ...errorSpy.mock.calls]
      .flat()
      .join(" ");
    expect(logged).not.toMatch(/Missing .?Description|aria-describedby/i);
    warnSpy.mockRestore();
    errorSpy.mockRestore();
  });
});
