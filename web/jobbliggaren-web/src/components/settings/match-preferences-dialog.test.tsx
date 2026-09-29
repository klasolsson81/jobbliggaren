import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within, waitFor } from "@testing-library/react";
import { render as rawRender } from "@testing-library/react/pure";
import { NextIntlClientProvider } from "next-intl";
import enMessages from "../../../messages/en";
import userEvent from "@testing-library/user-event";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import type { ActionResult } from "@/lib/actions/_action-result";
import type { CvSuggestResult } from "@/lib/actions/match-preferences";
import type { MatchPart, PartValue } from "./match-preferences-shared";

// The sections reference these server actions; none runs in jsdom. The dialog itself writes
// through `onSave`, which the card owns.
const { cvSuggestMock, parsedSuggestMock, skillSearchMock, skillSuggestMock } = vi.hoisted(
  () => ({
    cvSuggestMock: vi.fn(),
    parsedSuggestMock: vi.fn(),
    skillSearchMock: vi.fn(),
    skillSuggestMock: vi.fn(),
  })
);
vi.mock("@/lib/actions/match-preferences", () => ({
  suggestOccupationsFromCvAction: cvSuggestMock,
  suggestOccupationsFromParsedResumeAction: parsedSuggestMock,
  searchSkillsAction: skillSearchMock,
  suggestSkillsFromParsedResumeAction: skillSuggestMock,
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
  {
    conceptId: "kpPX_CNN_gDU",
    label: "Tillsvidareanställning (inkl. eventuell provanställning)",
  },
  { conceptId: "gro4_cWF_6D7", label: "Vikariat" },
];

const EMPTY = {
  occupations: { part: "occupations", groups: [], experience: [] },
  skills: { part: "skills", skills: [], skillGroups: [] },
  locations: { part: "locations", regions: [], municipalities: [], remote: false },
  employmentTypes: { part: "employmentTypes", types: [] },
  experience: { part: "experience", years: null },
} satisfies Record<MatchPart, PartValue>;

const FILLED = {
  occupations: { part: "occupations", groups: ["grp_backend"], experience: [] },
  skills: {
    part: "skills",
    skills: ["skill_react"],
    skillGroups: [{ conceptId: "skill_react", label: "React", memberConceptIds: ["skill_react"] }],
  },
  locations: { part: "locations", regions: ["region_sthlm"], municipalities: [], remote: false },
  employmentTypes: { part: "employmentTypes", types: ["gro4_cWF_6D7"] },
  experience: { part: "experience", years: 5 },
} satisfies Record<MatchPart, PartValue>;

const SAVED: ActionResult = { success: true };
const REFUSED_TEXT = "Ändringen kunde inte sparas. Försök igen om en stund.";
const REFUSED: ActionResult = { success: false, error: REFUSED_TEXT };

function renderDialog(value: PartValue) {
  const onSave = vi.fn<(value: PartValue) => Promise<ActionResult>>().mockResolvedValue(SAVED);
  const onOpenChange = vi.fn();
  render(
    <MatchPreferencesDialog
      open
      onOpenChange={onOpenChange}
      value={value}
      occupationFields={occupationFields}
      regions={regions}
      employmentTypes={employmentTypes}
      importCvHref="/cv/importera"
      onSave={onSave}
      onCloseAutoFocus={vi.fn()}
    />
  );
  return { onSave, onOpenChange };
}

beforeEach(() => {
  cvSuggestMock.mockReset().mockResolvedValue({ kind: "noCv" });
  parsedSuggestMock.mockReset().mockResolvedValue({ kind: "noCv" });
  skillSearchMock.mockReset().mockResolvedValue({ success: true, options: [] });
  skillSuggestMock.mockReset().mockResolvedValue({ kind: "noCv" });
});

describe("one dialog per part (#1918 Binding 3)", () => {
  it.each([
    ["occupations", "Yrken", "Spara yrken", "wide"],
    ["skills", "Kompetenser", "Spara kompetenser", "narrow"],
    ["locations", "Orter", "Spara orter", "wide"],
    ["employmentTypes", "Anställningsformer", "Spara anställningsformer", "narrow"],
    ["experience", "Antal års erfarenhet", "Spara erfarenhet", "narrow"],
  ] as const)("%s: titled by the part, saved by name, no subheading and no lede", (part, title, save, width) => {
    renderDialog(FILLED[part]);
    const dialog = screen.getByRole("dialog", { name: title });

    expect(within(dialog).getAllByRole("heading")).toHaveLength(1);
    expect(within(dialog).getByRole("heading", { name: title })).toBeInTheDocument();
    expect(dialog).not.toHaveAttribute("aria-describedby");
    expect(within(dialog).getByRole("button", { name: save })).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Avbryt" })).toBeInTheDocument();
    expect(dialog.classList.contains("jp-matchdialog--narrow")).toBe(width === "narrow");
  });

  it("renderar exakt EN stäng-knapp (radix Close)", async () => {
    const user = userEvent.setup();
    const { onOpenChange } = renderDialog(FILLED.skills);
    expect(screen.getAllByRole("button", { name: "Stäng" })).toHaveLength(1);
    await user.click(screen.getByRole("button", { name: "Stäng" }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("renderar utan Radix missing-description-varning", () => {
    const warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    renderDialog(FILLED.occupations);
    const logged = [...warnSpy.mock.calls, ...errorSpy.mock.calls].flat().join(" ");
    expect(logged).not.toMatch(/Missing .?Description|aria-describedby/i);
    warnSpy.mockRestore();
    errorSpy.mockRestore();
  });
});

describe("focus on open (#1918 Binding 5)", () => {
  it.each(["occupations", "skills", "locations", "employmentTypes"] as const)(
    "%s: the title, never Rensa",
    (part) => {
      renderDialog(FILLED[part]);
      const dialog = screen.getByRole("dialog");
      expect(within(dialog).getByRole("heading")).toHaveFocus();
      expect(within(dialog).getByRole("heading")).toHaveAttribute("tabindex", "-1");
      expect(within(dialog).getByRole("button", { name: "Rensa" })).not.toHaveFocus();
    }
  );

  it("experience: the field, named by the title and without a hint (Binding 4)", () => {
    renderDialog(FILLED.experience);
    const field = screen.getByRole("spinbutton", { name: "Antal års erfarenhet" });
    expect(field).toHaveFocus();
    expect(field).toHaveValue(5);
    expect(field).not.toHaveAttribute("aria-describedby");
  });
});

describe("an empty part opens its picker directly", () => {
  it.each([
    ["occupations", "Lägg till yrken"],
    ["skills", "Lägg till kompetens"],
    ["locations", "Lägg till orter"],
  ] as const)("%s", (part, picker) => {
    renderDialog(EMPTY[part]);
    expect(screen.getByRole("button", { name: picker })).toHaveAttribute("aria-expanded", "true");
  });

  it.each([
    ["occupations", "Lägg till yrken"],
    ["skills", "Lägg till kompetens"],
    ["locations", "Lägg till orter"],
  ] as const)("%s, when filled, keeps it closed", (part, picker) => {
    renderDialog(FILLED[part]);
    expect(screen.getByRole("button", { name: picker })).toHaveAttribute("aria-expanded", "false");
  });
});

describe("save writes the part and only the part", () => {
  it("Yrken: the chosen groups and their years, then closes", async () => {
    const user = userEvent.setup();
    const { onSave, onOpenChange } = renderDialog(EMPTY.occupations);

    await user.click(screen.getByRole("button", { name: /Data\/IT/ }));
    await user.click(screen.getByRole("checkbox", { name: "Backendutvecklare" }));
    await user.type(screen.getByRole("spinbutton", { name: "År i yrket Backendutvecklare" }), "8");
    await user.click(screen.getByRole("button", { name: "Spara yrken" }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(onSave).toHaveBeenCalledWith({
      part: "occupations",
      groups: ["grp_backend"],
      experience: [{ conceptId: "grp_backend", years: 8 }],
    });
  });

  it("Yrken: a removed occupation takes its years with it", async () => {
    const user = userEvent.setup();
    const { onSave } = renderDialog({
      part: "occupations",
      groups: ["grp_backend", "grp_frontend"],
      experience: [
        { conceptId: "grp_backend", years: 4 },
        { conceptId: "grp_frontend", years: 2 },
      ],
    });

    await user.click(screen.getByRole("button", { name: "Ta bort Backendutvecklare" }));
    await user.click(screen.getByRole("button", { name: "Spara yrken" }));

    await waitFor(() =>
      expect(onSave).toHaveBeenCalledWith({
        part: "occupations",
        groups: ["grp_frontend"],
        experience: [{ conceptId: "grp_frontend", years: 2 }],
      })
    );
  });

  it("Kompetenser: a twin chip drops both member ids, and the names ride along", async () => {
    const user = userEvent.setup();
    const csharp = {
      conceptId: "esco_csharp",
      label: "C#",
      memberConceptIds: ["esco_csharp", "af_csharp"],
    };
    const { onSave } = renderDialog({
      part: "skills",
      skills: ["esco_csharp", "af_csharp", "skill_sql"],
      skillGroups: [csharp, { conceptId: "skill_sql", label: "SQL", memberConceptIds: ["skill_sql"] }],
    });

    expect(screen.getAllByRole("button", { name: "Ta bort C#" })).toHaveLength(1);
    await user.click(screen.getByRole("button", { name: "Ta bort C#" }));
    await user.click(screen.getByRole("button", { name: "Spara kompetenser" }));

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave.mock.calls[0]![0]).toMatchObject({ part: "skills", skills: ["skill_sql"] });
  });

  it("Orter: region, municipality and distans travel together (NOTE-1)", async () => {
    const user = userEvent.setup();
    const { onSave } = renderDialog({
      part: "locations",
      regions: ["region_sthlm"],
      municipalities: ["mun_a"],
      remote: true,
    });

    await user.click(screen.getByRole("button", { name: "Ta bort mun_a" }));
    await user.click(screen.getByRole("button", { name: "Spara orter" }));

    await waitFor(() =>
      expect(onSave).toHaveBeenCalledWith({
        part: "locations",
        regions: ["region_sthlm"],
        municipalities: [],
        remote: true,
      })
    );
  });

  it("Anställningsformer: the checked types", async () => {
    const user = userEvent.setup();
    const { onSave } = renderDialog(EMPTY.employmentTypes);

    await user.click(screen.getByRole("checkbox", { name: "Vikariat" }));
    await user.click(screen.getByRole("button", { name: "Spara anställningsformer" }));

    await waitFor(() =>
      expect(onSave).toHaveBeenCalledWith({ part: "employmentTypes", types: ["gro4_cWF_6D7"] })
    );
  });

  it.each([
    ["a number", "12", 12],
    ["an emptied field, as not stated", "", null],
  ])("Antal års erfarenhet: %s", async (_row, typed, years) => {
    const user = userEvent.setup();
    const { onSave } = renderDialog(FILLED.experience);

    const field = screen.getByRole("spinbutton", { name: "Antal års erfarenhet" });
    await user.clear(field);
    if (typed !== "") await user.type(field, typed);
    await user.click(screen.getByRole("button", { name: "Spara erfarenhet" }));

    await waitFor(() => expect(onSave).toHaveBeenCalledWith({ part: "experience", years }));
  });

  it("Avbryt stänger utan att skriva", async () => {
    const user = userEvent.setup();
    const { onSave, onOpenChange } = renderDialog(FILLED.skills);
    await user.click(screen.getByRole("button", { name: "Avbryt" }));
    expect(onSave).not.toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});

describe("a refused save (#1918 Major 5)", () => {
  it("says why in the foot, keeps the dialog and its draft, and hands focus back to Spara", async () => {
    const user = userEvent.setup();
    const { onSave, onOpenChange } = renderDialog(EMPTY.employmentTypes);
    onSave.mockResolvedValue(REFUSED);

    await user.click(screen.getByRole("checkbox", { name: "Vikariat" }));
    const save = screen.getByRole("button", { name: "Spara anställningsformer" });
    await user.click(save);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(REFUSED_TEXT);
    expect(save).toHaveAttribute("aria-describedby", alert.id);
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
    expect(screen.getByRole("checkbox", { name: "Vikariat" })).toHaveAttribute(
      "aria-checked",
      "true"
    );
    await waitFor(() => expect(save).toHaveFocus());
  });

  it("shows Sparar… and disables both buttons while the write is out", async () => {
    const user = userEvent.setup();
    const { onSave, onOpenChange } = renderDialog(FILLED.experience);
    let settle: (result: ActionResult) => void = () => {};
    onSave.mockImplementation(
      () =>
        new Promise<ActionResult>((resolve) => {
          settle = resolve;
        })
    );

    await user.click(screen.getByRole("button", { name: "Spara erfarenhet" }));

    const busy = await screen.findByRole("button", { name: "Sparar…" });
    expect(busy).toBeDisabled();
    expect(screen.getByRole("button", { name: "Avbryt" })).toBeDisabled();
    settle(SAVED);
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });
});

// #1918 B1 as it reaches the dialogs (senior-cto-advisor 6.8). The section tests pin the other
// three; Anställningsformer's list is its own control, so focus lands on it.
describe("Anställningsformer: focus after a removal", () => {
  it("the last chip leaves focus on the list's first option", async () => {
    const user = userEvent.setup();
    renderDialog(FILLED.employmentTypes);

    screen.getByRole("button", { name: "Ta bort Vikariat" }).focus();
    await user.keyboard("{Delete}");

    expect(
      screen.getByRole("checkbox", {
        name: "Tillsvidareanställning (inkl. eventuell provanställning)",
      })
    ).toHaveFocus();
  });

  it("Rensa leaves focus on the list's first option", async () => {
    const user = userEvent.setup();
    renderDialog(FILLED.employmentTypes);

    await user.click(screen.getByRole("button", { name: "Rensa" }));

    expect(
      screen.getByRole("checkbox", {
        name: "Tillsvidareanställning (inkl. eventuell provanställning)",
      })
    ).toHaveFocus();
  });
});

describe("Yrken: CV-förslag", () => {
  it("inget CV → lugn tom-state med inline 'Ladda upp CV'-knapp (ingen sid-länk)", async () => {
    const user = userEvent.setup();
    cvSuggestMock.mockResolvedValue({ kind: "noCv" } satisfies CvSuggestResult);
    renderDialog(FILLED.occupations);

    await user.click(screen.getByRole("button", { name: "Föreslå utifrån mitt CV" }));

    expect(await screen.findByText("Inget CV uppladdat")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Ladda upp CV" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Importera CV" })).toBeNull();
  });

  it("CV med kandidater → PRE-ADDAS som borttagbara chips, inget skrivs", async () => {
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
    const { onSave } = renderDialog(FILLED.occupations);

    await user.click(screen.getByRole("button", { name: "Föreslå utifrån mitt CV" }));

    expect(
      await screen.findByRole("button", { name: "Ta bort Frontendutvecklare" })
    ).toBeInTheDocument();
    expect(screen.queryByText(/AI/)).toBeNull();
    expect(onSave).not.toHaveBeenCalled();
  });

  it("error → role=alert", async () => {
    const user = userEvent.setup();
    cvSuggestMock.mockResolvedValue({ kind: "error" } satisfies CvSuggestResult);
    renderDialog(FILLED.occupations);

    await user.click(screen.getByRole("button", { name: "Föreslå utifrån mitt CV" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/Kunde inte läsa ditt CV just nu/);
  });
});

describe("MatchPreferencesDialog — locale en (#1537)", () => {
  it("names the employment type from the catalogue and the part in English", () => {
    rawRender(
      <NextIntlClientProvider locale="en" messages={enMessages} timeZone="Europe/Stockholm">
        <MatchPreferencesDialog
          open
          onOpenChange={vi.fn()}
          value={FILLED.employmentTypes}
          occupationFields={occupationFields}
          regions={regions}
          employmentTypes={[employmentTypes[0]!]}
          importCvHref="/cv/importera"
          onSave={vi.fn()}
          onCloseAutoFocus={vi.fn()}
        />
      </NextIntlClientProvider>
    );

    expect(screen.getByRole("dialog", { name: "Employment types" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save employment types" })).toBeInTheDocument();
    expect(
      screen.getByText(/Permanent employment \(including any trial employment\)/)
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/Tillsvidareanställning \(inkl\. eventuell provanställning\)/)
    ).toBeNull();
  });
});
