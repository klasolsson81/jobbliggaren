import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";

// Mock båda action-modulens exports (server-actions körs aldrig i jsdom).
const { updateMock, deriveMock } = vi.hoisted(() => ({
  updateMock: vi.fn(),
  deriveMock: vi.fn(),
}));
vi.mock("@/lib/actions/match-preferences", () => ({
  updateMatchPreferencesAction: updateMock,
  deriveOccupationsAction: deriveMock,
}));

import {
  MatchPreferencesCard,
  flattenOccupationGroups,
  filterOptions,
} from "./match-preferences-card";

// ── Fixtures ────────────────────────────────────────────────
const occupationFields: ReadonlyArray<TaxonomyOccupationField> = [
  {
    conceptId: "field_data",
    label: "Data/IT",
    occupationGroups: [
      { conceptId: "grp_backend", label: "Backendutvecklare" },
      { conceptId: "grp_frontend", label: "Frontendutvecklare" },
    ],
  },
  {
    conceptId: "field_vard",
    label: "Hälso- och sjukvård",
    occupationGroups: [
      { conceptId: "grp_sjukskoterska", label: "Sjuksköterskor" },
    ],
  },
];

const regions: ReadonlyArray<TaxonomyRegion> = [
  { conceptId: "region_sthlm", label: "Stockholms län", municipalities: [] },
  { conceptId: "region_vg", label: "Västra Götalands län", municipalities: [] },
];

const employmentTypes: ReadonlyArray<TaxonomyOption> = [
  { conceptId: "et_fast", label: "Tillsvidareanställning" },
  { conceptId: "et_vikariat", label: "Vikariat" },
];

function renderCard(
  overrides?: Partial<React.ComponentProps<typeof MatchPreferencesCard>>
) {
  return render(
    <MatchPreferencesCard
      occupationFields={occupationFields}
      regions={regions}
      employmentTypes={employmentTypes}
      initialOccupationGroups={[]}
      initialRegions={[]}
      initialEmploymentTypes={[]}
      degraded={false}
      {...overrides}
    />
  );
}

// CheckItem renderar en role="checkbox" med label-texten som tillgängligt namn.
function checkbox(name: string | RegExp) {
  return screen.getByRole("checkbox", { name });
}

describe("flattenOccupationGroups", () => {
  it("plattar nästlade yrkesgrupper till en enkel conceptId/label-lista", () => {
    const flat = flattenOccupationGroups(occupationFields);
    expect(flat).toEqual([
      { conceptId: "grp_backend", label: "Backendutvecklare" },
      { conceptId: "grp_frontend", label: "Frontendutvecklare" },
      { conceptId: "grp_sjukskoterska", label: "Sjuksköterskor" },
    ]);
  });

  it("ger tom lista för tomt fält-set", () => {
    expect(flattenOccupationGroups([])).toEqual([]);
  });
});

describe("filterOptions", () => {
  const options = flattenOccupationGroups(occupationFields);

  it("substring-filtrerar case-insensitivt", () => {
    const result = filterOptions(options, "UTVECKLARE");
    expect(result.map((o) => o.conceptId)).toEqual([
      "grp_backend",
      "grp_frontend",
    ]);
  });

  it("blank query → hela listan", () => {
    expect(filterOptions(options, "   ")).toEqual(options);
  });

  it("ingen träff → tom lista", () => {
    expect(filterOptions(options, "saknas")).toEqual([]);
  });
});

describe("MatchPreferencesCard", () => {
  beforeEach(() => {
    updateMock.mockReset();
    deriveMock.mockReset();
    updateMock.mockResolvedValue({ success: true });
    deriveMock.mockResolvedValue({ success: true, candidates: [] });
  });

  it("renderar de tre sektions-rubrikerna + introtext", () => {
    renderCard();
    expect(
      screen.getByText("Yrkesgrupper", { selector: ".jp-popover__title" })
    ).toBeInTheDocument();
    expect(
      screen.getByText("Regioner", { selector: ".jp-popover__title" })
    ).toBeInTheDocument();
    expect(
      screen.getByText("Anställningsformer", { selector: ".jp-popover__title" })
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Ange vilka yrken, regioner och anställningsformer/)
    ).toBeInTheDocument();
  });

  it("förkryssar de initiala valen i rätt kryssrutor", () => {
    renderCard({
      initialOccupationGroups: ["grp_backend"],
      initialRegions: ["region_vg"],
      initialEmploymentTypes: ["et_fast"],
    });
    expect(checkbox("Backendutvecklare")).toHaveAttribute(
      "aria-checked",
      "true"
    );
    expect(checkbox("Frontendutvecklare")).toHaveAttribute(
      "aria-checked",
      "false"
    );
    expect(checkbox("Västra Götalands län")).toHaveAttribute(
      "aria-checked",
      "true"
    );
    expect(checkbox("Tillsvidareanställning")).toHaveAttribute(
      "aria-checked",
      "true"
    );
  });

  it("toggling av en kryssruta uppdaterar valet", async () => {
    const user = userEvent.setup();
    renderCard();
    const box = checkbox("Backendutvecklare");
    expect(box).toHaveAttribute("aria-checked", "false");

    await user.click(box);
    expect(checkbox("Backendutvecklare")).toHaveAttribute(
      "aria-checked",
      "true"
    );

    await user.click(checkbox("Backendutvecklare"));
    expect(checkbox("Backendutvecklare")).toHaveAttribute(
      "aria-checked",
      "false"
    );
  });

  it("Rensa nollställer en dimension", async () => {
    const user = userEvent.setup();
    renderCard({ initialRegions: ["region_sthlm", "region_vg"] });

    // Rensa-länken bor i Regioner-sektionens sectionhead.
    const regionGroup = screen.getByRole("group", { name: "Regioner" });
    const rensa = within(regionGroup).getByRole("button", { name: "Rensa" });
    await user.click(rensa);

    expect(checkbox("Stockholms län")).toHaveAttribute("aria-checked", "false");
    expect(checkbox("Västra Götalands län")).toHaveAttribute(
      "aria-checked",
      "false"
    );
  });

  it("filter-inputen smalnar av yrkeslistan", async () => {
    const user = userEvent.setup();
    renderCard();

    expect(checkbox("Sjuksköterskor")).toBeInTheDocument();

    const filter = screen.getByLabelText("Filtrera yrkesgrupper");
    await user.type(filter, "utvecklare");

    expect(screen.queryByRole("checkbox", { name: "Sjuksköterskor" })).toBeNull();
    expect(checkbox("Backendutvecklare")).toBeInTheDocument();
    expect(checkbox("Frontendutvecklare")).toBeInTheDocument();
  });

  it("Spara anropar updateMatchPreferencesAction med hela aktuella valet", async () => {
    const user = userEvent.setup();
    renderCard({
      initialOccupationGroups: ["grp_backend"],
      initialRegions: ["region_sthlm"],
      initialEmploymentTypes: ["et_fast"],
    });

    await user.click(
      screen.getByRole("button", { name: "Spara matchningsönskemål" })
    );

    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(1));
    expect(updateMock).toHaveBeenCalledWith({
      preferredOccupationGroups: ["grp_backend"],
      preferredRegions: ["region_sthlm"],
      preferredEmploymentTypes: ["et_fast"],
    });
  });

  it("Spara med allt tomt är tillåtet (action anropas ändå)", async () => {
    const user = userEvent.setup();
    renderCard();

    await user.click(
      screen.getByRole("button", { name: "Spara matchningsönskemål" })
    );

    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(1));
    expect(updateMock).toHaveBeenCalledWith({
      preferredOccupationGroups: [],
      preferredRegions: [],
      preferredEmploymentTypes: [],
    });
  });

  it("Föreslå anropar deriveOccupationsAction och renderar returnerade kandidater", async () => {
    const user = userEvent.setup();
    deriveMock.mockResolvedValue({
      success: true,
      candidates: [
        {
          occupationGroupConceptId: "grp_frontend",
          occupationGroupLabel: "Frontendutvecklare",
        },
      ],
    });
    renderCard();

    await user.type(
      screen.getByLabelText("Föreslå utifrån en yrkestitel"),
      "frontend"
    );
    await user.click(screen.getByRole("button", { name: "Föreslå" }));

    await waitFor(() => expect(deriveMock).toHaveBeenCalledWith("frontend"));
    // Kandidat-blocket renderar sin egen role="group".
    const candidateGroup = await screen.findByRole("group", {
      name: "Föreslagna yrkesgrupper",
    });
    expect(
      within(candidateGroup).getByRole("checkbox", {
        name: "Frontendutvecklare",
      })
    ).toBeInTheDocument();
  });

  it("att välja en kandidat togglar dess yrkesgrupp", async () => {
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
    renderCard();

    await user.type(
      screen.getByLabelText("Föreslå utifrån en yrkestitel"),
      "backend"
    );
    await user.click(screen.getByRole("button", { name: "Föreslå" }));

    const candidateGroup = await screen.findByRole("group", {
      name: "Föreslagna yrkesgrupper",
    });
    const candidateBox = within(candidateGroup).getByRole("checkbox", {
      name: "Backendutvecklare",
    });
    expect(candidateBox).toHaveAttribute("aria-checked", "false");

    await user.click(candidateBox);

    // Toggla kandidaten → spara → backend får yrkesgruppen.
    await user.click(
      screen.getByRole("button", { name: "Spara matchningsönskemål" })
    );
    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(1));
    expect(updateMock.mock.calls[0]![0]).toEqual({
      preferredOccupationGroups: ["grp_backend"],
      preferredRegions: [],
      preferredEmploymentTypes: [],
    });
  });

  it("noll kandidater → lugn inga-förslag-text", async () => {
    const user = userEvent.setup();
    deriveMock.mockResolvedValue({ success: true, candidates: [] });
    renderCard();

    await user.type(
      screen.getByLabelText("Föreslå utifrån en yrkestitel"),
      "obefintligt"
    );
    await user.click(screen.getByRole("button", { name: "Föreslå" }));

    expect(
      await screen.findByText(/Inga förslag för den titeln/)
    ).toBeInTheDocument();
  });

  it("degraded → fallback-text, inga väljare", () => {
    renderCard({ degraded: true });
    expect(
      screen.getByText(/Dina matchningsval kunde inte läsas in just nu/)
    ).toBeInTheDocument();
    expect(screen.queryByRole("checkbox")).toBeNull();
    expect(
      screen.queryByRole("button", { name: "Spara matchningsönskemål" })
    ).toBeNull();
  });
});
