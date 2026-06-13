import { describe, it, expect, vi, beforeEach } from "vitest";
import { createRef } from "react";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobbKlass2Panel } from "./jobb-klass2-panel";
import type { TaxonomyOption } from "@/lib/dto/taxonomy";

// "honest 8"-utdrag (Klas — råa JobTech-labels, ingen kurering).
const employmentTypeOptions: ReadonlyArray<TaxonomyOption> = [
  { conceptId: "et_vanlig", label: "Vanlig anställning" },
  { conceptId: "et_vikariat", label: "Vikariat" },
  { conceptId: "et_sommar", label: "Sommarjobb / feriejobb" },
];
// Backend sorterar Label Ordinal → Deltid före Heltid (as-is rendering).
const worktimeExtentOptions: ReadonlyArray<TaxonomyOption> = [
  { conceptId: "wt_deltid", label: "Deltid" },
  { conceptId: "wt_heltid", label: "Heltid" },
];

function setup(
  extra?: Partial<Parameters<typeof JobbKlass2Panel>[0]>,
) {
  const onEmploymentTypeChange = vi.fn();
  const onWorktimeExtentChange = vi.fn();
  const triggerRef = createRef<HTMLButtonElement>();
  render(
    <>
      <button ref={triggerRef} type="button">
        Filter
      </button>
      <JobbKlass2Panel
        open
        employmentTypeOptions={employmentTypeOptions}
        worktimeExtentOptions={worktimeExtentOptions}
        employmentType={[]}
        worktimeExtent={[]}
        onEmploymentTypeChange={onEmploymentTypeChange}
        onWorktimeExtentChange={onWorktimeExtentChange}
        onClose={vi.fn()}
        triggerRef={triggerRef}
        emptyText="Filter kunde inte laddas just nu."
        {...extra}
      />
    </>,
  );
  return { onEmploymentTypeChange, onWorktimeExtentChange };
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("JobbKlass2Panel — Omfattning (radio single-select)", () => {
  it("renderar 'Alla' först, därefter options as-is (Deltid/Heltid)", () => {
    setup();
    const group = screen.getByRole("radiogroup", { name: "Omfattning" });
    const labels = within(group)
      .getAllByRole("radio")
      .map((r) => r.textContent);
    expect(labels).toEqual(["Alla", "Deltid", "Heltid"]);
  });

  it("'Alla' är vald (aria-checked) när worktimeExtent är tom", () => {
    setup({ worktimeExtent: [] });
    expect(
      screen.getByRole("radio", { name: "Alla" }),
    ).toHaveAttribute("aria-checked", "true");
  });

  it("val av Heltid emitterar en array med ETT element", async () => {
    const user = userEvent.setup();
    const { onWorktimeExtentChange } = setup();
    await user.click(screen.getByRole("radio", { name: "Heltid" }));
    expect(onWorktimeExtentChange).toHaveBeenCalledWith(["wt_heltid"]);
  });

  it("val av 'Alla' emitterar en TOM array (inget filter)", async () => {
    const user = userEvent.setup();
    const { onWorktimeExtentChange } = setup({ worktimeExtent: ["wt_heltid"] });
    await user.click(screen.getByRole("radio", { name: "Alla" }));
    expect(onWorktimeExtentChange).toHaveBeenCalledWith([]);
  });

  it("Rensa i Omfattning-sektionen nollar valet (tom array)", async () => {
    const user = userEvent.setup();
    const { onWorktimeExtentChange } = setup({ worktimeExtent: ["wt_heltid"] });
    const head = screen
      .getByRole("radiogroup", { name: "Omfattning" })
      .parentElement!.querySelector(".jp-panel__sectionhead")!;
    await user.click(within(head as HTMLElement).getByText("Rensa"));
    expect(onWorktimeExtentChange).toHaveBeenCalledWith([]);
  });
});

describe("JobbKlass2Panel — Anställningsform (checkbox multi-select)", () => {
  it("renderar ALLA options med råa JobTech-labels (honest, inkl. 'Vanlig anställning')", () => {
    setup();
    const group = screen.getByRole("group", { name: "Anställningsform" });
    const labels = within(group)
      .getAllByRole("checkbox")
      .map((c) => c.textContent);
    expect(labels).toEqual([
      "Vanlig anställning",
      "Vikariat",
      "Sommarjobb / feriejobb",
    ]);
  });

  it("kryssa en option lägger till dess conceptId (multi)", async () => {
    const user = userEvent.setup();
    const { onEmploymentTypeChange } = setup({
      employmentType: ["et_vikariat"],
    });
    await user.click(
      screen.getByRole("checkbox", { name: "Vanlig anställning" }),
    );
    expect(onEmploymentTypeChange).toHaveBeenCalledWith([
      "et_vikariat",
      "et_vanlig",
    ]);
  });

  it("avkryssa en redan vald option tar bort den", async () => {
    const user = userEvent.setup();
    const { onEmploymentTypeChange } = setup({
      employmentType: ["et_vikariat", "et_vanlig"],
    });
    await user.click(screen.getByRole("checkbox", { name: "Vikariat" }));
    expect(onEmploymentTypeChange).toHaveBeenCalledWith(["et_vanlig"]);
  });

  it("Rensa i Anställningsform-sektionen nollar alla val", async () => {
    const user = userEvent.setup();
    const { onEmploymentTypeChange } = setup({
      employmentType: ["et_vikariat", "et_vanlig"],
    });
    const head = screen
      .getByRole("group", { name: "Anställningsform" })
      .parentElement!.querySelector(".jp-panel__sectionhead")!;
    await user.click(within(head as HTMLElement).getByText("Rensa"));
    expect(onEmploymentTypeChange).toHaveBeenCalledWith([]);
  });
});

describe("JobbKlass2Panel — facet-counts (PR-3)", () => {
  it("renderar per-option-tal på Heltid/Deltid men INTE på 'Alla'", () => {
    setup({ worktimeExtentCounts: { wt_heltid: 100, wt_deltid: 25 } });
    expect(
      screen.getByRole("radio", { name: /Heltid/ }).textContent,
    ).toContain("(100)");
    expect(
      screen.getByRole("radio", { name: /Deltid/ }).textContent,
    ).toContain("(25)");
    // "Alla" bär aldrig ett tal (summan ägs av list-svarets totalCount, SPOT).
    expect(screen.getByRole("radio", { name: "Alla" }).textContent).not.toMatch(
      /\(\d/,
    );
  });

  it("renderar per-option-tal på anställningsform-checkboxar", () => {
    setup({ employmentTypeCounts: { et_vanlig: 24, et_vikariat: 7 } });
    expect(
      screen.getByRole("checkbox", { name: /Vanlig anställning/ }).textContent,
    ).toContain("(24)");
    expect(
      screen.getByRole("checkbox", { name: /Vikariat/ }).textContent,
    ).toContain("(7)");
  });

  it("saknad nyckel i count-dicten → 0 (degraderar inte raden)", () => {
    setup({ employmentTypeCounts: { et_vanlig: 24 } });
    expect(
      screen.getByRole("checkbox", { name: /Sommarjobb/ }).textContent,
    ).toContain("(0)");
  });

  it("null counts → inga tal renderas (degraderad/pre-fetch, panelen användbar)", () => {
    setup({ employmentTypeCounts: null, worktimeExtentCounts: null });
    expect(
      screen.getByRole("checkbox", { name: "Vikariat" }).textContent,
    ).not.toMatch(/\(\d/);
    expect(
      screen.getByRole("radio", { name: "Heltid" }).textContent,
    ).not.toMatch(/\(\d/);
  });
});

describe("JobbKlass2Panel — a11y + degradering", () => {
  it("panelen exponeras som dialog med aria-label 'Filter'", () => {
    setup();
    expect(screen.getByRole("dialog", { name: "Filter" })).toBeInTheDocument();
  });

  it("inga options → civil degradering (emptyText), inga grupper", () => {
    setup({
      employmentTypeOptions: [],
      worktimeExtentOptions: [],
      emptyText: "Filter kunde inte laddas just nu.",
    });
    expect(
      screen.getByText("Filter kunde inte laddas just nu."),
    ).toBeInTheDocument();
    expect(screen.queryByRole("radiogroup")).not.toBeInTheDocument();
    expect(
      screen.queryByRole("group", { name: "Anställningsform" }),
    ).not.toBeInTheDocument();
  });
});
