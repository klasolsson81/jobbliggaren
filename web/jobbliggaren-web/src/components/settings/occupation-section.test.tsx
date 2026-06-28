import { describe, it, expect, vi, beforeEach } from "vitest";
import { useState } from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { TaxonomyOccupationField } from "@/lib/dto/taxonomy";
import type { CvSuggestResult } from "@/lib/actions/match-preferences";

const { deriveMock, cvSuggestMock, parsedSuggestMock } = vi.hoisted(() => ({
  deriveMock: vi.fn(),
  cvSuggestMock: vi.fn(),
  parsedSuggestMock: vi.fn(),
}));
vi.mock("@/lib/actions/match-preferences", () => ({
  deriveOccupationsAction: deriveMock,
  suggestOccupationsFromCvAction: cvSuggestMock,
  suggestOccupationsFromParsedResumeAction: parsedSuggestMock,
}));

// Stub CvUploadForm (Spår 4 inline-upload) — the real one uses next/navigation +
// fetch which jsdom lacks. The stub exposes a button that fires onUploaded with a
// fixed parsed_resume id so the inline-upload → suggest flow is testable.
vi.mock("@/components/resumes/cv-upload-form", () => ({
  CvUploadForm: ({ onUploaded }: { onUploaded?: (id: string) => void }) => (
    <button type="button" onClick={() => onUploaded?.("parsed-uploaded")}>
      Ladda upp (stub)
    </button>
  ),
}));

import { OccupationSection } from "./occupation-section";

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

/**
 * Mini-host: håller draft-state och speglar hur dialogen/wizarden wirar
 * onToggle/onReplace/onClear så testet kan observera pre-add och chip-borttagning.
 */
function HostHarness(
  props: Partial<React.ComponentProps<typeof OccupationSection>> & {
    initial?: ReadonlyArray<string>;
  }
) {
  const { initial = [], ...rest } = props;
  return <Host initial={initial} rest={rest} />;
}

function Host({
  initial,
  rest,
}: {
  initial: ReadonlyArray<string>;
  rest: Partial<React.ComponentProps<typeof OccupationSection>>;
}) {
  const [selected, setSelected] = useState<ReadonlyArray<string>>(initial);
  return (
    <OccupationSection
      occupationFields={occupationFields}
      selected={selected}
      onToggle={(id) =>
        setSelected((prev) =>
          prev.includes(id) ? prev.filter((v) => v !== id) : [...prev, id]
        )
      }
      onReplace={(next) => setSelected(next)}
      onClear={() => setSelected([])}
      importCvHref="/cv/importera"
      {...rest}
    />
  );
}

beforeEach(() => {
  deriveMock.mockReset();
  cvSuggestMock.mockReset();
  parsedSuggestMock.mockReset();
  cvSuggestMock.mockResolvedValue({ kind: "noCv" } satisfies CvSuggestResult);
  parsedSuggestMock.mockResolvedValue({ kind: "noCv" } satisfies CvSuggestResult);
});

describe("OccupationSection — rubrik (showHeading)", () => {
  it("visar 'Yrken'-rubriken som default (dialogen)", () => {
    render(<HostHarness headingId="h" />);
    expect(screen.getByText("Yrken")).toBeInTheDocument();
  });

  it("döljer 'Yrken'-rubriken när showHeading=false (wizarden — DialogTitle bär den)", () => {
    render(<HostHarness showHeading={false} />);
    expect(screen.queryByText("Yrken")).toBeNull();
  });

  it("visar bara Rensa-länken (ingen rubrik) i wizard-läget när något är valt", () => {
    render(<HostHarness showHeading={false} initial={["grp_backend"]} />);
    expect(screen.queryByText("Yrken")).toBeNull();
    expect(screen.getByRole("button", { name: "Rensa" })).toBeInTheDocument();
  });
});

describe("OccupationSection — yrkestitel-fältet borttaget", () => {
  it("renderar inte längre 'Föreslå utifrån en yrkestitel'-fältet eller dess knapp", () => {
    render(<HostHarness />);
    expect(
      screen.queryByLabelText("Föreslå utifrån en yrkestitel")
    ).toBeNull();
    expect(screen.queryByRole("button", { name: "Föreslå" })).toBeNull();
    expect(deriveMock).not.toHaveBeenCalled();
  });
});

describe("OccupationSection — 'Lägg till yrken'-disclosure", () => {
  it("kaskaden är dold tills CTA:n klickas (inget skrivs ut på en gång)", async () => {
    const user = userEvent.setup();
    render(<HostHarness />);

    // Filterfältet/kaskaden finns inte i DOM:en förrän disclosuren öppnas.
    expect(screen.queryByLabelText("Filtrera yrkesgrupper")).toBeNull();
    const cta = screen.getByRole("button", { name: "Lägg till yrken" });
    expect(cta).toHaveAttribute("aria-expanded", "false");

    await user.click(cta);
    expect(cta).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByLabelText("Filtrera yrkesgrupper")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Data\/IT/ })).toBeInTheDocument();
  });

  it("välja ett yrke i kaskaden pinnar det som borttagbar chip", async () => {
    const user = userEvent.setup();
    render(<HostHarness />);

    await user.click(screen.getByRole("button", { name: "Lägg till yrken" }));
    await user.click(screen.getByRole("button", { name: /Data\/IT/ }));
    await user.click(
      screen.getByRole("checkbox", { name: "Backendutvecklare" })
    );

    expect(
      screen.getByRole("button", { name: "Ta bort Backendutvecklare" })
    ).toBeInTheDocument();
  });
});

describe("OccupationSection — CV-förslag pre-addas som chips", () => {
  it("auto-suggest pre-addar kandidaterna till draften (chips), ingen kandidat-checklista", async () => {
    cvSuggestMock.mockResolvedValue({
      kind: "candidates",
      candidates: [
        {
          occupationGroupConceptId: "grp_backend",
          occupationGroupLabel: "Backendutvecklare",
        },
      ],
    } satisfies CvSuggestResult);
    render(<HostHarness autoSuggestFromCv />);

    await waitFor(() => expect(cvSuggestMock).toHaveBeenCalledTimes(1));
    expect(
      await screen.findByRole("button", { name: "Ta bort Backendutvecklare" })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("group", { name: "Föreslagna yrkesgrupper" })
    ).toBeNull();
  });

  it("pre-add MERGAR med befintligt manuellt val (dedupe, ingen dubblett)", async () => {
    cvSuggestMock.mockResolvedValue({
      kind: "candidates",
      candidates: [
        {
          occupationGroupConceptId: "grp_backend",
          occupationGroupLabel: "Backendutvecklare",
        },
        {
          occupationGroupConceptId: "grp_frontend",
          occupationGroupLabel: "Frontendutvecklare",
        },
      ],
    } satisfies CvSuggestResult);
    render(<HostHarness autoSuggestFromCv initial={["grp_backend"]} />);

    await waitFor(() => expect(cvSuggestMock).toHaveBeenCalledTimes(1));
    // Bägge syns; grp_backend förblir EN chip (mängd-dedupe).
    expect(
      await screen.findByRole("button", { name: "Ta bort Frontendutvecklare" })
    ).toBeInTheDocument();
    expect(
      screen.getAllByRole("button", { name: "Ta bort Backendutvecklare" })
    ).toHaveLength(1);
  });

  it("noRole → lugn inline-rad, ingen kandidat-checklista", async () => {
    cvSuggestMock.mockResolvedValue({ kind: "noRole" } satisfies CvSuggestResult);
    render(<HostHarness autoSuggestFromCv />);
    expect(
      await screen.findByText(/Vi kunde inte läsa ett yrke ur ditt CV/)
    ).toBeInTheDocument();
  });

  it("noCv → 'Ladda upp CV' öppnar inline-uppladdning, ingen sid-navigering", async () => {
    const user = userEvent.setup();
    cvSuggestMock.mockResolvedValue({ kind: "noCv" } satisfies CvSuggestResult);
    render(<HostHarness autoSuggestFromCv />);

    // Ingen sid-länk till /cv/importera längre — en knapp som öppnar inline-upload.
    expect(screen.queryByRole("link", { name: "Importera CV" })).toBeNull();
    const uploadBtn = await screen.findByRole("button", { name: "Ladda upp CV" });
    await user.click(uploadBtn);

    // Inline-upload-ytan visas; importsidan finns kvar som sekundär utväg.
    expect(
      screen.getByRole("group", { name: "Ladda upp CV" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "Öppna importsidan i stället" })
    ).toHaveAttribute("href", "/cv/importera");
  });

  it("inline-upload → kör CV-förslaget mot det uppladdade CV:t och pre-addar chips", async () => {
    const user = userEvent.setup();
    // Första suggest (autoSuggest, ingen CV) → noCv; efter upload → kandidater.
    cvSuggestMock.mockResolvedValue({ kind: "noCv" } satisfies CvSuggestResult);
    parsedSuggestMock.mockResolvedValue({
      kind: "candidates",
      candidates: [
        {
          occupationGroupConceptId: "grp_backend",
          occupationGroupLabel: "Backendutvecklare",
        },
      ],
    } satisfies CvSuggestResult);
    render(<HostHarness autoSuggestFromCv />);

    await user.click(await screen.findByRole("button", { name: "Ladda upp CV" }));
    // Stubbens knapp fyrar onUploaded("parsed-uploaded").
    await user.click(screen.getByRole("button", { name: "Ladda upp (stub)" }));

    // Förslaget körs mot det just uppladdade parsed_resume:t (inte latestRole-vägen).
    await waitFor(() =>
      expect(parsedSuggestMock).toHaveBeenCalledWith("parsed-uploaded")
    );
    expect(
      await screen.findByRole("button", { name: "Ta bort Backendutvecklare" })
    ).toBeInTheDocument();
  });

  it("dialog-läget (autoSuggestFromCv=false) har en knapp som triggar CV-förslaget", async () => {
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
    render(<HostHarness />);

    // Ingen auto-körning utan autoSuggestFromCv.
    expect(cvSuggestMock).not.toHaveBeenCalled();
    await user.click(
      screen.getByRole("button", { name: "Föreslå utifrån mitt CV" })
    );
    expect(
      await screen.findByRole("button", { name: "Ta bort Backendutvecklare" })
    ).toBeInTheDocument();
  });

  it("welcome-flödet (parsedResumeId) läser parsed-vägen, aldrig latestRole-vägen", async () => {
    parsedSuggestMock.mockResolvedValue({ kind: "noRole" } satisfies CvSuggestResult);
    render(<HostHarness autoSuggestFromCv parsedResumeId="parsed-1" />);
    await waitFor(() =>
      expect(parsedSuggestMock).toHaveBeenCalledWith("parsed-1")
    );
    expect(cvSuggestMock).not.toHaveBeenCalled();
  });
});

/**
 * exp-per-occ (ADR 0079-amendment PR-4): en host som även håller
 * erfarenhets-overlayn (precis som wizarden/dialogen), så testet kan observera
 * att fältet renderas, redigerar mapen, och att CV-seeden (0/null) bevaras.
 */
function YearsHost({
  initial = [],
  initialExperience = {},
  rest = {},
}: {
  initial?: ReadonlyArray<string>;
  initialExperience?: Readonly<Record<string, number | null>>;
  rest?: Partial<React.ComponentProps<typeof OccupationSection>>;
}) {
  const [selected, setSelected] = useState<ReadonlyArray<string>>(initial);
  const [experience, setExperience] =
    useState<Readonly<Record<string, number | null>>>(initialExperience);
  return (
    <OccupationSection
      occupationFields={occupationFields}
      selected={selected}
      onToggle={(id) =>
        setSelected((prev) =>
          prev.includes(id) ? prev.filter((v) => v !== id) : [...prev, id]
        )
      }
      onReplace={(next) => setSelected(next)}
      onClear={() => setSelected([])}
      importCvHref="/cv/importera"
      experienceByConceptId={experience}
      onExperienceChange={(id, years) =>
        setExperience((prev) => ({ ...prev, [id]: years }))
      }
      onSeedExperience={(seed) =>
        setExperience((prev) => {
          const next = { ...prev };
          for (const [id, years] of Object.entries(seed)) {
            if (!(id in next)) next[id] = years;
          }
          return next;
        })
      }
      {...rest}
    />
  );
}

describe("OccupationSection — per-yrke-erfarenhet (exp-per-occ PR-4)", () => {
  it("renderar ett 'ungefärliga år'-fält per pinnat yrke (per-yrke aria-label)", () => {
    render(<YearsHost initial={["grp_backend"]} />);
    expect(
      screen.getByRole("spinbutton", {
        name: "År i yrket Backendutvecklare",
      })
    ).toBeInTheDocument();
  });

  it("inget år-fält när onExperienceChange inte ges (kortets läs-läge)", () => {
    render(<HostHarness initial={["grp_backend"]} />);
    expect(
      screen.queryByRole("spinbutton", {
        name: "År i yrket Backendutvecklare",
      })
    ).toBeNull();
  });

  it("redigering av fältet uppdaterar overlay-mapen", async () => {
    const user = userEvent.setup();
    render(<YearsHost initial={["grp_backend"]} />);
    const field = screen.getByRole("spinbutton", {
      name: "År i yrket Backendutvecklare",
    });
    await user.type(field, "7");
    expect(field).toHaveValue(7);
  });

  it("seedar CV-härledda år (0 och null bevaras skilt)", async () => {
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
    render(<YearsHost rest={{ autoSuggestFromCv: true }} />);

    const backend = await screen.findByRole("spinbutton", {
      name: "År i yrket Backendutvecklare",
    });
    const frontend = screen.getByRole("spinbutton", {
      name: "År i yrket Frontendutvecklare",
    });
    // 0 seedas som "0" (skilt), null som tomt.
    expect(backend).toHaveValue(0);
    expect(frontend).toHaveValue(null);
  });

  it("CV-seeden skriver aldrig över ett befintligt användar-värde", async () => {
    cvSuggestMock.mockResolvedValue({
      kind: "candidates",
      candidates: [
        {
          occupationGroupConceptId: "grp_backend",
          occupationGroupLabel: "Backendutvecklare",
          approximateYears: 9,
        },
      ],
    } satisfies CvSuggestResult);
    render(
      <YearsHost
        initial={["grp_backend"]}
        initialExperience={{ grp_backend: 3 }}
        rest={{ autoSuggestFromCv: true }}
      />
    );

    await waitFor(() => expect(cvSuggestMock).toHaveBeenCalledTimes(1));
    const backend = screen.getByRole("spinbutton", {
      name: "År i yrket Backendutvecklare",
    });
    // Befintliga 3 bevaras — seedens 9 skriver inte över.
    expect(backend).toHaveValue(3);
  });

  it("att ta bort en yrkes-chip tar bort dess år-fält (lokalitet)", async () => {
    const user = userEvent.setup();
    render(<YearsHost initial={["grp_backend"]} />);
    await user.click(
      screen.getByRole("button", { name: "Ta bort Backendutvecklare" })
    );
    expect(
      screen.queryByRole("spinbutton", {
        name: "År i yrket Backendutvecklare",
      })
    ).toBeNull();
  });
});

describe("OccupationSection — 'Välj alla yrkesgrupper'", () => {
  async function openField(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByRole("button", { name: "Lägg till yrken" }));
    await user.click(screen.getByRole("button", { name: /Data\/IT/ }));
  }

  it("markerar alla grupper i det aktiva yrkesområdet i ett klick", async () => {
    const user = userEvent.setup();
    render(<HostHarness />);
    await openField(user);

    await user.click(
      screen.getByRole("checkbox", { name: "Välj alla yrkesgrupper" })
    );

    expect(
      screen.getByRole("button", { name: "Ta bort Backendutvecklare" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Ta bort Frontendutvecklare" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("checkbox", { name: "Välj alla yrkesgrupper" })
    ).toHaveAttribute("aria-checked", "true");
  });

  it("avmarkerar alla när allt redan är valt (toggle)", async () => {
    const user = userEvent.setup();
    render(<HostHarness initial={["grp_backend", "grp_frontend"]} />);
    await openField(user);

    const selectAll = screen.getByRole("checkbox", {
      name: "Välj alla yrkesgrupper",
    });
    expect(selectAll).toHaveAttribute("aria-checked", "true");
    await user.click(selectAll);

    expect(
      screen.queryByRole("button", { name: "Ta bort Backendutvecklare" })
    ).toBeNull();
    expect(
      screen.queryByRole("button", { name: "Ta bort Frontendutvecklare" })
    ).toBeNull();
  });

  it("annonserar tri-state 'mixed' vid partiellt val", async () => {
    const user = userEvent.setup();
    render(<HostHarness initial={["grp_backend"]} />);
    await openField(user);

    expect(
      screen.getByRole("checkbox", { name: "Välj alla yrkesgrupper" })
    ).toHaveAttribute("aria-checked", "mixed");
  });

  it("bevarar val i andra yrkesområden (merge, inte ersätt)", async () => {
    const twoFields: ReadonlyArray<TaxonomyOccupationField> = [
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
        label: "Hälsa och sjukvård",
        occupationGroups: [
          { conceptId: "grp_ssk", label: "Sjuksköterskor" },
        ],
      },
    ];
    const user = userEvent.setup();
    render(
      <HostHarness occupationFields={twoFields} initial={["grp_ssk"]} />
    );

    await user.click(screen.getByRole("button", { name: "Lägg till yrken" }));
    await user.click(screen.getByRole("button", { name: /Data\/IT/ }));
    await user.click(
      screen.getByRole("checkbox", { name: "Välj alla yrkesgrupper" })
    );

    // Data/IT-gruppen valdes, men vård-valet finns kvar (merge via onReplace).
    expect(
      screen.getByRole("button", { name: "Ta bort Backendutvecklare" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Ta bort Sjuksköterskor" })
    ).toBeInTheDocument();
  });
});
