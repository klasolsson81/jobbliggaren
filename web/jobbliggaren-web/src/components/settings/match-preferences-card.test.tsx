import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, within, waitFor } from "@testing-library/react";
import { render as rawRender } from "@testing-library/react/pure";
import { NextIntlClientProvider } from "next-intl";
import enMessages from "../../../messages/en";
import userEvent from "@testing-library/user-event";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import type { ActionResult } from "@/lib/actions/_action-result";

// Mock action-modulens exports (server-actions körs aldrig i jsdom). The suggest and search
// actions are there because the code-split dialog (#748) mounts the sections that reference them.
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
// The stale-build reload's one seam (ADR 0148): mocked so S1 can count it.
vi.mock("@/lib/stale-build/reload-document", () => ({ reloadDocument: vi.fn() }));
import { UnrecognizedActionError } from "next/dist/client/components/unrecognized-action-error";
import { reloadDocument } from "@/lib/stale-build/reload-document";
import {
  STALE_BUILD_RELOADED_NOTICE_KEY,
  STALE_BUILD_RELOAD_STAMP_KEY,
} from "@/lib/stale-build/stale-build-reload";

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
    occupationGroups: [{ conceptId: "grp_sjukskoterska", label: "Sjuksköterskor" }],
  },
];

const regions: ReadonlyArray<TaxonomyRegion> = [
  { conceptId: "region_sthlm", label: "Stockholms län", municipalities: [] },
  { conceptId: "region_vg", label: "Västra Götalands län", municipalities: [] },
];

const employmentTypes: ReadonlyArray<TaxonomyOption> = [
  {
    conceptId: "kpPX_CNN_gDU",
    label: "Tillsvidareanställning (inkl. eventuell provanställning)",
  },
  { conceptId: "gro4_cWF_6D7", label: "Vikariat" },
];

const PERMANENT = "Tillsvidareanställning (inkl. eventuell provanställning)";

// What the action answers. The refusal text is the action's own mapped copy, whatever the status.
const SAVED: ActionResult = { success: true };
const REFUSED_TEXT = "Ändringen kunde inte sparas. Försök igen om en stund.";
const REFUSED: ActionResult = { success: false, error: REFUSED_TEXT };

type CardProps = React.ComponentProps<typeof MatchPreferencesCard>;

function renderCard(overrides?: Partial<CardProps>) {
  return render(
    <MatchPreferencesCard
      occupationFields={occupationFields}
      regions={regions}
      employmentTypes={employmentTypes}
      initialOccupationGroups={[]}
      initialRegions={[]}
      initialMunicipalities={[]}
      initialRemote={false}
      initialEmploymentTypes={[]}
      initialSkills={[]}
      initialSkillGroups={[]}
      initialExperienceYears={null}
      initialOccupationExperience={[]}
      degraded={false}
      {...overrides}
    />
  );
}

// `hidden: true`: an open dialog hides the card from the accessibility tree, and a test still
// reads what the card shows behind it.
const part = (name: string) => screen.getByRole("region", { name, hidden: true });

/** The backdrop Radix renders behind an open dialog: a click on it is a click outside. */
function dialogOverlay(): HTMLElement {
  const overlay = document.querySelector<HTMLElement>('[data-slot="dialog-overlay"]');
  if (overlay === null) throw new Error("no dialog is open");
  return overlay;
}

/** The chips a part shows, by name. */
function chipsOf(name: string): string[] {
  return within(part(name))
    .queryAllByRole("button", { name: /^Ta bort /, hidden: true })
    .map((button) => (button.getAttribute("aria-label") ?? "").replace(/^Ta bort /, ""));
}

/** How to answer every held promise, so none is left out when its test ends. */
const releases: Array<() => void> = [];

/** Writes that stay out until the test answers them, in the order they were sent. */
function heldWrites() {
  const answers: Array<(result: ActionResult) => void> = [];
  updateMock.mockImplementation(
    () =>
      new Promise<ActionResult>((resolve) => {
        answers.push(resolve);
        releases.push(() => resolve(SAVED));
      })
  );
  return answers;
}

beforeEach(() => {
  updateMock.mockReset().mockResolvedValue(SAVED);
  cvSuggestMock.mockReset().mockResolvedValue({ kind: "noCv" });
  parsedSuggestMock.mockReset().mockResolvedValue({ kind: "noCv" });
  skillSearchMock.mockReset().mockResolvedValue({ success: true, options: [] });
  skillSuggestMock.mockReset().mockResolvedValue({ kind: "noCv" });
});

// A promise left out would hold its transition, and every later test's with it: React entangles
// pending async transitions. Writes still queued behind it are answered at once.
afterEach(() => {
  updateMock.mockResolvedValue(SAVED);
  for (const release of releases.splice(0)) release();
  // S1 leaves the two stamps, a Storage.prototype spy and a call on the seam behind, and a
  // later stale row would be refused by the 60 s guard — order-dependent, so reset here.
  sessionStorage.clear();
  vi.restoreAllMocks();
  vi.mocked(reloadDocument).mockClear();
});

describe("flattenOccupationGroups", () => {
  it("plattar nästlade yrkesgrupper till en enkel conceptId/label-lista", () => {
    expect(flattenOccupationGroups(occupationFields)).toEqual([
      { conceptId: "grp_backend", label: "Backendutvecklare" },
      { conceptId: "grp_frontend", label: "Frontendutvecklare" },
      { conceptId: "grp_sjukskoterska", label: "Sjuksköterskor" },
    ]);
  });
});

describe("filterOptions", () => {
  it("substring-filtrerar case-insensitivt", () => {
    const options = flattenOccupationGroups(occupationFields);
    expect(filterOptions(options, "UTVECKLARE").map((o) => o.conceptId)).toEqual([
      "grp_backend",
      "grp_frontend",
    ]);
  });
});

describe("U1: an h2 and five parts (#1918 Binding 1)", () => {
  it("names the card in an h2 and each part in an h3, in the settled order", () => {
    renderCard();
    expect(screen.getByRole("heading", { level: 2, name: "Matchning" })).toBeInTheDocument();
    expect(screen.getAllByRole("heading", { level: 3 }).map((h) => h.textContent)).toEqual([
      "Yrken",
      "Kompetenser",
      "Orter",
      "Anställningsformer",
      "Antal års erfarenhet",
    ]);
    for (const name of ["Yrken", "Kompetenser", "Orter", "Anställningsformer", "Antal års erfarenhet"]) {
      expect(part(name)).toBeInTheDocument();
    }
  });

  it("says what an empty part means, unchanged", () => {
    renderCard();
    expect(within(part("Yrken")).getByText("Alla yrken (inget valt)")).toBeInTheDocument();
    expect(within(part("Kompetenser")).getByText("Inga kompetenser valda")).toBeInTheDocument();
    expect(within(part("Orter")).getByText("Hela landet (ingen ort vald)")).toBeInTheDocument();
    expect(
      within(part("Anställningsformer")).getByText("Alla anställningsformer (inget valt)")
    ).toBeInTheDocument();
    expect(within(part("Antal års erfarenhet")).getByText("Inget angivet")).toBeInTheDocument();
  });

  it("shows chosen values as removable chips with their names", () => {
    renderCard({
      initialOccupationGroups: ["grp_backend"],
      initialRegions: ["region_sthlm"],
      initialRemote: true,
      initialEmploymentTypes: ["gro4_cWF_6D7"],
      initialExperienceYears: 5,
    });
    expect(chipsOf("Yrken")).toEqual(["Backendutvecklare"]);
    // Distans first, the widest choice.
    expect(chipsOf("Orter")).toEqual(["Distans", "Stockholms län"]);
    expect(chipsOf("Anställningsformer")).toEqual(["Vikariat"]);
    expect(within(part("Antal års erfarenhet")).getByText("5 år")).toBeInTheDocument();
  });

  it("names saved skills from the resolved groups on a cold load (ADR 0047), one chip per twin", () => {
    renderCard({
      initialSkills: ["esco_csharp", "af_csharp", "skill_gone"],
      initialSkillGroups: [
        { conceptId: "esco_csharp", label: "C#", memberConceptIds: ["esco_csharp", "af_csharp"] },
      ],
    });
    // An id no group names falls back on the id rather than vanishing.
    expect(chipsOf("Kompetenser")).toEqual(["C#", "skill_gone"]);
  });

  it("carries no card-level Lägg till and no card-level status", () => {
    renderCard();
    expect(screen.queryByRole("button", { name: "Lägg till" })).toBeNull();
    const card = screen.getByRole("heading", { level: 2 }).closest("section")!;
    const statuses = within(card).getAllByRole("status");
    // One status line per part, inside the part.
    expect(statuses).toHaveLength(5);
    for (const status of statuses) expect(status.closest(".jp-settings-group")).not.toBeNull();
  });

  it("degraded → one sentence, no parts and no buttons", () => {
    renderCard({ degraded: true });
    expect(screen.getByText(/Dina matchningsval kunde inte läsas in just nu/)).toBeInTheDocument();
    expect(screen.queryByRole("region")).toBeNull();
    expect(screen.queryByRole("button")).toBeNull();
  });
});

describe("the part's button (#1918 Binding 2)", () => {
  it("reads Lägg till when the part is empty and Ändra when it holds something, named with its part", () => {
    renderCard({ initialSkills: ["skill_react"], initialExperienceYears: 0 });

    const add = screen.getByRole("button", { name: "Lägg till Yrken" });
    expect(add).toHaveTextContent(/^Lägg till$/);
    expect(add).toHaveAttribute("aria-haspopup", "dialog");
    expect(screen.getByRole("button", { name: "Ändra Kompetenser" })).toHaveTextContent(/^Ändra$/);
    expect(screen.getByRole("button", { name: "Lägg till Orter" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Lägg till Anställningsformer" })).toBeInTheDocument();
    // Zero years is stated, not empty.
    expect(screen.getByRole("button", { name: "Ändra Antal års erfarenhet" })).toBeInTheDocument();
  });

  it("switches Ändra to Lägg till when the last chip goes, and stays the same element", async () => {
    const user = userEvent.setup();
    renderCard({ initialEmploymentTypes: ["gro4_cWF_6D7"] });

    const button = screen.getByRole("button", { name: "Ändra Anställningsformer" });
    await user.click(screen.getByRole("button", { name: "Ta bort Vikariat" }));

    expect(screen.getByRole("button", { name: "Lägg till Anställningsformer" })).toBe(button);
  });
});

describe("a removal writes its part and only its part (#1918 Binding 7, ADR 0147)", () => {
  it("Yrken: the groups left, and no years key, so the server keeps the years (D2)", async () => {
    const user = userEvent.setup();
    renderCard({
      initialOccupationGroups: ["grp_backend", "grp_frontend"],
      initialOccupationExperience: [{ conceptId: "grp_frontend", years: 3 }],
      initialRegions: ["region_sthlm"],
    });

    await user.click(screen.getByRole("button", { name: "Ta bort Backendutvecklare" }));

    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(1));
    expect(updateMock.mock.calls[0]![0]).toEqual({
      occupations: { preferredOccupationGroups: ["grp_frontend"] },
    });
    expect(updateMock.mock.calls[0]![0].occupations).not.toHaveProperty(
      "preferredOccupationExperience"
    );
  });

  it("Kompetenser: a twin chip drops both member ids", async () => {
    const user = userEvent.setup();
    renderCard({
      initialSkills: ["esco_csharp", "af_csharp"],
      initialSkillGroups: [
        { conceptId: "esco_csharp", label: "C#", memberConceptIds: ["esco_csharp", "af_csharp"] },
      ],
    });

    await user.click(screen.getByRole("button", { name: "Ta bort C#" }));

    await waitFor(() =>
      expect(updateMock).toHaveBeenCalledWith({ skills: { preferredSkills: [] } })
    );
  });

  it("Orter: a municipality goes, region and distans travel along unchanged", async () => {
    const user = userEvent.setup();
    renderCard({
      initialRegions: ["region_sthlm"],
      initialMunicipalities: ["mun_a", "mun_b"],
      initialRemote: true,
    });

    await user.click(screen.getByRole("button", { name: "Ta bort mun_a" }));

    await waitFor(() =>
      expect(updateMock).toHaveBeenCalledWith({
        locations: {
          preferredRegions: ["region_sthlm"],
          preferredMunicipalities: ["mun_b"],
          preferredRemote: true,
        },
      })
    );
  });

  it("Orter: Distans is a flag, and its chip writes it false (#551 punkt 4)", async () => {
    const user = userEvent.setup();
    renderCard({ initialRegions: ["region_sthlm"], initialRemote: true });

    await user.click(screen.getByRole("button", { name: "Ta bort Distans" }));

    await waitFor(() =>
      expect(updateMock).toHaveBeenCalledWith({
        locations: {
          preferredRegions: ["region_sthlm"],
          preferredMunicipalities: [],
          preferredRemote: false,
        },
      })
    );
  });

  it("Anställningsformer: the types left", async () => {
    const user = userEvent.setup();
    renderCard({ initialEmploymentTypes: ["kpPX_CNN_gDU", "gro4_cWF_6D7"] });

    await user.click(screen.getByRole("button", { name: "Ta bort Vikariat" }));

    await waitFor(() =>
      expect(updateMock).toHaveBeenCalledWith({
        employmentTypes: { preferredEmploymentTypes: ["kpPX_CNN_gDU"] },
      })
    );
  });
});

describe("the outcome sits under its part (#1918 Major 5, #1391)", () => {
  it("a saved removal says Sparat HH:mm under the part", async () => {
    const user = userEvent.setup();
    renderCard({ initialRegions: ["region_sthlm"], initialEmploymentTypes: ["gro4_cWF_6D7"] });

    await user.click(screen.getByRole("button", { name: "Ta bort Stockholms län" }));

    expect(
      await within(part("Orter")).findByText(/^Sparat \d{2}:\d{2}$/)
    ).toBeInTheDocument();
    expect(within(part("Anställningsformer")).queryByText(/^Sparat/)).toBeNull();
  });

  it("a refused removal brings the chip back and says why under the part", async () => {
    const user = userEvent.setup();
    updateMock.mockResolvedValue(REFUSED);
    renderCard({ initialRegions: ["region_sthlm"] });

    await user.click(screen.getByRole("button", { name: "Ta bort Stockholms län" }));

    const alert = await within(part("Orter")).findByRole("alert");
    expect(alert).toHaveTextContent(REFUSED_TEXT);
    expect(chipsOf("Orter")).toEqual(["Stockholms län"]);
    expect(screen.getByRole("button", { name: "Ändra Orter" })).toHaveAttribute(
      "aria-describedby",
      alert.id
    );
  });

  it("a write the action cannot reach brings the chip back in the connection copy", async () => {
    const user = userEvent.setup();
    updateMock.mockRejectedValue(new TypeError("Failed to fetch"));
    renderCard({ initialEmploymentTypes: ["gro4_cWF_6D7"] });

    await user.click(screen.getByRole("button", { name: "Ta bort Vikariat" }));

    expect(await within(part("Anställningsformer")).findByRole("alert")).toHaveTextContent(
      "Kunde inte nå servern. Kontrollera din nätverksanslutning."
    );
    expect(chipsOf("Anställningsformer")).toEqual(["Vikariat"]);
    // S2 (ADR 0148 D8): a generic rejection is not a stale build — no reload.
    expect(reloadDocument).not.toHaveBeenCalled();
  });

  it("S1 (ADR 0148 D8): a write refused as a stale-build action id reloads the page and shows no inline error", async () => {
    // The card catches its action's rejection itself (module-level
    // startTransition), so no boundary ever sees it: the catch calls the core.
    // The error is the router's own class, what `server-action-reducer.js`
    // constructs on `x-nextjs-action-not-found: 1`.
    const user = userEvent.setup();
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    updateMock.mockRejectedValue(
      new UnrecognizedActionError('Server Action "00a89fc0" was not found on the server.')
    );
    renderCard({ initialEmploymentTypes: ["gro4_cWF_6D7"] });

    await user.click(screen.getByRole("button", { name: "Ta bort Vikariat" }));

    await vi.waitFor(() => expect(reloadDocument).toHaveBeenCalledTimes(1));
    expect(within(part("Anställningsformer")).queryByRole("alert")).not.toBeInTheDocument();
    expect(setItem.mock.calls.map(([key]) => key)).toEqual([
      STALE_BUILD_RELOAD_STAMP_KEY,
      STALE_BUILD_RELOADED_NOTICE_KEY,
    ]);
  });
});

// ADR 0147 D9 and design-reviewer Major 6: after every sequence the part shows exactly what the
// server holds. The server here is the last write the action accepted.
describe("(d): a part's writes run in turn, each bound when it runs", () => {
  const STHLM = "Stockholms län";
  const VG = "Västra Götalands län";

  it.each([
    // [first, second, shown after the first answer, the second write's regions,
    //  what the server then holds, what the card shows at the end]
    ["fail", "ok", [STHLM], ["region_sthlm"], ["region_sthlm"], [STHLM]],
    ["fail", "fail", [STHLM], ["region_sthlm"], ["region_sthlm", "region_vg"], [STHLM, VG]],
    ["ok", "fail", [], [], ["region_vg"], [VG]],
    ["ok", "ok", [], [], [], []],
  ] as const)(
    "%s/%s: the second write is bound after the first answer, and the card ends as the server",
    async (first, second, afterFirst, secondRegions, serverHolds, shown) => {
      const user = userEvent.setup();
      const answers = heldWrites();
      renderCard({ initialRegions: ["region_sthlm", "region_vg"] });
      let server: ReadonlyArray<string> = ["region_sthlm", "region_vg"];
      const answer = (index: number, outcome: "ok" | "fail") => {
        if (outcome === "ok") {
          server = updateMock.mock.calls[index]![0].locations.preferredRegions;
        }
        answers[index]!(outcome === "ok" ? SAVED : REFUSED);
      };

      await user.click(screen.getByRole("button", { name: `Ta bort ${STHLM}` }));
      await user.click(screen.getByRole("button", { name: `Ta bort ${VG}` }));

      // The second removal waits for the first answer: one write out, both chips hidden.
      expect(updateMock).toHaveBeenCalledTimes(1);
      expect(updateMock.mock.calls[0]![0].locations.preferredRegions).toEqual(["region_vg"]);
      expect(chipsOf("Orter")).toEqual([]);

      answer(0, first);
      await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(2));
      expect(updateMock.mock.calls[1]![0].locations.preferredRegions).toEqual(secondRegions);
      // A refusal restores its own chip only; the removal still out stays hidden.
      expect(chipsOf("Orter")).toEqual(afterFirst);

      answer(1, second);
      // Settled once the part shows the second write's outcome and no removal is out.
      await waitFor(() => {
        expect(chipsOf("Orter")).toEqual(shown);
        expect(
          within(part("Orter")).getByText(second === "ok" ? /^Sparat \d{2}:\d{2}$/ : REFUSED_TEXT)
        ).toBeInTheDocument();
      });
      expect(server).toEqual(serverHolds);
      expect(chipsOf("Orter")).toEqual(
        regions.filter((r) => server.includes(r.conceptId)).map((r) => r.label)
      );
    }
  );

  // dotnet-architect N1: with three writes, "minus its own members" and "minus every pending
  // removal" part ways.
  it("each write carries only its own removal: A, B and C removed while the first is out", async () => {
    const user = userEvent.setup();
    const answers = heldWrites();
    renderCard({
      regions: [...regions, { conceptId: "region_skane", label: "Skåne län", municipalities: [] }],
      initialRegions: ["region_sthlm", "region_vg", "region_skane"],
    });
    const sent = (index: number): ReadonlyArray<string> =>
      updateMock.mock.calls[index]![0].locations.preferredRegions;
    let server: ReadonlyArray<string> = ["region_sthlm", "region_vg", "region_skane"];
    const answer = (index: number, result: ActionResult) => {
      if (result.success) server = sent(index);
      answers[index]!(result);
    };

    await user.click(screen.getByRole("button", { name: `Ta bort ${STHLM}` }));
    await user.click(screen.getByRole("button", { name: `Ta bort ${VG}` }));
    await user.click(screen.getByRole("button", { name: "Ta bort Skåne län" }));

    expect(updateMock).toHaveBeenCalledTimes(1);
    expect(sent(0)).toEqual(["region_vg", "region_skane"]);
    expect(chipsOf("Orter")).toEqual([]);

    answer(0, SAVED);
    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(2));
    expect(sent(1)).toEqual(["region_skane"]);

    answer(1, SAVED);
    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(3));
    expect(sent(2)).toEqual([]);

    answer(2, REFUSED);
    await waitFor(() => {
      expect(chipsOf("Orter")).toEqual(["Skåne län"]);
      expect(within(part("Orter")).getByText(REFUSED_TEXT)).toBeInTheDocument();
    });
    expect(server).toEqual(["region_skane"]);
  });

  it("never disables a chip or the part's button while a write is out", async () => {
    const user = userEvent.setup();
    const answers = heldWrites();
    renderCard({ initialRegions: ["region_sthlm", "region_vg"] });

    await user.click(screen.getByRole("button", { name: `Ta bort ${STHLM}` }));

    expect(screen.getByRole("button", { name: `Ta bort ${VG}` })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Ändra Orter" })).toBeEnabled();
    answers[0]!(SAVED);
    await within(part("Orter")).findByText(/^Sparat/);
  });

  it("gives each part its own queue: a write to one part does not wait on another", async () => {
    const user = userEvent.setup();
    heldWrites();
    renderCard({ initialRegions: ["region_sthlm"], initialEmploymentTypes: ["gro4_cWF_6D7"] });

    await user.click(screen.getByRole("button", { name: `Ta bort ${STHLM}` }));
    await user.click(screen.getByRole("button", { name: "Ta bort Vikariat" }));

    expect(updateMock).toHaveBeenCalledTimes(2);
    expect(updateMock.mock.calls.map((call) => Object.keys(call[0]))).toEqual([
      ["locations"],
      ["employmentTypes"],
    ]);
  });
});

// design-reviewer Blocker 1: every removal moves focus on, whichever key or pointer did it.
describe("focus after a removal (#1918 B1, WCAG 2.4.3)", () => {
  it.each([
    ["Enter", "{Enter}"],
    ["Space", " "],
    ["Delete", "{Delete}"],
    ["Backspace", "{Backspace}"],
  ])("%s on a ⨯ moves focus to the next chip's ⨯", async (_key, keys) => {
    const user = userEvent.setup();
    renderCard({ initialRegions: ["region_sthlm", "region_vg"] });

    screen.getByRole("button", { name: "Ta bort Stockholms län" }).focus();
    await user.keyboard(keys);

    expect(screen.getByRole("button", { name: "Ta bort Västra Götalands län" })).toHaveFocus();
    expect(document.body).not.toHaveFocus();
  });

  it("a click moves focus to the next chip's ⨯", async () => {
    const user = userEvent.setup();
    renderCard({ initialEmploymentTypes: ["kpPX_CNN_gDU", "gro4_cWF_6D7"] });

    await user.click(screen.getByRole("button", { name: `Ta bort ${PERMANENT}` }));

    expect(screen.getByRole("button", { name: "Ta bort Vikariat" })).toHaveFocus();
  });

  it("without a next chip, focus goes to the previous one", async () => {
    const user = userEvent.setup();
    renderCard({ initialRemote: true, initialRegions: ["region_sthlm"] });

    screen.getByRole("button", { name: "Ta bort Stockholms län" }).focus();
    await user.keyboard("{Delete}");

    expect(screen.getByRole("button", { name: "Ta bort Distans" })).toHaveFocus();
  });

  it("between two chips, focus goes to the next one, not the previous", async () => {
    const user = userEvent.setup();
    renderCard({ initialRemote: true, initialRegions: ["region_sthlm", "region_vg"] });

    screen.getByRole("button", { name: "Ta bort Stockholms län" }).focus();
    await user.keyboard("{Delete}");

    expect(screen.getByRole("button", { name: "Ta bort Västra Götalands län" })).toHaveFocus();
  });

  it("the last chip leaves focus on the part's own button", async () => {
    const user = userEvent.setup();
    renderCard({ initialSkills: ["skill_react"], initialSkillGroups: [
      { conceptId: "skill_react", label: "React", memberConceptIds: ["skill_react"] },
    ] });

    screen.getByRole("button", { name: "Ta bort React" }).focus();
    await user.keyboard("{Enter}");

    expect(screen.getByRole("button", { name: "Lägg till Kompetenser" })).toHaveFocus();
  });
});

describe("the part's dialog (#1918 Binding 3–5)", () => {
  it("opens titled by the part, its picker open when the part is empty, its title focused", async () => {
    const user = userEvent.setup();
    renderCard();

    await user.click(screen.getByRole("button", { name: "Lägg till Yrken" }));

    const dialog = await screen.findByRole("dialog", { name: "Yrken" });
    expect(within(dialog).getByRole("heading", { name: "Yrken" })).toHaveFocus();
    expect(within(dialog).getByRole("button", { name: "Lägg till yrken" })).toHaveAttribute(
      "aria-expanded",
      "true"
    );
  });

  it("opens Antal års erfarenhet with the field focused", async () => {
    const user = userEvent.setup();
    renderCard({ initialExperienceYears: 4 });

    await user.click(screen.getByRole("button", { name: "Ändra Antal års erfarenhet" }));

    const field = await screen.findByRole("spinbutton", { name: "Antal års erfarenhet" });
    expect(field).toHaveFocus();
    expect(field).toHaveValue(4);
  });

  it.each([
    ["Escape", async (user: ReturnType<typeof userEvent.setup>) => user.keyboard("{Escape}")],
    ["Avbryt", async (user: ReturnType<typeof userEvent.setup>) =>
      user.click(screen.getByRole("button", { name: "Avbryt" }))],
    ["×", async (user: ReturnType<typeof userEvent.setup>) =>
      user.click(screen.getByRole("button", { name: "Stäng" }))],
    ["a click on the overlay", async (user: ReturnType<typeof userEvent.setup>) =>
      user.click(dialogOverlay())],
    ["Spara", async (user: ReturnType<typeof userEvent.setup>) =>
      user.click(screen.getByRole("button", { name: "Spara orter" }))],
  ])("closing by %s puts focus back on the part's button", async (_way, close) => {
    const user = userEvent.setup();
    renderCard({ initialRegions: ["region_sthlm"] });

    await user.click(screen.getByRole("button", { name: "Ändra Orter" }));
    await screen.findByRole("dialog", { name: "Orter" });
    await close(user);

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Ändra Orter" })).toHaveFocus()
    );
  });

  it("a save writes only its part, shows the receipt under it, and closes", async () => {
    const user = userEvent.setup();
    renderCard({
      initialSkills: ["skill_react", "skill_sql"],
      initialSkillGroups: [
        { conceptId: "skill_react", label: "React", memberConceptIds: ["skill_react"] },
        { conceptId: "skill_sql", label: "SQL", memberConceptIds: ["skill_sql"] },
      ],
      initialRegions: ["region_sthlm"],
    });

    await user.click(screen.getByRole("button", { name: "Ändra Kompetenser" }));
    const dialog = await screen.findByRole("dialog", { name: "Kompetenser" });
    await user.click(within(dialog).getByRole("button", { name: "Ta bort React" }));
    await user.click(within(dialog).getByRole("button", { name: "Spara kompetenser" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(updateMock).toHaveBeenCalledTimes(1);
    expect(updateMock).toHaveBeenCalledWith({ skills: { preferredSkills: ["skill_sql"] } });
    expect(chipsOf("Kompetenser")).toEqual(["SQL"]);
    expect(within(part("Kompetenser")).getByText(/^Sparat \d{2}:\d{2}$/)).toBeInTheDocument();
  });

  it("Yrken sends its years, as it renders them", async () => {
    const user = userEvent.setup();
    renderCard({
      initialOccupationGroups: ["grp_backend"],
      initialOccupationExperience: [{ conceptId: "grp_backend", years: 4 }],
    });

    await user.click(screen.getByRole("button", { name: "Ändra Yrken" }));
    await screen.findByRole("dialog", { name: "Yrken" });
    await user.click(screen.getByRole("button", { name: "Spara yrken" }));

    await waitFor(() =>
      expect(updateMock).toHaveBeenCalledWith({
        occupations: {
          preferredOccupationGroups: ["grp_backend"],
          preferredOccupationExperience: [{ conceptId: "grp_backend", years: 4 }],
        },
      })
    );
  });

  it.each([
    ["a number", "7", 7, "7 år", "Ändra Antal års erfarenhet"],
    ["an emptied field", "", null, "Inget angivet", "Lägg till Antal års erfarenhet"],
  ])("Antal års erfarenhet: %s is written and shown", async (_row, typed, years, text, button) => {
    const user = userEvent.setup();
    renderCard({ initialExperienceYears: 3 });

    await user.click(screen.getByRole("button", { name: "Ändra Antal års erfarenhet" }));
    const field = await screen.findByRole("spinbutton", { name: "Antal års erfarenhet" });
    await user.clear(field);
    if (typed !== "") await user.type(field, typed);
    await user.click(screen.getByRole("button", { name: "Spara erfarenhet" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(updateMock).toHaveBeenCalledWith({ experience: { experienceYears: years } });
    expect(within(part("Antal års erfarenhet")).getByText(text)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: button })).toBeInTheDocument();
  });

  it("a refused save stays open with its draft and says why in the foot", async () => {
    const user = userEvent.setup();
    updateMock.mockResolvedValue(REFUSED);
    renderCard({ initialEmploymentTypes: ["gro4_cWF_6D7"] });

    await user.click(screen.getByRole("button", { name: "Ändra Anställningsformer" }));
    const dialog = await screen.findByRole("dialog", { name: "Anställningsformer" });
    await user.click(within(dialog).getByRole("checkbox", { name: PERMANENT }));
    await user.click(within(dialog).getByRole("button", { name: "Spara anställningsformer" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(REFUSED_TEXT);
    expect(screen.getByRole("dialog", { name: "Anställningsformer" })).toBeInTheDocument();
    expect(within(dialog).getByRole("checkbox", { name: PERMANENT })).toHaveAttribute(
      "aria-checked",
      "true"
    );
    // The card shows what the server holds: the refused draft reached nothing.
    expect(chipsOf("Anställningsformer")).toEqual(["Vikariat"]);
  });
});

// design-reviewer's ruling (a1) on PR #1928, and code-reviewer's (c).
describe("the part's dialog while its save is out", () => {
  type User = ReturnType<typeof userEvent.setup>;

  /** Opens Orter, removes Stockholms län from the draft and saves, with the write held. */
  async function saveHeld(user: User) {
    const answers = heldWrites();
    renderCard({ initialRegions: ["region_sthlm", "region_vg"] });
    await user.click(screen.getByRole("button", { name: "Ändra Orter" }));
    const dialog = await screen.findByRole("dialog", { name: "Orter" });
    const status = within(dialog).getByRole("status");
    expect(status.textContent).toBe("");
    await user.click(within(dialog).getByRole("button", { name: "Ta bort Stockholms län" }));
    await user.click(within(dialog).getByRole("button", { name: "Spara orter" }));
    return { answers, dialog, status };
  }

  it.each([
    ["Escape", async (user: User) => user.keyboard("{Escape}")],
    ["×", async (user: User) => user.click(screen.getByRole("button", { name: "Stäng" }))],
    ["a click on the overlay", async (user: User) => user.click(dialogOverlay())],
  ])("%s does nothing while the save runs, and the landed save then closes it", async (_way, attempt) => {
    const user = userEvent.setup();
    const { answers, dialog } = await saveHeld(user);

    await attempt(user);

    expect(screen.getByRole("dialog", { name: "Orter" })).toBe(dialog);
    expect(within(dialog).getByRole("button", { name: "Sparar…" })).toBeDisabled();
    expect(within(dialog).getByRole("status")).toHaveTextContent("Sparar…");
    expect(within(dialog).getByRole("button", { name: "Ta bort Västra Götalands län" })).toBeEnabled();

    answers[0]!(SAVED);

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Ändra Orter" })).toHaveFocus()
    );
    expect(within(part("Orter")).getByText(/^Sparat \d{2}:\d{2}$/)).toBeInTheDocument();
    expect(chipsOf("Orter")).toEqual(["Västra Götalands län"]);
    expect(updateMock).toHaveBeenCalledTimes(1);
  });

  it("a refused save stays open with the draft and the alert, and Esc then closes it", async () => {
    const user = userEvent.setup();
    const { answers, dialog, status } = await saveHeld(user);

    answers[0]!(REFUSED);

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(REFUSED_TEXT);
    expect(status.textContent).toBe("");
    expect(within(dialog).queryByRole("button", { name: "Ta bort Stockholms län" })).toBeNull();
    expect(within(dialog).getByRole("button", { name: "Ta bort Västra Götalands län" })).toBeInTheDocument();
    expect(chipsOf("Orter")).toEqual(["Stockholms län", "Västra Götalands län"]);
    await waitFor(() =>
      expect(within(dialog).getByRole("button", { name: "Spara orter" })).toHaveFocus()
    );

    await user.keyboard("{Escape}");

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Ändra Orter" })).toHaveFocus()
    );
  });

  // design-reviewer Blocker 1: the status stays mounted beside the alert, never in its place.
  it("a retry after a refusal says Sparar… in the same status node", async () => {
    const user = userEvent.setup();
    const { answers, dialog, status } = await saveHeld(user);
    expect(status).toHaveTextContent("Sparar…");

    answers[0]!(REFUSED);
    await within(dialog).findByRole("alert");
    expect(within(dialog).getByRole("status")).toBe(status);
    expect(status.textContent).toBe("");

    await user.click(within(dialog).getByRole("button", { name: "Spara orter" }));

    expect(within(dialog).getByRole("status")).toBe(status);
    expect(status).toHaveTextContent("Sparar…");
    expect(within(dialog).queryByRole("alert")).toBeNull();
  });

  // design-reviewer Minor 2: M5 puts a dialog save's refusal in the foot and its receipt under the part.
  it("a refused save is told once, in the dialog's foot and never also under the part", async () => {
    const user = userEvent.setup();
    const { answers, dialog } = await saveHeld(user);

    answers[0]!(REFUSED);

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(REFUSED_TEXT);
    expect(screen.getAllByText(REFUSED_TEXT)).toHaveLength(1);
    expect(within(part("Orter")).queryByRole("alert", { hidden: true })).toBeNull();

    await user.keyboard("{Escape}");

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(within(part("Orter")).queryByRole("alert")).toBeNull();
  });

  // code-reviewer's new Major and dotnet-architect N4: the lock follows the dialog's own save, and
  // React ties every concurrent async transition together.
  it.each([
    ["Escape", async (user: User) => user.keyboard("{Escape}")],
    ["×", async (user: User) => user.click(screen.getByRole("button", { name: "Stäng" }))],
    ["a click on the overlay", async (user: User) => user.click(dialogOverlay())],
  ])("after a refusal, %s closes it while another part's write is still out", async (_way, close) => {
    const user = userEvent.setup();
    const answers = heldWrites();
    renderCard({
      initialRegions: ["region_sthlm", "region_vg"],
      initialEmploymentTypes: ["gro4_cWF_6D7"],
    });

    await user.click(screen.getByRole("button", { name: "Ta bort Vikariat" }));
    await user.click(screen.getByRole("button", { name: "Ändra Orter" }));
    const dialog = await screen.findByRole("dialog", { name: "Orter" });
    await user.click(within(dialog).getByRole("button", { name: "Spara orter" }));
    expect(updateMock).toHaveBeenCalledTimes(2);

    answers[1]!(REFUSED);
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(REFUSED_TEXT);

    await close(user);

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Ändra Orter" })).toHaveFocus()
    );
  });

  it("after a refusal, Esc closes it while the editor's skill search is still out", async () => {
    const user = userEvent.setup();
    const answers = heldWrites();
    skillSearchMock.mockImplementation(
      () =>
        new Promise((resolve) => {
          releases.push(() => resolve({ success: true, options: [] }));
        })
    );
    renderCard();

    await user.click(screen.getByRole("button", { name: "Lägg till Kompetenser" }));
    const dialog = await screen.findByRole("dialog", { name: "Kompetenser" });
    await user.click(within(dialog).getByRole("button", { name: "Spara kompetenser" }));
    await user.type(within(dialog).getByLabelText("Sök kompetens"), "rea");
    await waitFor(() => expect(skillSearchMock).toHaveBeenCalledWith("rea"));

    answers[0]!(REFUSED);
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(REFUSED_TEXT);

    await user.keyboard("{Escape}");

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Lägg till Kompetenser" })).toHaveFocus()
    );
  });

  it("waits for a removal still out in the part: one write at a time, the dialog's after the removal's answer", async () => {
    const user = userEvent.setup();
    const answers = heldWrites();
    renderCard({ initialRegions: ["region_sthlm", "region_vg"] });

    await user.click(screen.getByRole("button", { name: "Ta bort Stockholms län" }));
    await user.click(screen.getByRole("button", { name: "Ändra Orter" }));
    const dialog = await screen.findByRole("dialog", { name: "Orter" });
    await user.click(within(dialog).getByRole("button", { name: "Ta bort Västra Götalands län" }));
    await user.click(within(dialog).getByRole("button", { name: "Spara orter" }));

    expect(updateMock).toHaveBeenCalledTimes(1);
    expect(updateMock.mock.calls[0]![0].locations.preferredRegions).toEqual(["region_vg"]);
    expect(within(dialog).getByRole("button", { name: "Sparar…" })).toBeDisabled();

    answers[0]!(SAVED);

    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(2));
    expect(updateMock.mock.calls[1]![0]).toEqual({
      locations: { preferredRegions: [], preferredMunicipalities: [], preferredRemote: false },
    });

    answers[1]!(SAVED);

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(chipsOf("Orter")).toEqual([]);
  });
});

describe("MatchPreferencesCard — locale en (#1537, #1918 m9)", () => {
  function renderEnglish(overrides: Partial<CardProps>) {
    rawRender(
      <NextIntlClientProvider locale="en" messages={enMessages} timeZone="Europe/Stockholm">
        <MatchPreferencesCard
          occupationFields={occupationFields}
          regions={regions}
          employmentTypes={employmentTypes}
          initialOccupationGroups={[]}
          initialRegions={[]}
          initialMunicipalities={[]}
          initialRemote={false}
          initialEmploymentTypes={[]}
          initialSkills={[]}
          initialSkillGroups={[]}
          initialExperienceYears={null}
          initialOccupationExperience={[]}
          degraded={false}
          {...overrides}
        />
      </NextIntlClientProvider>
    );
  }

  it("namnger anställningsformen ur katalogen, inte ur propens källetikett", () => {
    renderEnglish({ initialEmploymentTypes: ["kpPX_CNN_gDU"] });
    expect(
      screen.getByText(/Permanent employment \(including any trial employment\)/)
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/Tillsvidareanställning \(inkl\. eventuell provanställning\)/)
    ).toBeNull();
    expect(screen.getByRole("button", { name: "Change Employment types" })).toBeInTheDocument();
  });

  it.each([
    [1, "1 year"],
    [3, "3 years"],
  ])("says %i year(s) in the plural English needs", (years, text) => {
    renderEnglish({ initialExperienceYears: years });
    expect(screen.getByText(text)).toBeInTheDocument();
  });
});
