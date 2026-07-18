import { describe, it, expect, vi, beforeEach } from "vitest";
import { act } from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";

// #748 — the code-split guarantee. These assertions rest on IMPORT TIMING (a
// bundler/ESM fact), NOT the render runtime: vitest exercises next/dynamic's
// pages-loadable path, not the app-router React.lazy path — so this proves "the
// dialog module is not evaluated until first open", not the production chunk
// split (covered by the build measurement in the PR body + manual click-through).
//
// The spy flips the first time ./match-preferences-dialog is evaluated. Two
// independent, order-/race-robust checks kill the two ways to defeat the split:
//   1. `evaluatedAtFileLoad` (captured below, at module load, before any test) —
//      a STATIC import evaluates the dialog when this file imports the card, so a
//      revert to a static import makes it true → RED. Immune to beforeEach and
//      async timing.
//   2. the live spy after render + a flushed tick, before any click — dropping
//      the `{dialogRequested && …}` gate mounts the lazy dialog at render, whose
//      loader then evaluates the module → RED. The flush defeats the async race.
const dialogModuleEvaluated = vi.hoisted(() => ({ current: false }));
vi.mock("./match-preferences-dialog", async (importOriginal) => {
  dialogModuleEvaluated.current = true;
  return await importOriginal<typeof import("./match-preferences-dialog")>();
});

// The dialog's sections reference these server actions (never run in jsdom).
const {
  updateMock,
  cvSuggestMock,
  parsedSuggestMock,
  skillSearchMock,
  skillSuggestMock,
} = vi.hoisted(() => ({
  updateMock: vi.fn(),
  cvSuggestMock: vi.fn(),
  parsedSuggestMock: vi.fn(),
  skillSearchMock: vi.fn(),
  skillSuggestMock: vi.fn(),
}));
vi.mock("@/lib/actions/match-preferences", () => ({
  updateMatchPreferencesAction: updateMock,
  suggestOccupationsFromCvAction: cvSuggestMock,
  suggestOccupationsFromParsedResumeAction: parsedSuggestMock,
  searchSkillsAction: skillSearchMock,
  suggestSkillsFromParsedResumeAction: skillSuggestMock,
}));

import { MatchPreferencesCard } from "./match-preferences-card";

// Snapshot the spy immediately after imports resolve. With the code-split this is
// false (the card's dynamic import has not fired); a static import would have set
// it true during the line above. Captured once, before any test body / beforeEach.
const evaluatedAtFileLoad = dialogModuleEvaluated.current;

const occupationFields: ReadonlyArray<TaxonomyOccupationField> = [
  {
    conceptId: "field_data",
    label: "Data/IT",
    occupationGroups: [{ conceptId: "grp_backend", label: "Backendutvecklare" }],
  },
];
const regions: ReadonlyArray<TaxonomyRegion> = [
  { conceptId: "region_sthlm", label: "Stockholms län", municipalities: [] },
];
const employmentTypes: ReadonlyArray<TaxonomyOption> = [
  { conceptId: "et_fast", label: "Tillsvidareanställning" },
];

function renderCard() {
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
      initialSkillGroups={[]}
      initialExperienceYears={null}
      initialOccupationExperience={[]}
      degraded={false}
    />
  );
}

// Flush the microtask/macrotask queue so any render-triggered dynamic import
// would have resolved (and its loadable setState committed) inside act().
async function flush() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

beforeEach(() => {
  updateMock.mockReset().mockResolvedValue({ success: true });
  cvSuggestMock.mockReset().mockResolvedValue({ kind: "noCv" });
  parsedSuggestMock.mockReset().mockResolvedValue({ kind: "noCv" });
  skillSearchMock.mockReset().mockResolvedValue({ success: true, options: [] });
  skillSuggestMock.mockReset().mockResolvedValue({ kind: "noCv" });
});

describe("MatchPreferencesCard — code-split dialog (#748)", () => {
  it("does not evaluate the dialog module until 'Lägg till' is clicked", async () => {
    // Kill M2 (revert to static import): the module must not have loaded when
    // this test file imported the card.
    expect(evaluatedAtFileLoad).toBe(false);

    const user = userEvent.setup();
    renderCard();
    await flush();

    // Kill M1 (drop the gate): rendering the card must not mount the lazy dialog,
    // so its loader has not run even after a flushed tick. (DOM absence alone is
    // NOT the guarantee — a statically mounted CLOSED Radix dialog also renders
    // nothing; the eval spy is the killer.)
    expect(dialogModuleEvaluated.current).toBe(false);
    expect(screen.queryByRole("dialog")).toBeNull();

    await user.click(screen.getByRole("button", { name: "Lägg till" }));

    // First open loads the chunk and mounts the real dialog with props threaded
    // through the lazily-loaded boundary (taxonomy → sections). Kills M3 (trigger
    // sets only dialogOpen, not dialogRequested): the dialog would never mount.
    const dialog = await screen.findByRole("dialog");
    expect(dialog).toBeInTheDocument();
    expect(dialogModuleEvaluated.current).toBe(true);
    expect(screen.getByRole("group", { name: /yrke/i })).toBeInTheDocument();
  });

  it("reopens after close (gate latches; content remounts and reseeds)", async () => {
    const user = userEvent.setup();
    renderCard();

    await user.click(screen.getByRole("button", { name: "Lägg till" }));
    expect(await screen.findByRole("dialog")).toBeInTheDocument();

    await user.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());

    await user.click(screen.getByRole("button", { name: "Lägg till" }));
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
  });
});
