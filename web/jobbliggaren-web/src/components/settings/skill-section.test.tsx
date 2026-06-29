import { describe, it, expect, vi, beforeEach } from "vitest";
import { useState } from "react";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { SkillGroup } from "@/lib/dto/skills";
import type {
  SkillSearchResult,
  SkillSuggestResult,
} from "@/lib/actions/match-preferences";

const { skillSearchMock, skillSuggestMock } = vi.hoisted(() => ({
  skillSearchMock: vi.fn(),
  skillSuggestMock: vi.fn(),
}));
vi.mock("@/lib/actions/match-preferences", () => ({
  searchSkillsAction: skillSearchMock,
  suggestSkillsFromParsedResumeAction: skillSuggestMock,
}));

import { SkillSection } from "./skill-section";

/** Singleton-grupp-fabrik (member = sig själv) — den vanliga icke-twin-yta. */
function singleton(conceptId: string, label: string): SkillGroup {
  return { conceptId, label, memberConceptIds: [conceptId] };
}

/**
 * Mini-host: holds the skill draft (a FLAT string[]) and mirrors how the
 * dialog/wizard wire onReplace/onClear so the test can observe the flat union
 * pre-add and group removal. `selected` stays a flat list of ALL member ids
 * (#277). An optional `onReplaceSpy` OBSERVES the full-replace calls WITHOUT
 * breaking state (the host still applies the new value) — so a test can both
 * assert the exact payload AND see the resulting chip render.
 */
function HostHarness(
  props: Partial<React.ComponentProps<typeof SkillSection>> & {
    initial?: ReadonlyArray<string>;
    onReplaceSpy?: (next: string[]) => void;
  }
) {
  const { initial = [], onReplaceSpy, ...rest } = props;
  return <Host initial={initial} onReplaceSpy={onReplaceSpy} rest={rest} />;
}

function Host({
  initial,
  onReplaceSpy,
  rest,
}: {
  initial: ReadonlyArray<string>;
  onReplaceSpy?: (next: string[]) => void;
  rest: Partial<React.ComponentProps<typeof SkillSection>>;
}) {
  const [selected, setSelected] = useState<ReadonlyArray<string>>(initial);
  return (
    <SkillSection
      selected={selected}
      onReplace={(next) => {
        onReplaceSpy?.(next);
        setSelected(next);
      }}
      onClear={() => setSelected([])}
      {...rest}
    />
  );
}

beforeEach(() => {
  skillSearchMock.mockReset();
  skillSuggestMock.mockReset();
  skillSearchMock.mockResolvedValue({
    success: true,
    options: [],
  } satisfies SkillSearchResult);
  skillSuggestMock.mockResolvedValue({ kind: "noCv" } satisfies SkillSuggestResult);
});

describe("SkillSection — rubrik (showHeading)", () => {
  it("visar 'Kompetenser'-rubriken som default (dialogen)", () => {
    render(<HostHarness headingId="h" />);
    expect(screen.getByText("Kompetenser")).toBeInTheDocument();
  });

  it("döljer rubriken när showHeading=false (wizarden — DialogTitle bär den)", () => {
    render(<HostHarness showHeading={false} />);
    expect(screen.queryByText("Kompetenser")).toBeNull();
  });
});

describe("SkillSection — pre-fyllda chips (settings-pre-fill)", () => {
  it("renderar sparade kompetenser som borttagbara chips med labels ur initialGroups", () => {
    render(
      <HostHarness
        initial={["skill_react"]}
        initialGroups={[singleton("skill_react", "React")]}
      />
    );
    expect(
      screen.getByRole("button", { name: "Ta bort React" })
    ).toBeInTheDocument();
  });

  it("faller tillbaka på id:t när en sparad kompetens saknar grupp", () => {
    render(<HostHarness initial={["skill_unknown"]} />);
    expect(
      screen.getByRole("button", { name: "Ta bort skill_unknown" })
    ).toBeInTheDocument();
  });
});

describe("SkillSection — sök-disclosure (search-as-you-type)", () => {
  it("sök-fältet är dolt tills 'Lägg till kompetens'-CTA:n klickas", async () => {
    const user = userEvent.setup();
    render(<HostHarness />);

    expect(screen.queryByLabelText("Sök kompetens")).toBeNull();
    const cta = screen.getByRole("button", { name: "Lägg till kompetens" });
    expect(cta).toHaveAttribute("aria-expanded", "false");

    await user.click(cta);
    expect(cta).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByLabelText("Sök kompetens")).toBeInTheDocument();
  });

  it("sök-fältet är ett vanligt sökfält, INTE en combobox (ingen falsk a11y-utfästelse)", async () => {
    const user = userEvent.setup();
    render(<HostHarness />);
    await user.click(screen.getByRole("button", { name: "Lägg till kompetens" }));

    const input = screen.getByLabelText("Sök kompetens");
    // Resultaten är add-knappar (role="group" + aria-pressed), inte ett
    // listbox/options-mönster, och det finns ingen aria-activedescendant /
    // piltangents-navigering → fältet får ALDRIG combobox-attributen.
    expect(input).toHaveAttribute("type", "search");
    expect(input).not.toHaveAttribute("role", "combobox");
    expect(input).not.toHaveAttribute("aria-expanded");
    expect(input).not.toHaveAttribute("aria-controls");
    // Den ärliga utfästelsen kvarstår: hjälptext via aria-describedby.
    expect(input).toHaveAttribute("aria-describedby");
  });

  it("annonserar 'Söker…' i en role=status live-region medan sökningen pågår", async () => {
    let resolveSearch: ((r: SkillSearchResult) => void) | undefined;
    skillSearchMock.mockReturnValue(
      new Promise<SkillSearchResult>((resolve) => {
        resolveSearch = resolve;
      })
    );
    const user = userEvent.setup();
    render(<HostHarness />);
    await user.click(screen.getByRole("button", { name: "Lägg till kompetens" }));
    await user.type(screen.getByLabelText("Sök kompetens"), "rea");

    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent("Söker");

    resolveSearch?.({ success: true, options: [] });
  });

  it("kort query (<2 tecken) anropar aldrig sök-actionen", async () => {
    const user = userEvent.setup();
    render(<HostHarness />);
    await user.click(screen.getByRole("button", { name: "Lägg till kompetens" }));
    await user.type(screen.getByLabelText("Sök kompetens"), "r");
    // Debounce + min-2-char-grinden → ingen rundtur.
    await new Promise((r) => setTimeout(r, 350));
    expect(skillSearchMock).not.toHaveBeenCalled();
  });

  it("söker (debounced) och renderar grupp-träffar som add-rader", async () => {
    skillSearchMock.mockResolvedValue({
      success: true,
      options: [singleton("skill_react", "React")],
    } satisfies SkillSearchResult);
    const user = userEvent.setup();
    render(<HostHarness />);
    await user.click(screen.getByRole("button", { name: "Lägg till kompetens" }));
    await user.type(screen.getByLabelText("Sök kompetens"), "rea");

    await waitFor(() => expect(skillSearchMock).toHaveBeenCalledWith("rea"));
    expect(
      await screen.findByRole("button", { name: /React/ })
    ).toBeInTheDocument();
  });

  it("klick på en sök-träff pinnar den som borttagbar chip (med dess label)", async () => {
    skillSearchMock.mockResolvedValue({
      success: true,
      options: [singleton("skill_sql", "SQL")],
    } satisfies SkillSearchResult);
    const user = userEvent.setup();
    render(<HostHarness />);
    await user.click(screen.getByRole("button", { name: "Lägg till kompetens" }));
    await user.type(screen.getByLabelText("Sök kompetens"), "sql");

    const option = await screen.findByRole("button", { name: /SQL/ });
    await user.click(option);

    expect(
      screen.getByRole("button", { name: "Ta bort SQL" })
    ).toBeInTheDocument();
  });

  it("tom träfflista visar en ärlig 'ingen träff'-rad", async () => {
    skillSearchMock.mockResolvedValue({
      success: true,
      options: [],
    } satisfies SkillSearchResult);
    const user = userEvent.setup();
    render(<HostHarness />);
    await user.click(screen.getByRole("button", { name: "Lägg till kompetens" }));
    await user.type(screen.getByLabelText("Sök kompetens"), "xyzzy");

    expect(
      await screen.findByText("Ingen kompetens matchar din sökning.")
    ).toBeInTheDocument();
  });
});

describe("SkillSection — #277 twin chips (en grupp = en chip = alla member-id)", () => {
  // The twin "C#" group: ESCO + AF ids share one exact-label surface.
  const csharp: SkillGroup = {
    conceptId: "esco_csharp",
    label: "C#",
    memberConceptIds: ["esco_csharp", "af_csharp"],
  };

  it("en twin-grupp-träff renderar EN add-rad och bekräftelsen lägger BÅDA member-id i draften", async () => {
    skillSearchMock.mockResolvedValue({
      success: true,
      options: [csharp],
    } satisfies SkillSearchResult);
    const user = userEvent.setup();
    const onReplace = vi.fn();
    render(<HostHarness onReplaceSpy={onReplace} />);
    await user.click(screen.getByRole("button", { name: "Lägg till kompetens" }));
    await user.type(screen.getByLabelText("Sök kompetens"), "c#");

    // EXAKT EN add-rad för "C#" (twin-paret kollapsar till en grupp).
    const rows = await screen.findAllByRole("button", { name: /C#/ });
    expect(rows).toHaveLength(1);

    await user.click(rows[0]!);
    // Bekräftelsen lägger HELA gruppens member-id (platt union) i draften.
    expect(onReplace).toHaveBeenCalledWith(["esco_csharp", "af_csharp"]);
  });

  it("att ta bort en twin-chip rensar ALLA dess member-id (differens)", async () => {
    const user = userEvent.setup();
    const onReplace = vi.fn();
    render(
      <HostHarness
        initial={["esco_csharp", "af_csharp"]}
        initialGroups={[csharp]}
        onReplaceSpy={onReplace}
      />
    );
    // EXAKT EN chip för det sparade twin-paret.
    expect(screen.getAllByRole("button", { name: "Ta bort C#" })).toHaveLength(1);

    await user.click(screen.getByRole("button", { name: "Ta bort C#" }));
    // Differensen droppar BÅDA member-id på en gång.
    expect(onReplace).toHaveBeenCalledWith([]);
  });

  it("ett sparat twin-par renderas som EN chip vid pre-fill (cold-load via grupperad resolve)", () => {
    render(
      <HostHarness initial={["esco_csharp", "af_csharp"]} initialGroups={[csharp]} />
    );
    expect(screen.getAllByRole("button", { name: "Ta bort C#" })).toHaveLength(1);
  });

  it("'redan tillagd' är sant bara när ALLA member-id är valda (halvt par är fortfarande addbart)", async () => {
    skillSearchMock.mockResolvedValue({
      success: true,
      options: [csharp],
    } satisfies SkillSearchResult);
    const user = userEvent.setup();
    const onReplace = vi.fn();
    // Bara ENA twin-id valt → gruppen ska INTE räknas som "redan tillagd".
    render(
      <HostHarness
        initial={["esco_csharp"]}
        initialGroups={[csharp]}
        onReplaceSpy={onReplace}
      />
    );
    await user.click(screen.getByRole("button", { name: "Lägg till kompetens" }));
    await user.type(screen.getByLabelText("Sök kompetens"), "c#");

    // Scope to the picker panel so the search-result ROW (which carries
    // aria-pressed) is matched, not the pinned chip's "Ta bort C#" remove button.
    const panel = screen.getByRole("group", { name: "Lägg till kompetens" });
    const row = await within(panel).findByRole("button", { name: /C#/ });
    expect(row).toHaveAttribute("aria-pressed", "false");

    // Klick kompletterar paret (union av member-id mot existerande val).
    await user.click(row);
    expect(onReplace).toHaveBeenCalledWith(["esco_csharp", "af_csharp"]);
  });

  it("en redan fullt vald grupp visar 'Tillagd' och är aria-pressed", async () => {
    skillSearchMock.mockResolvedValue({
      success: true,
      options: [csharp],
    } satisfies SkillSearchResult);
    const user = userEvent.setup();
    render(
      <HostHarness initial={["esco_csharp", "af_csharp"]} initialGroups={[csharp]} />
    );
    await user.click(screen.getByRole("button", { name: "Lägg till kompetens" }));
    await user.type(screen.getByLabelText("Sök kompetens"), "c#");

    // Scope to the picker panel (the search-result row carries aria-pressed).
    const panel = screen.getByRole("group", { name: "Lägg till kompetens" });
    const row = await within(panel).findByRole("button", { name: /C#/ });
    expect(row).toHaveAttribute("aria-pressed", "true");
  });
});

describe("SkillSection — CV-förslag pre-addas som chips (welcome/just-uppladdat)", () => {
  it("auto-suggest pre-addar kandidat-grupperna till draften (chips), ingen checklista", async () => {
    skillSuggestMock.mockResolvedValue({
      kind: "candidates",
      candidates: [singleton("skill_react", "React")],
    } satisfies SkillSuggestResult);
    render(<HostHarness autoSuggestFromCv parsedResumeId="parsed-1" />);

    await waitFor(() =>
      expect(skillSuggestMock).toHaveBeenCalledWith("parsed-1")
    );
    expect(
      await screen.findByRole("button", { name: "Ta bort React" })
    ).toBeInTheDocument();
  });

  it("en CV-twin-grupp pre-addar BÅDA member-id som EN chip", async () => {
    skillSuggestMock.mockResolvedValue({
      kind: "candidates",
      candidates: [
        {
          conceptId: "esco_csharp",
          label: "C#",
          memberConceptIds: ["esco_csharp", "af_csharp"],
        },
      ],
    } satisfies SkillSuggestResult);
    const onReplace = vi.fn();
    render(
      <HostHarness autoSuggestFromCv parsedResumeId="parsed-1" onReplaceSpy={onReplace} />
    );

    await waitFor(() => expect(skillSuggestMock).toHaveBeenCalledTimes(1));
    // EN chip för paret, och draften fick BÅDA member-id (platt union).
    expect(
      await screen.findByRole("button", { name: "Ta bort C#" })
    ).toBeInTheDocument();
    expect(onReplace).toHaveBeenCalledWith(["esco_csharp", "af_csharp"]);
  });

  it("pre-add MERGAR med befintligt manuellt val (dedupe)", async () => {
    skillSuggestMock.mockResolvedValue({
      kind: "candidates",
      candidates: [
        singleton("skill_react", "React"),
        singleton("skill_sql", "SQL"),
      ],
    } satisfies SkillSuggestResult);
    render(
      <HostHarness
        autoSuggestFromCv
        parsedResumeId="parsed-1"
        initial={["skill_react"]}
        initialGroups={[singleton("skill_react", "React")]}
      />
    );

    await waitFor(() => expect(skillSuggestMock).toHaveBeenCalledTimes(1));
    expect(
      await screen.findByRole("button", { name: "Ta bort SQL" })
    ).toBeInTheDocument();
    expect(
      screen.getAllByRole("button", { name: "Ta bort React" })
    ).toHaveLength(1);
  });

  it("noSkills (CV utan läsbara kompetenser) → lugn inline-rad, ingen checklista", async () => {
    skillSuggestMock.mockResolvedValue({
      kind: "noRole",
    } satisfies SkillSuggestResult);
    render(<HostHarness autoSuggestFromCv parsedResumeId="parsed-1" />);
    expect(
      await screen.findByText(/Vi kunde inte läsa några kompetenser ur ditt CV/)
    ).toBeInTheDocument();
  });

  it("utan parsedResumeId körs inget CV-förslag (settings-vägen)", async () => {
    render(<HostHarness autoSuggestFromCv />);
    // Ingen CV-källa → ingen läsning; sök-tillägget täcker settings-användaren.
    await new Promise((r) => setTimeout(r, 50));
    expect(skillSuggestMock).not.toHaveBeenCalled();
  });
});

describe("SkillSection — rensa", () => {
  it("Rensa-länken töms hela valet", async () => {
    const user = userEvent.setup();
    render(
      <HostHarness
        initial={["skill_react"]}
        initialGroups={[singleton("skill_react", "React")]}
      />
    );
    await user.click(screen.getByRole("button", { name: "Rensa" }));
    expect(
      screen.queryByRole("button", { name: "Ta bort React" })
    ).toBeNull();
  });
});
