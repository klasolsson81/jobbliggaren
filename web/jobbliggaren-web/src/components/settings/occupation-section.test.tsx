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
    expect(screen.getByRole("option", { name: /Data\/IT/ })).toBeInTheDocument();
  });

  it("välja ett yrke i kaskaden pinnar det som borttagbar chip", async () => {
    const user = userEvent.setup();
    render(<HostHarness />);

    await user.click(screen.getByRole("button", { name: "Lägg till yrken" }));
    await user.click(screen.getByRole("option", { name: /Data\/IT/ }));
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

  it("noCv → behåller 'Importera CV'-affordansen", async () => {
    cvSuggestMock.mockResolvedValue({ kind: "noCv" } satisfies CvSuggestResult);
    render(<HostHarness autoSuggestFromCv />);
    const link = await screen.findByRole("link", { name: "Importera CV" });
    expect(link).toHaveAttribute("href", "/cv/importera");
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
