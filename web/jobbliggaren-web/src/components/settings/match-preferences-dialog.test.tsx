import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import type { CvSuggestResult } from "@/lib/actions/match-preferences";

const { updateMock, deriveMock, cvSuggestMock, parsedSuggestMock } =
  vi.hoisted(() => ({
    updateMock: vi.fn(),
    deriveMock: vi.fn(),
    cvSuggestMock: vi.fn(),
    parsedSuggestMock: vi.fn(),
  }));
vi.mock("@/lib/actions/match-preferences", () => ({
  updateMatchPreferencesAction: updateMock,
  deriveOccupationsAction: deriveMock,
  suggestOccupationsFromCvAction: cvSuggestMock,
  suggestOccupationsFromParsedResumeAction: parsedSuggestMock,
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
  parsedSuggestMock.mockReset();
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

    // Lägg till ett yrke via disclosure-kaskaden: öppna picker → välj
    // yrkesområde → kryssa grupp.
    await user.click(screen.getByRole("button", { name: "Lägg till yrken" }));
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
  it("inget CV → lugn tom-state med inline 'Ladda upp CV'-knapp (ingen sid-länk)", async () => {
    const user = userEvent.setup();
    cvSuggestMock.mockResolvedValue({ kind: "noCv" } satisfies CvSuggestResult);
    renderDialog();

    await user.click(
      screen.getByRole("button", { name: "Föreslå utifrån mitt CV" })
    );

    // Spår 4: laddar upp inline i dialogen i stället för att navigera bort till
    // /cv/importera-sidan. Det fulla inline-upload→förslag-flödet (med stubbad
    // CvUploadForm) testas i occupation-section.test.tsx.
    expect(await screen.findByText("Inget CV uppladdat")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Ladda upp CV" })
    ).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Importera CV" })).toBeNull();
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

  it("CV med kandidater → PRE-ADDAS som borttagbara chips i Yrken (ej en separat checklista)", async () => {
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

    // Kandidaten pre-addas till draften som en borttagbar chip (propose-and-
    // approve — draft-only, inget skrivs). Ingen separat kryss-checklista.
    const yrken = screen.getByRole("group", { name: "Yrken" });
    expect(
      await within(yrken).findByRole("button", {
        name: "Ta bort Backendutvecklare",
      })
    ).toBeInTheDocument();
    // Inte längre en kandidat-checklista.
    expect(
      screen.queryByRole("group", { name: "Föreslagna yrkesgrupper" })
    ).toBeNull();
    // Deterministisk copy — aldrig "AI". Inget skrivs förrän Spara.
    expect(screen.queryByText(/AI/)).toBeNull();
    expect(updateMock).not.toHaveBeenCalled();
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

  it("en pre-addad CV-chip kan tas bort (propose-and-approve)", async () => {
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
    const yrken = screen.getByRole("group", { name: "Yrken" });
    const remove = await within(yrken).findByRole("button", {
      name: "Ta bort Frontendutvecklare",
    });
    await user.click(remove);
    expect(
      within(yrken).queryByRole("button", { name: "Ta bort Frontendutvecklare" })
    ).toBeNull();
  });
});

describe("MatchPreferencesDialog — yrkestitel-fältet borttaget (redesign)", () => {
  it("renderar inte längre 'Föreslå utifrån en yrkestitel'-fältet", () => {
    renderDialog();
    expect(
      screen.queryByLabelText("Föreslå utifrån en yrkestitel")
    ).toBeNull();
    expect(screen.queryByRole("button", { name: "Föreslå" })).toBeNull();
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
