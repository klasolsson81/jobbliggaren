import { describe, it, expect, vi, beforeEach } from "vitest";
import { createRef } from "react";
import { render, screen, within } from "@testing-library/react";
import { render as rawRender } from "@testing-library/react/pure";
import { NextIntlClientProvider } from "next-intl";
import enMessages from "../../../messages/en";
import userEvent from "@testing-library/user-event";
import { JobbKlass2Panel } from "./jobb-klass2-panel";
import type { TaxonomyOption } from "@/lib/dto/taxonomy";

// "honest 8"-utdrag med de RIKTIGA conceptId:na ur klass2-taxonomy.json. Påhittade id
// faller utanför den kodade mängden, så de hade motionerat fallback-grenen och aldrig
// översättningen — panelen matas i produktion av exakt de tio frusna id:na (#1537,
// code-reviewer Blocker 2026-08-28).
const employmentTypeOptions: ReadonlyArray<TaxonomyOption> = [
  { conceptId: "PFZr_Syz_cUq", label: "Vanlig anställning" },
  { conceptId: "gro4_cWF_6D7", label: "Vikariat" },
  { conceptId: "Jh8f_q9J_pbJ", label: "Sommarjobb / feriejobb" },
];
const worktimeExtentOptions: ReadonlyArray<TaxonomyOption> = [
  { conceptId: "947z_JGS_Uk2", label: "Deltid" },
  { conceptId: "6YE1_gAC_R2G", label: "Heltid" },
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
  it("renderar 'Alla' först, därefter options ordnade på visat namn (Deltid/Heltid)", () => {
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
    expect(onWorktimeExtentChange).toHaveBeenCalledWith(["6YE1_gAC_R2G"]);
  });

  it("val av 'Alla' emitterar en TOM array (inget filter)", async () => {
    const user = userEvent.setup();
    const { onWorktimeExtentChange } = setup({ worktimeExtent: ["6YE1_gAC_R2G"] });
    await user.click(screen.getByRole("radio", { name: "Alla" }));
    expect(onWorktimeExtentChange).toHaveBeenCalledWith([]);
  });

  it("Rensa i Omfattning-sektionen nollar valet (tom array)", async () => {
    const user = userEvent.setup();
    const { onWorktimeExtentChange } = setup({ worktimeExtent: ["6YE1_gAC_R2G"] });
    const head = screen
      .getByRole("radiogroup", { name: "Omfattning" })
      .parentElement!.querySelector(".jp-panel__sectionhead")!;
    await user.click(within(head as HTMLElement).getByText("Rensa"));
    expect(onWorktimeExtentChange).toHaveBeenCalledWith([]);
  });
});

describe("JobbKlass2Panel — Anställningsform (checkbox multi-select)", () => {
  it("renderar ALLA options, ingen utelämnad eller hopslagen (honest 8)", () => {
    setup();
    const group = screen.getByRole("group", { name: "Anställningsform" });
    const labels = within(group)
      .getAllByRole("checkbox")
      .map((c) => c.textContent);
    // Ordnat på det visade namnet (Klas 2026-08-28), inte på fixturens ordning.
    expect(labels).toEqual([
      "Sommarjobb / feriejobb",
      "Vanlig anställning",
      "Vikariat",
    ]);
  });

  it("kryssa en option lägger till dess conceptId (multi)", async () => {
    const user = userEvent.setup();
    const { onEmploymentTypeChange } = setup({
      employmentType: ["gro4_cWF_6D7"],
    });
    await user.click(
      screen.getByRole("checkbox", { name: "Vanlig anställning" }),
    );
    expect(onEmploymentTypeChange).toHaveBeenCalledWith([
      "gro4_cWF_6D7",
      "PFZr_Syz_cUq",
    ]);
  });

  it("avkryssa en redan vald option tar bort den", async () => {
    const user = userEvent.setup();
    const { onEmploymentTypeChange } = setup({
      employmentType: ["gro4_cWF_6D7", "PFZr_Syz_cUq"],
    });
    await user.click(screen.getByRole("checkbox", { name: "Vikariat" }));
    expect(onEmploymentTypeChange).toHaveBeenCalledWith(["PFZr_Syz_cUq"]);
  });

  it("Rensa i Anställningsform-sektionen nollar alla val", async () => {
    const user = userEvent.setup();
    const { onEmploymentTypeChange } = setup({
      employmentType: ["gro4_cWF_6D7", "PFZr_Syz_cUq"],
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
    setup({ worktimeExtentCounts: { "6YE1_gAC_R2G": 100, "947z_JGS_Uk2": 25 } });
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
    setup({ employmentTypeCounts: { PFZr_Syz_cUq: 24, gro4_cWF_6D7: 7 } });
    expect(
      screen.getByRole("checkbox", { name: /Vanlig anställning/ }).textContent,
    ).toContain("(24)");
    expect(
      screen.getByRole("checkbox", { name: /Vikariat/ }).textContent,
    ).toContain("(7)");
  });

  it("saknad nyckel i count-dicten → 0 (degraderar inte raden)", () => {
    setup({ employmentTypeCounts: { PFZr_Syz_cUq: 24 } });
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

describe("JobbKlass2Panel — locale en (#1537)", () => {
  // `render` går genom shimen som hårdkodar locale="sv", så det engelska fallet
  // renderas via `/pure` — samma väg som `match-setup-rail-modal.test.tsx`.
  function renderEnglish() {
    const triggerRef = createRef<HTMLButtonElement>();
    rawRender(
      <NextIntlClientProvider locale="en" messages={enMessages} timeZone="Europe/Stockholm">
        <button ref={triggerRef} type="button">
          Filter
        </button>
        <JobbKlass2Panel
          open
          employmentTypeOptions={employmentTypeOptions}
          worktimeExtentOptions={worktimeExtentOptions}
          employmentType={[]}
          worktimeExtent={[]}
          onEmploymentTypeChange={vi.fn()}
          onWorktimeExtentChange={vi.fn()}
          onClose={vi.fn()}
          triggerRef={triggerRef}
          emptyText="Filters could not be loaded right now."
        />
      </NextIntlClientProvider>,
    );
  }

  it("namnger anställningsformerna på engelska, ordnade på det visade namnet", () => {
    renderEnglish();
    const group = screen.getByRole("group", { name: "Employment type" });
    const labels = within(group)
      .getAllByRole("checkbox")
      .map((c) => c.textContent);

    expect(labels).toEqual([
      "Regular employment",
      "Substitute position",
      "Summer job / holiday job",
    ]);
  });

  it("vänder omfattningens ordning, eftersom engelskan sorterar på sina egna ord", () => {
    // Backend skickar Deltid före Heltid (svensk ordinal). Under `en` är det
    // Full-time före Part-time — den halvan fäller en regression till backend-ordning.
    renderEnglish();
    const group = screen.getByRole("radiogroup", { name: "Scope" });
    const labels = within(group)
      .getAllByRole("radio")
      .map((r) => r.textContent);

    expect(labels).toEqual(["All", "Full-time", "Part-time"]);
  });
});
