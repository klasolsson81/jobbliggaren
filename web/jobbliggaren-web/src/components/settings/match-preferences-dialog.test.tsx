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

import { MatchPreferencesDialog } from "./match-preferences-dialog";

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

function renderDialog(
  overrides?: Partial<React.ComponentProps<typeof MatchPreferencesDialog>>
) {
  const onSaved = vi.fn();
  const onOpenChange = vi.fn();
  render(
    <MatchPreferencesDialog
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
});

describe("MatchPreferencesDialog — shell + draft", () => {
  it("renderar titel + intro + tre facet-sektioner", () => {
    renderDialog();
    expect(
      screen.getByRole("heading", { name: "Lägg till i matchning" })
    ).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Yrken" })).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Regioner" })).toBeInTheDocument();
    expect(
      screen.getByRole("group", { name: "Anställningsformer" })
    ).toBeInTheDocument();
  });

  it("renderar exakt EN stäng-knapp (radix Close, civic-restylad) som stänger", async () => {
    const user = userEvent.setup();
    const { onOpenChange } = renderDialog();
    // Regressionsvakt: dialogen hade tidigare BÅDE en egen .jp-matchdialog__close
    // OCH shadcns inbyggda Close → två "Stäng" i DOM (dubblerad för SR). Nu är
    // den inbyggda radix-Close den enda stäng-kontrollen.
    expect(screen.getAllByRole("button", { name: "Stäng" })).toHaveLength(1);
    await user.click(screen.getByRole("button", { name: "Stäng" }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("seedar draften från den persisterade mängden vid öppning (pinnade chips)", () => {
    renderDialog({
      persistedRegions: ["region_sthlm"],
      persistedEmploymentTypes: ["et_fast"],
    });
    const regionGroup = screen.getByRole("group", { name: "Regioner" });
    expect(
      within(regionGroup).getByRole("button", { name: "Ta bort Stockholms län" })
    ).toBeInTheDocument();
  });

  it("Spara skriver den fulla mängden och anropar onSaved + stänger", async () => {
    const user = userEvent.setup();
    const { onSaved, onOpenChange } = renderDialog({
      persistedRegions: ["region_sthlm"],
    });

    // Lägg till ett yrke via kaskaden: välj yrkesområde → kryssa grupp.
    await user.click(screen.getByRole("option", { name: /Data\/IT/ }));
    await user.click(screen.getByRole("checkbox", { name: "Backendutvecklare" }));

    await user.click(screen.getByRole("button", { name: "Spara matchning" }));

    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(1));
    expect(updateMock).toHaveBeenCalledWith({
      preferredOccupationGroups: ["grp_backend"],
      preferredRegions: ["region_sthlm"],
      preferredEmploymentTypes: [],
    });
    expect(onSaved).toHaveBeenCalledWith({
      occupations: ["grp_backend"],
      regions: ["region_sthlm"],
      employment: [],
    });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("Avbryt stänger utan att skriva", async () => {
    const user = userEvent.setup();
    const { onOpenChange } = renderDialog();
    await user.click(screen.getByRole("button", { name: "Avbryt" }));
    expect(updateMock).not.toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("misslyckad save visar role=alert utan att stänga", async () => {
    const user = userEvent.setup();
    updateMock.mockResolvedValue({ success: false, error: "Serverfel" });
    const { onOpenChange } = renderDialog();
    await user.click(screen.getByRole("button", { name: "Spara matchning" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Serverfel");
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  it("pinnad chip kan tas bort i dialogen (edit) innan save", async () => {
    const user = userEvent.setup();
    renderDialog({ persistedRegions: ["region_sthlm"] });
    await user.click(
      screen.getByRole("button", { name: "Ta bort Stockholms län" })
    );
    expect(
      screen.queryByRole("button", { name: "Ta bort Stockholms län" })
    ).toBeNull();
  });
});

describe("MatchPreferencesDialog — CV-förslag (fyra states)", () => {
  it("inget CV → lugn tom-state-Alert med Importera CV-länk", async () => {
    const user = userEvent.setup();
    cvSuggestMock.mockResolvedValue({ kind: "noCv" } satisfies CvSuggestResult);
    renderDialog();

    await user.click(
      screen.getByRole("button", { name: "Föreslå utifrån mitt CV" })
    );

    expect(await screen.findByText("Inget CV uppladdat")).toBeInTheDocument();
    const link = screen.getByRole("link", { name: "Importera CV" });
    expect(link).toHaveAttribute("href", "/cv/importera");
  });

  it("CV utan läsbar roll → lugn rad", async () => {
    const user = userEvent.setup();
    cvSuggestMock.mockResolvedValue({ kind: "noRole" } satisfies CvSuggestResult);
    renderDialog();

    await user.click(
      screen.getByRole("button", { name: "Föreslå utifrån mitt CV" })
    );

    expect(
      await screen.findByText(/Vi kunde inte läsa ett yrke ur ditt CV/)
    ).toBeInTheDocument();
  });

  it("CV med kandidater → pre-kryssad förhandsgranskning, aldrig auto-applicerad copy", async () => {
    const user = userEvent.setup();
    cvSuggestMock.mockResolvedValue({
      kind: "candidates",
      candidates: [
        {
          occupationGroupConceptId: "grp_backend",
          occupationGroupLabel: "Backendutvecklare",
        },
      ],
    } satisfies CvSuggestResult);
    renderDialog();

    await user.click(
      screen.getByRole("button", { name: "Föreslå utifrån mitt CV" })
    );

    const group = await screen.findByRole("group", {
      name: "Föreslagna yrkesgrupper",
    });
    expect(
      within(group).getByRole("checkbox", { name: "Backendutvecklare" })
    ).toBeInTheDocument();
    // Deterministisk copy — aldrig "AI".
    expect(
      screen.getByText(/Föreslår yrken utifrån ditt CV/)
    ).toBeInTheDocument();
    expect(screen.queryByText(/AI/)).toBeNull();
  });

  it("error → role=alert", async () => {
    const user = userEvent.setup();
    cvSuggestMock.mockResolvedValue({ kind: "error" } satisfies CvSuggestResult);
    renderDialog();

    await user.click(
      screen.getByRole("button", { name: "Föreslå utifrån mitt CV" })
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /Kunde inte läsa ditt CV just nu/
    );
  });

  it("att kryssa en CV-kandidat pinnar den som en borttagbar chip i Yrken", async () => {
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
    renderDialog();

    await user.click(
      screen.getByRole("button", { name: "Föreslå utifrån mitt CV" })
    );
    const group = await screen.findByRole("group", {
      name: "Föreslagna yrkesgrupper",
    });
    await user.click(
      within(group).getByRole("checkbox", { name: "Frontendutvecklare" })
    );

    const yrken = screen.getByRole("group", { name: "Yrken" });
    expect(
      within(yrken).getByRole("button", { name: "Ta bort Frontendutvecklare" })
    ).toBeInTheDocument();
  });
});

describe("MatchPreferencesDialog — yrkestitel-förslag (behålls)", () => {
  it("Föreslå anropar deriveOccupationsAction och renderar kandidater", async () => {
    const user = userEvent.setup();
    deriveMock.mockResolvedValue({
      success: true,
      candidates: [
        {
          occupationGroupConceptId: "grp_backend",
          occupationGroupLabel: "Backendutvecklare",
        },
      ],
    });
    renderDialog();

    await user.type(
      screen.getByLabelText("Föreslå utifrån en yrkestitel"),
      "backend"
    );
    await user.click(screen.getByRole("button", { name: "Föreslå" }));

    await waitFor(() => expect(deriveMock).toHaveBeenCalledWith("backend"));
    const group = await screen.findByRole("group", {
      name: "Föreslagna yrken utifrån titel",
    });
    expect(
      within(group).getByRole("checkbox", { name: "Backendutvecklare" })
    ).toBeInTheDocument();
  });
});

describe("MatchPreferencesDialog — a11y (Radix description-wiring)", () => {
  // Regression: explicit aria-describedby på DialogContent + explicit id på
  // DialogDescription besegrade Radix auto-wiring och gav konsolvarningen
  // "Missing `Description` or `aria-describedby={undefined}`". Radix kopplar nu själv.
  it("renderar utan Radix missing-description-varning", () => {
    const warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    renderDialog();
    const logged = [...warnSpy.mock.calls, ...errorSpy.mock.calls]
      .flat()
      .join(" ");
    expect(logged).not.toMatch(/Missing .?Description|aria-describedby/i);
    warnSpy.mockRestore();
    errorSpy.mockRestore();
  });
});
