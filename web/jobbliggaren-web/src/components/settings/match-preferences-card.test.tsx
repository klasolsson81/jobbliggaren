import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";

// Mock action-modulens exports (server-actions körs aldrig i jsdom).
// suggestOccupationsFromParsedResumeAction + de två skill-actionerna behövs
// eftersom den (alltid monterade) dialogen importerar OccupationSection +
// SkillSection som refererar dem.
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
      initialMunicipalities={[]}
      initialEmploymentTypes={[]}
      initialSkills={[]}
      initialSkillLabels={[]}
      initialExperienceYears={null}
      degraded={false}
      {...overrides}
    />
  );
}

beforeEach(() => {
  updateMock.mockReset();
  deriveMock.mockReset();
  cvSuggestMock.mockReset();
  parsedSuggestMock.mockReset();
  skillSearchMock.mockReset();
  skillSuggestMock.mockReset();
  updateMock.mockResolvedValue({ success: true });
  deriveMock.mockResolvedValue({ success: true, candidates: [] });
  cvSuggestMock.mockResolvedValue({ kind: "noCv" });
  parsedSuggestMock.mockResolvedValue({ kind: "noCv" });
  skillSearchMock.mockResolvedValue({ success: true, options: [] });
  skillSuggestMock.mockResolvedValue({ kind: "noCv" });
});

describe("flattenOccupationGroups", () => {
  it("plattar nästlade yrkesgrupper till en enkel conceptId/label-lista", () => {
    expect(flattenOccupationGroups(occupationFields)).toEqual([
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
    expect(filterOptions(options, "UTVECKLARE").map((o) => o.conceptId)).toEqual(
      ["grp_backend", "grp_frontend"]
    );
  });

  it("blank query → hela listan", () => {
    expect(filterOptions(options, "   ")).toEqual(options);
  });

  it("ingen träff → tom lista", () => {
    expect(filterOptions(options, "saknas")).toEqual([]);
  });
});

describe("MatchPreferencesCard — summary + chips", () => {
  it("renderar de tre facet-grupperna med synliga mono-caps-labels via aria-labelledby", () => {
    renderCard();
    // Varje facet är en role="group" med en synlig label kopplad via labelledby.
    expect(screen.getByRole("group", { name: "Yrken" })).toBeInTheDocument();
    // Spår 3 PR-D: region-facetten är nu "Orter" (län + kommun).
    expect(screen.getByRole("group", { name: "Orter" })).toBeInTheDocument();
    expect(
      screen.getByRole("group", { name: "Anställningsformer" })
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Ange vilka yrken, orter och anställningsformer/)
    ).toBeInTheDocument();
  });

  it("tom facet visar en ärlig rad i stället för chips", () => {
    renderCard();
    expect(screen.getByText("Alla yrken (inget valt)")).toBeInTheDocument();
    expect(
      screen.getByText("Hela landet (ingen ort vald)")
    ).toBeInTheDocument();
    expect(
      screen.getByText("Alla anställningsformer (inget valt)")
    ).toBeInTheDocument();
  });

  it("valda värden renderas som borttagbara chips med svenska namn", () => {
    renderCard({
      initialOccupationGroups: ["grp_backend"],
      initialRegions: ["region_sthlm", "region_vg"],
    });
    const yrken = screen.getByRole("group", { name: "Yrken" });
    expect(within(yrken).getByText("Backendutvecklare")).toBeInTheDocument();
    // ⨯-knappen bär ett a11y-namn "Ta bort {namn}".
    expect(
      within(yrken).getByRole("button", { name: "Ta bort Backendutvecklare" })
    ).toBeInTheDocument();
    const ortGroup = screen.getByRole("group", { name: "Orter" });
    expect(
      within(ortGroup).getByRole("button", { name: "Ta bort Stockholms län" })
    ).toBeInTheDocument();
  });

  it("sparade kompetenser renderar NAMN vid kall laddning ur initialSkillLabels (ADR 0047)", () => {
    renderCard({
      initialSkills: ["skill_react"],
      initialSkillLabels: [{ conceptId: "skill_react", label: "React" }],
    });
    const skills = screen.getByRole("group", { name: "Kompetenser" });
    expect(within(skills).getByText("React")).toBeInTheDocument();
    expect(
      within(skills).getByRole("button", { name: "Ta bort React" })
    ).toBeInTheDocument();
  });

  it("sparad kompetens utan label faller tillbaka på id:t (borttaget concept)", () => {
    renderCard({ initialSkills: ["skill_gone"], initialSkillLabels: [] });
    const skills = screen.getByRole("group", { name: "Kompetenser" });
    expect(
      within(skills).getByRole("button", { name: "Ta bort skill_gone" })
    ).toBeInTheDocument();
  });

  it("'Lägg till' är en dialog-affordans (aria-haspopup=dialog)", () => {
    renderCard();
    const add = screen.getByRole("button", { name: "Lägg till" });
    expect(add).toHaveAttribute("aria-haspopup", "dialog");
  });

  it("degraded → fallback-text, inga chips, ingen Lägg till", () => {
    renderCard({ degraded: true });
    expect(
      screen.getByText(/Dina matchningsval kunde inte läsas in just nu/)
    ).toBeInTheDocument();
    expect(screen.queryByRole("group", { name: "Yrken" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Lägg till" })).toBeNull();
  });
});

describe("MatchPreferencesCard — optimistisk borttagning + auto-save", () => {
  it("klick på ⨯ tar bort chippen direkt och persisterar HELA nya mängden", async () => {
    const user = userEvent.setup();
    renderCard({
      initialOccupationGroups: ["grp_backend", "grp_frontend"],
      initialRegions: ["region_sthlm"],
      initialEmploymentTypes: ["et_fast"],
    });

    await user.click(
      screen.getByRole("button", { name: "Ta bort Backendutvecklare" })
    );

    // Optimistiskt borta direkt.
    expect(
      screen.queryByRole("button", { name: "Ta bort Backendutvecklare" })
    ).toBeNull();
    // Full-replace: de andra facetterna oförändrade.
    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(1));
    expect(updateMock).toHaveBeenCalledWith({
      preferredOccupationGroups: ["grp_frontend"],
      preferredRegions: ["region_sthlm"],
      preferredMunicipalities: [],
      preferredEmploymentTypes: ["et_fast"],
      preferredSkills: [],
      experienceYears: null,
    });
  });

  it("Spår 3 PR-D: en kommun-chip kan tas bort och kommuner submittas atomiskt med regioner", async () => {
    const user = userEvent.setup();
    renderCard({
      initialRegions: ["region_sthlm"],
      initialMunicipalities: ["mun_a", "mun_b"],
    });

    // Ta bort en kommun-chip (faller tillbaka på id:t som namn — fixturens
    // regioner saknar municipalities). Region-axeln måste bäras oförändrad.
    await user.click(screen.getByRole("button", { name: "Ta bort mun_a" }));

    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(1));
    expect(updateMock).toHaveBeenCalledWith({
      preferredOccupationGroups: [],
      preferredRegions: ["region_sthlm"],
      preferredMunicipalities: ["mun_b"],
      preferredEmploymentTypes: [],
      preferredSkills: [],
      experienceYears: null,
    });
  });

  it("lyckad save visar status-raden 'Sparat HH:mm'", async () => {
    const user = userEvent.setup();
    renderCard({ initialRegions: ["region_sthlm"] });

    await user.click(
      screen.getByRole("button", { name: "Ta bort Stockholms län" })
    );

    expect(await screen.findByText(/^Sparat \d{2}:\d{2}$/)).toBeInTheDocument();
  });

  it("misslyckad save återställer chippen och visar role=alert", async () => {
    const user = userEvent.setup();
    updateMock.mockResolvedValue({ success: false, error: "nope" });
    renderCard({ initialRegions: ["region_sthlm"] });

    await user.click(
      screen.getByRole("button", { name: "Ta bort Stockholms län" })
    );

    // Chippen återinförd (revert) + alert.
    await waitFor(() =>
      expect(
        screen.getByRole("button", { name: "Ta bort Stockholms län" })
      ).toBeInTheDocument()
    );
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Ändringen kunde inte sparas. Försök igen."
    );
  });
});

describe("MatchPreferencesCard — tangentbord", () => {
  it("Delete på en fokuserad ⨯ tar bort chippen", async () => {
    const user = userEvent.setup();
    renderCard({ initialRegions: ["region_sthlm", "region_vg"] });

    const first = screen.getByRole("button", { name: "Ta bort Stockholms län" });
    first.focus();
    await user.keyboard("{Delete}");

    expect(
      screen.queryByRole("button", { name: "Ta bort Stockholms län" })
    ).toBeNull();
    await waitFor(() => expect(updateMock).toHaveBeenCalled());
  });

  it("Backspace flyttar fokus till nästa chips ⨯ efter borttagning", async () => {
    const user = userEvent.setup();
    renderCard({ initialRegions: ["region_sthlm", "region_vg"] });

    const first = screen.getByRole("button", { name: "Ta bort Stockholms län" });
    first.focus();
    await user.keyboard("{Backspace}");

    // Fokus landar på grannen (Västra Götaland), aldrig på body.
    await waitFor(() =>
      expect(
        screen.getByRole("button", { name: "Ta bort Västra Götalands län" })
      ).toHaveFocus()
    );
    expect(document.body).not.toHaveFocus();
  });

  it("borttagning av den sista chippen flyttar fokus till 'Lägg till'", async () => {
    const user = userEvent.setup();
    renderCard({ initialEmploymentTypes: ["et_fast"] });

    const only = screen.getByRole("button", {
      name: "Ta bort Tillsvidareanställning",
    });
    only.focus();
    await user.keyboard("{Delete}");

    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Lägg till" })).toHaveFocus()
    );
  });
});
