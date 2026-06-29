import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import type { CvSuggestResult } from "@/lib/actions/match-preferences";

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
  {
    conceptId: "region_sthlm",
    label: "Stockholms län",
    municipalities: [
      { conceptId: "mun_sthlm", label: "Stockholm" },
      { conceptId: "mun_solna", label: "Solna" },
    ],
  },
];
const employmentTypes: ReadonlyArray<TaxonomyOption> = [
  // #251 (A4): "Vanlig anställning" bär det FRUSNA AF-concept-id:t (PFZr_Syz_cUq)
  // som noten i steg 4 detekterar mot — inte labeln.
  { conceptId: "PFZr_Syz_cUq", label: "Vanlig anställning" },
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
      persistedMunicipalities={[]}
      persistedEmploymentTypes={[]}
      persistedSkills={[]}
      persistedOccupationExperience={[]}
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
  skillSearchMock.mockReset();
  skillSuggestMock.mockReset();
  updateMock.mockResolvedValue({ success: true });
  deriveMock.mockResolvedValue({ success: true, candidates: [] });
  // Default: inget CV (auto-suggest på steg 1 ger en lugn tom-state).
  cvSuggestMock.mockResolvedValue({ kind: "noCv" } satisfies CvSuggestResult);
  parsedSuggestMock.mockResolvedValue({ kind: "noCv" } satisfies CvSuggestResult);
  skillSearchMock.mockResolvedValue({ success: true, options: [] });
  skillSuggestMock.mockResolvedValue({ kind: "noCv" });
});

describe("MatchSetupWizard — CV-källa på steg 1", () => {
  it("utan parsedResumeId auto-suggestar via det promotade Resume:ts latestRole-väg", async () => {
    renderWizard();
    await waitFor(() => expect(cvSuggestMock).toHaveBeenCalledTimes(1));
    expect(parsedSuggestMock).not.toHaveBeenCalled();
  });

  it("med parsedResumeId auto-suggestar ur det just uppladdade parsed_resume:t", async () => {
    parsedSuggestMock.mockResolvedValue({
      kind: "candidates",
      candidates: [
        { occupationGroupConceptId: "grp_backend", occupationGroupLabel: "Backendutvecklare" },
      ],
    } satisfies CvSuggestResult);

    renderWizard({ parsedResumeId: "parsed-xyz" });

    await waitFor(() =>
      expect(parsedSuggestMock).toHaveBeenCalledWith("parsed-xyz")
    );
    // Welcome-flödet läser staging-CV:t — aldrig latestRole-vägen.
    expect(cvSuggestMock).not.toHaveBeenCalled();
  });

  it("med proposedOccupationGroups (welcome-flödet, STEG 1) seedar draften OCH auto-suggestar INTE", async () => {
    renderWizard({ proposedOccupationGroups: ["grp_backend"] });

    // Det förhämtade förslaget visas som en (borttagbar) chip på steg 1 —
    // draften är redan seedad innan staging-artefakten promotades bort.
    expect(await screen.findByText("Backendutvecklare")).toBeInTheDocument();
    // Ingen dubbel-läsning: varken parsed- eller latestRole-vägen anropas.
    expect(cvSuggestMock).not.toHaveBeenCalled();
    expect(parsedSuggestMock).not.toHaveBeenCalled();
  });
});

describe("MatchSetupWizard — steg-navigering", () => {
  it("startar på steg 1 av 5 med mono-stegräknare och Yrken-rubrik", () => {
    renderWizard();
    expect(screen.getByText("Steg 1 av 5")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Yrken" })).toBeInTheDocument();
  });

  it("Tillbaka är dolt på steg 1 men finns från steg 2", async () => {
    const user = userEvent.setup();
    renderWizard();
    expect(screen.queryByRole("button", { name: "Tillbaka" })).toBeNull();
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    expect(screen.getByText("Steg 2 av 5")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Tillbaka" })
    ).toBeInTheDocument();
  });

  it("Nästa går framåt och Tillbaka går bakåt genom alla fem steg", async () => {
    const user = userEvent.setup();
    renderWizard();
    // 1 → 2 (Kompetenser) → 3 (Orter) → 4 (Anställningsformer) → 5 (Sammanfattning)
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    expect(
      screen.getByRole("heading", { name: "Kompetenser" })
    ).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    expect(screen.getByRole("heading", { name: "Orter" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    expect(
      screen.getByRole("heading", { name: "Anställningsformer" })
    ).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    expect(screen.getByText("Steg 5 av 5")).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: "Sammanfattning" })
    ).toBeInTheDocument();
    // sista steget: primär = Spara matchning (ej Nästa)
    expect(
      screen.getByRole("button", { name: "Spara matchning" })
    ).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Nästa" })).toBeNull();
    // 5 → 4
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
    expect(
      screen.getByRole("heading", { name: "Kompetenser" })
    ).toBeInTheDocument();
  });

  it("Hoppa över finns inte på sista steget (det ÄR klart-steget)", async () => {
    const user = userEvent.setup();
    renderWizard();
    await user.click(screen.getByRole("button", { name: "Nästa" }));
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
      const heading = screen.getByRole("heading", { name: "Kompetenser" });
      expect(heading).toHaveFocus();
    });
  });
});

describe("MatchSetupWizard — 'Vanlig anställning'-not i steg 4 (#251 A4)", () => {
  /** Navigera 1 → 4 (Anställningsformer). */
  async function gotoEmployment(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    expect(
      screen.getByRole("heading", { name: "Anställningsformer" })
    ).toBeInTheDocument();
  }

  it("visar en icke-blockerande not när en smalare typ valts men inte Vanlig anställning", async () => {
    const user = userEvent.setup();
    renderWizard();
    await gotoEmployment(user);

    // Inget valt ännu → ingen not (tomt = ärligt "alla", ADR 0076). Note-specifik
    // fras (intro-copyn nämner också Vanlig anställning, så vi matchar notens egen).
    expect(
      screen.queryByText(/Lägg gärna till den för att inte missa träffar/)
    ).toBeNull();

    // Välj Tillsvidareanställning (men inte Vanlig anställning) → noten dyker upp.
    await user.click(
      screen.getByRole("checkbox", { name: "Tillsvidareanställning" })
    );
    expect(
      screen.getByText(/Lägg gärna till den för att inte missa träffar/)
    ).toBeInTheDocument();
    // Noten blockerar aldrig framåt (propose-and-approve: inget skrivs förrän PUT).
    expect(screen.getByRole("button", { name: "Nästa" })).toBeEnabled();
  });

  it("döljer noten igen när Vanlig anställning också väljs", async () => {
    const user = userEvent.setup();
    renderWizard();
    await gotoEmployment(user);

    await user.click(
      screen.getByRole("checkbox", { name: "Tillsvidareanställning" })
    );
    expect(
      screen.getByText(/Lägg gärna till den för att inte missa träffar/)
    ).toBeInTheDocument();

    await user.click(
      screen.getByRole("checkbox", { name: "Vanlig anställning" })
    );
    expect(
      screen.queryByText(/Lägg gärna till den för att inte missa träffar/)
    ).toBeNull();
  });
});

describe("MatchSetupWizard — utbildnings-steg är OUT v1 (ADR 0077 Klas-fork)", () => {
  it("renderar aldrig ett Utbildning-steg", async () => {
    const user = userEvent.setup();
    renderWizard();
    for (let i = 0; i < 4; i++) {
      const next = screen.queryByRole("button", { name: "Nästa" });
      if (next) await user.click(next);
    }
    expect(screen.queryByText(/Utbildning/i)).toBeNull();
    // STEG 3 / ADR 0079: wizarden har nu 5 steg (yrken/kompetenser/orter/
    // anställning/klart) — aldrig ett sjätte.
    expect(screen.queryByText("Steg 6 av")).toBeNull();
  });
});

describe("MatchSetupWizard — kompetens-steget (STEG 3 / ADR 0079)", () => {
  it("steg 2 visar kompetens-sektionen UTAN profil-nivå erfarenhet-fält (exp-per-occ PR-4)", async () => {
    const user = userEvent.setup();
    renderWizard();
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    expect(
      screen.getByRole("heading", { name: "Kompetenser" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Lägg till kompetens" })
    ).toBeInTheDocument();
    // exp-per-occ PR-4 (Klas-beslut): den profil-nivå ExperienceField är
    // BORTTAGEN ur steg 2 — erfarenhet anges nu per yrke (på steg 1).
    expect(screen.queryByLabelText("Antal års erfarenhet")).toBeNull();
  });

  it("med parsedResumeId auto-föreslår kompetenser ur det uppladdade CV:t (steg 2)", async () => {
    skillSuggestMock.mockResolvedValue({
      kind: "candidates",
      candidates: [
        { conceptId: "skill_react", label: "React", memberConceptIds: ["skill_react"] },
      ],
    });
    const user = userEvent.setup();
    renderWizard({ parsedResumeId: "parsed-xyz" });
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await waitFor(() =>
      expect(skillSuggestMock).toHaveBeenCalledWith("parsed-xyz")
    );
    // Kandidaten pre-addas som borttagbar chip (propose-and-approve).
    expect(
      await screen.findByRole("button", { name: "Ta bort React" })
    ).toBeInTheDocument();
    expect(updateMock).not.toHaveBeenCalled();
  });

  it("med proposedSkills (welcome-flödet) seedar kompetens-draften OCH auto-föreslår INTE", async () => {
    const user = userEvent.setup();
    renderWizard({
      proposedSkills: [
        { conceptId: "skill_sql", label: "SQL", memberConceptIds: ["skill_sql"] },
      ],
    });
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    // Det förhämtade förslaget visas som en borttagbar chip på steg 2.
    expect(await screen.findByText("SQL")).toBeInTheDocument();
    expect(skillSuggestMock).not.toHaveBeenCalled();
  });

  it("steg 5 visar LABEL (inte rått concept-id) för en MANUELLT tillagd kompetens (#253)", async () => {
    // #253: en manuellt sökt+tillagd kompetens fick sin label bara i
    // SkillSections interna store. Utan att wizarden fångar den speglade storen
    // renderade review-steget det råa concept-id:t. BE-söket returnerar labeln.
    skillSearchMock.mockResolvedValue({
      success: true,
      options: [
        {
          conceptId: "jBKc_5Yx_Y6T",
          label: "Maskininlärning",
          memberConceptIds: ["jBKc_5Yx_Y6T"],
        },
      ],
    });
    const user = userEvent.setup();
    renderWizard();

    // Steg 2: öppna "Lägg till kompetens", sök, addera träffen som chip.
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("button", { name: "Lägg till kompetens" }));
    await user.type(screen.getByRole("searchbox"), "maskin");
    await user.click(
      await screen.findByRole("button", { name: /Maskininlärning/ })
    );

    // Navigera till sammanfattningen (steg 5). SkillSection avmonteras men
    // wizarden har redan fångat den speglade labeln.
    await user.click(screen.getByRole("button", { name: "Nästa" })); // 2 → 3
    await user.click(screen.getByRole("button", { name: "Nästa" })); // 3 → 4
    await user.click(screen.getByRole("button", { name: "Nästa" })); // 4 → 5

    // Kompetens-gruppen i sammanfattningen visar LABELN, ALDRIG det råa id:t.
    const reviewSkills = screen.getByRole("group", { name: "Kompetenser" });
    expect(
      within(reviewSkills).getByText("Maskininlärning")
    ).toBeInTheDocument();
    expect(within(reviewSkills).queryByText("jBKc_5Yx_Y6T")).toBeNull();
  });

  it("#277: steg 5 visar EN 'C#'-chip för ett bekräftat twin-par (sökt + tillagt)", async () => {
    // Sök ger en twin-grupp (ESCO + AF "C#") → EN add-rad; bekräftelsen lägger
    // BÅDA member-id i draften; steg-5-sammanfattningen visar EN chip.
    skillSearchMock.mockResolvedValue({
      success: true,
      options: [
        {
          conceptId: "esco_csharp",
          label: "C#",
          memberConceptIds: ["esco_csharp", "af_csharp"],
        },
      ],
    });
    const user = userEvent.setup();
    renderWizard();

    await user.click(screen.getByRole("button", { name: "Nästa" })); // 1 → 2
    await user.click(screen.getByRole("button", { name: "Lägg till kompetens" }));
    await user.type(screen.getByRole("searchbox"), "c#");
    const rows = await screen.findAllByRole("button", { name: /C#/ });
    expect(rows).toHaveLength(1); // EN add-rad för twin-paret
    await user.click(rows[0]!);

    await user.click(screen.getByRole("button", { name: "Nästa" })); // 2 → 3
    await user.click(screen.getByRole("button", { name: "Nästa" })); // 3 → 4
    await user.click(screen.getByRole("button", { name: "Nästa" })); // 4 → 5

    const reviewSkills = screen.getByRole("group", { name: "Kompetenser" });
    expect(
      within(reviewSkills).getAllByRole("button", { name: "Ta bort C#" })
    ).toHaveLength(1);
  });

  it("#277: en bekräftad twin-grupp skrivs som BÅDA member-id i en FLAT preferredSkills på spara", async () => {
    skillSearchMock.mockResolvedValue({
      success: true,
      options: [
        {
          conceptId: "esco_csharp",
          label: "C#",
          memberConceptIds: ["esco_csharp", "af_csharp"],
        },
      ],
    });
    const user = userEvent.setup();
    renderWizard();

    await user.click(screen.getByRole("button", { name: "Nästa" })); // 1 → 2
    await user.click(screen.getByRole("button", { name: "Lägg till kompetens" }));
    await user.type(screen.getByRole("searchbox"), "c#");
    await user.click(await screen.findByRole("button", { name: /C#/ }));

    await user.click(screen.getByRole("button", { name: "Nästa" })); // 2 → 3
    await user.click(screen.getByRole("button", { name: "Nästa" })); // 3 → 4
    await user.click(screen.getByRole("button", { name: "Nästa" })); // 4 → 5
    await user.click(screen.getByRole("button", { name: "Spara matchning" }));

    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(1));
    // Save-payloaden är en FLAT string[] med BÅDA twin-id (grad-inert).
    expect(updateMock).toHaveBeenCalledWith(
      expect.objectContaining({
        preferredSkills: ["esco_csharp", "af_csharp"],
      })
    );
  });
});

describe("MatchSetupWizard — CV-prefill in i steg 1", () => {
  it("auto-kör CV-förslaget vid montering och PRE-ADDAR kandidaterna som borttagbara chips", async () => {
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
    // Kandidaten pre-addas till draften som en borttagbar chip (propose-and-
    // approve) — ingen separat kryss-checklista att bocka i.
    expect(
      await screen.findByRole("button", { name: "Ta bort Backendutvecklare" })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("group", { name: "Föreslagna yrkesgrupper" })
    ).toBeNull();
    // Deterministisk copy — aldrig "AI". Inget skrivs vid pre-add.
    expect(screen.queryByText(/AI/)).toBeNull();
    expect(updateMock).not.toHaveBeenCalled();
  });

  it("en pre-addad CV-chip kan tas bort i Yrken (propose-and-approve)", async () => {
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

    const remove = await screen.findByRole("button", {
      name: "Ta bort Frontendutvecklare",
    });
    await user.click(remove);
    expect(
      screen.queryByRole("button", { name: "Ta bort Frontendutvecklare" })
    ).toBeNull();
  });

  it("CV utan läsbar roll → lugn inline-rad (ingen kandidat-checklista)", async () => {
    cvSuggestMock.mockResolvedValue({ kind: "noRole" } satisfies CvSuggestResult);
    renderWizard();

    expect(
      await screen.findByText(/Vi kunde inte läsa ett yrke ur ditt CV/)
    ).toBeInTheDocument();
  });
});

describe("MatchSetupWizard — ett enda save på slutet", () => {
  it("skriver inget förrän sista steget; Spara skriver alla tre dimensionerna en gång", async () => {
    const user = userEvent.setup();
    const { onSaved, onOpenChange } = renderWizard({
      persistedEmploymentTypes: ["et_fast"],
    });

    // Steg 1: öppna "Lägg till yrken"-disclosure och välj via kaskaden.
    await user.click(screen.getByRole("button", { name: "Lägg till yrken" }));
    await user.click(screen.getByRole("button", { name: /Data\/IT/ }));
    await user.click(
      screen.getByRole("checkbox", { name: "Backendutvecklare" })
    );
    // exp-per-occ PR-4: ange ungefärliga år PÅ yrkes-chippen (steg 1), inte i en
    // global profil-siffra. Fältet bär en per-yrke aria-label.
    await user.type(
      screen.getByRole("spinbutton", {
        name: "År i yrket Backendutvecklare",
      }),
      "5"
    );
    expect(updateMock).not.toHaveBeenCalled();

    // Steg 2 (kompetens): lämna kompetenser tomma — inget profil-nivå
    // erfarenhet-fält längre (exp-per-occ PR-4).
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    expect(updateMock).not.toHaveBeenCalled();

    // Steg 3 (Spår 3 PR-D): lägg till HELA länet via ort-kaskaden. Öppna
    // disclosuren → välj länet i vänsterkolumnen → kryssa "Hela länet"-raden
    // (togglar länets concept-id i region-axeln, ETT id).
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("button", { name: "Lägg till orter" }));
    await user.click(screen.getByRole("button", { name: "Stockholms län" }));
    await user.click(
      screen.getByRole("checkbox", { name: "Hela Stockholms län" })
    );
    expect(updateMock).not.toHaveBeenCalled();

    // Steg 4 → 5.
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("button", { name: "Nästa" }));

    // Sista steget: ett enda PUT med alla dimensionerna. Region + kommun
    // submittas atomiskt (NOTE-1); "hela länet" = region-id, ingen kommun.
    // STEG 3 / ADR 0079: kompetens + erfarenhet i SAMMA PUT.
    await user.click(screen.getByRole("button", { name: "Spara matchning" }));

    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(1));
    // exp-per-occ PR-4: per-yrke-overlayn skickas, scopad till valda yrken;
    // profil-nivå `experienceYears` skickas INTE LÄNGRE från wizarden.
    expect(updateMock).toHaveBeenCalledWith({
      preferredOccupationGroups: ["grp_backend"],
      preferredRegions: ["region_sthlm"],
      preferredMunicipalities: [],
      preferredEmploymentTypes: ["et_fast"],
      preferredSkills: [],
      preferredOccupationExperience: [{ conceptId: "grp_backend", years: 5 }],
    });
    expect(updateMock.mock.calls[0]?.[0]).not.toHaveProperty("experienceYears");
    expect(onSaved).toHaveBeenCalledWith({
      occupations: ["grp_backend"],
      regions: ["region_sthlm"],
      municipalities: [],
      employment: ["et_fast"],
      skills: [],
      occupationExperience: [{ conceptId: "grp_backend", years: 5 }],
    });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("klart-steget visar valda chips och tom-rad för en oangiven dimension (ärligt)", async () => {
    const user = userEvent.setup();
    renderWizard({ persistedRegions: ["region_sthlm"] });

    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("button", { name: "Nästa" }));

    const reviewOrter = screen.getByRole("group", { name: "Orter" });
    expect(
      within(reviewOrter).getByRole("button", {
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
    await user.click(screen.getByRole("button", { name: "Nästa" }));
    await user.click(screen.getByRole("button", { name: "Spara matchning" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Serverfel");
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});

describe("MatchSetupWizard — per-yrke-erfarenhet (exp-per-occ, ADR 0079-amendment PR-4)", () => {
  it("CV-förslaget seedar approximateYears in i fältet (0 och null bevaras skilt)", async () => {
    // CV-förslaget bär per-yrke-år: grp_backend=0 (parsad delårsroll),
    // grp_frontend=null (ej angivet). Båda pre-addas som chips; året seedas in.
    cvSuggestMock.mockResolvedValue({
      kind: "candidates",
      candidates: [
        {
          occupationGroupConceptId: "grp_backend",
          occupationGroupLabel: "Backendutvecklare",
          approximateYears: 0,
        },
        {
          occupationGroupConceptId: "grp_frontend",
          occupationGroupLabel: "Frontendutvecklare",
          approximateYears: null,
        },
      ],
    } satisfies CvSuggestResult);
    renderWizard();

    // grp_backend seedas till "0" (skilt från tomt), grp_frontend till tomt (null).
    const backendYears = await screen.findByRole("spinbutton", {
      name: "År i yrket Backendutvecklare",
    });
    const frontendYears = screen.getByRole("spinbutton", {
      name: "År i yrket Frontendutvecklare",
    });
    expect(backendYears).toHaveValue(0);
    expect(frontendYears).toHaveValue(null);
  });

  it("ett användar-angivet år vinner över CV-seeden (seed skriver aldrig över)", async () => {
    cvSuggestMock.mockResolvedValue({
      kind: "candidates",
      candidates: [
        {
          occupationGroupConceptId: "grp_backend",
          occupationGroupLabel: "Backendutvecklare",
          // CV-deriverad seed scopad bort eftersom yrket redan har ett värde
          // (persisterat överlay). Seeden mergas bara för yrken UTAN nyckel.
          approximateYears: 9,
        },
      ],
    } satisfies CvSuggestResult);
    renderWizard({
      persistedOccupationGroups: ["grp_backend"],
      persistedOccupationExperience: [{ conceptId: "grp_backend", years: 3 }],
    });

    const backendYears = await screen.findByRole("spinbutton", {
      name: "År i yrket Backendutvecklare",
    });
    // Det persisterade 3 vinner — CV-seedens 9 skriver aldrig över.
    expect(backendYears).toHaveValue(3);
  });

  it("att ta bort ett yrke droppar dess år ur PUT-payloaden (subset-regeln)", async () => {
    const user = userEvent.setup();
    const { onSaved } = renderWizard({
      persistedOccupationGroups: ["grp_backend", "grp_frontend"],
      persistedOccupationExperience: [
        { conceptId: "grp_backend", years: 5 },
        { conceptId: "grp_frontend", years: 2 },
      ],
    });

    // Ta bort grp_frontend i Yrken-steget (chip-borttagning).
    await user.click(
      screen.getByRole("button", { name: "Ta bort Frontendutvecklare" })
    );

    // Gå till sista steget och spara.
    for (let i = 0; i < 4; i++) {
      await user.click(screen.getByRole("button", { name: "Nästa" }));
    }
    await user.click(screen.getByRole("button", { name: "Spara matchning" }));

    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(1));
    // Endast det kvarvarande yrkets år finns kvar i overlayn.
    expect(updateMock).toHaveBeenCalledWith(
      expect.objectContaining({
        preferredOccupationGroups: ["grp_backend"],
        preferredOccupationExperience: [{ conceptId: "grp_backend", years: 5 }],
      })
    );
    expect(onSaved).toHaveBeenCalledWith(
      expect.objectContaining({
        occupationExperience: [{ conceptId: "grp_backend", years: 5 }],
      })
    );
  });

  it("review-steget visar varje yrkes år på sin rad (ärlig 'år ej angivna' när null)", async () => {
    const user = userEvent.setup();
    renderWizard({
      persistedOccupationGroups: ["grp_backend", "grp_frontend"],
      persistedOccupationExperience: [
        { conceptId: "grp_backend", years: 6 },
        // grp_frontend har en null-rad → "år ej angivna".
        { conceptId: "grp_frontend", years: null },
      ],
    });

    for (let i = 0; i < 4; i++) {
      await user.click(screen.getByRole("button", { name: "Nästa" }));
    }

    const reviewOccupations = screen.getByRole("group", { name: "Yrken" });
    expect(within(reviewOccupations).getByText("6 år")).toBeInTheDocument();
    expect(
      within(reviewOccupations).getByText("År ej angivna")
    ).toBeInTheDocument();
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
