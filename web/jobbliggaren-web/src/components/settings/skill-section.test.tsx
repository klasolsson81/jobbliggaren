import { describe, it, expect, vi, beforeEach } from "vitest";
import { useState } from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { Option } from "./match-preferences-shared";
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

/**
 * Mini-host: holds the skill draft and mirrors how the dialog/wizard wire
 * onToggle/onReplace/onClear so the test can observe pre-add and chip removal.
 */
function HostHarness(
  props: Partial<React.ComponentProps<typeof SkillSection>> & {
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
  rest: Partial<React.ComponentProps<typeof SkillSection>>;
}) {
  const [selected, setSelected] = useState<ReadonlyArray<string>>(initial);
  return (
    <SkillSection
      selected={selected}
      onToggle={(id) =>
        setSelected((prev) =>
          prev.includes(id) ? prev.filter((v) => v !== id) : [...prev, id]
        )
      }
      onReplace={(next) => setSelected(next)}
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
  it("renderar sparade kompetenser som borttagbara chips med labels ur initialLabels", () => {
    render(
      <HostHarness
        initial={["skill_react"]}
        initialLabels={[{ conceptId: "skill_react", label: "React" }] satisfies Option[]}
      />
    );
    expect(
      screen.getByRole("button", { name: "Ta bort React" })
    ).toBeInTheDocument();
  });

  it("faller tillbaka på id:t när en sparad kompetens saknar label", () => {
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

  it("söker (debounced) och renderar träffar som add-rader", async () => {
    skillSearchMock.mockResolvedValue({
      success: true,
      options: [{ conceptId: "skill_react", label: "React" }],
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
      options: [{ conceptId: "skill_sql", label: "SQL" }],
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

describe("SkillSection — CV-förslag pre-addas som chips (welcome/just-uppladdat)", () => {
  it("auto-suggest pre-addar kandidaterna till draften (chips), ingen checklista", async () => {
    skillSuggestMock.mockResolvedValue({
      kind: "candidates",
      candidates: [{ conceptId: "skill_react", label: "React" }],
    } satisfies SkillSuggestResult);
    render(<HostHarness autoSuggestFromCv parsedResumeId="parsed-1" />);

    await waitFor(() =>
      expect(skillSuggestMock).toHaveBeenCalledWith("parsed-1")
    );
    expect(
      await screen.findByRole("button", { name: "Ta bort React" })
    ).toBeInTheDocument();
  });

  it("pre-add MERGAR med befintligt manuellt val (dedupe)", async () => {
    skillSuggestMock.mockResolvedValue({
      kind: "candidates",
      candidates: [
        { conceptId: "skill_react", label: "React" },
        { conceptId: "skill_sql", label: "SQL" },
      ],
    } satisfies SkillSuggestResult);
    render(
      <HostHarness
        autoSuggestFromCv
        parsedResumeId="parsed-1"
        initial={["skill_react"]}
        initialLabels={[{ conceptId: "skill_react", label: "React" }]}
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
        initialLabels={[{ conceptId: "skill_react", label: "React" }]}
      />
    );
    await user.click(screen.getByRole("button", { name: "Rensa" }));
    expect(
      screen.queryByRole("button", { name: "Ta bort React" })
    ).toBeNull();
  });
});
