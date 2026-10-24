import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Clock } from "lucide-react";
import { JobbHeroFilters } from "./jobb-hero-filters";
import { HeroChip } from "./hero-chip";
import { ForetagSokSearchbar } from "@/components/company-criteria/foretag-sok-searchbar";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import type { CriterionReference } from "@/lib/dto/company-criteria";
import type { OccupationSearchResolver } from "@/lib/occupation-search/resolve-occupation-search";
import { IT_GROUP, ruleAnswer } from "@/test/fixtures/occupation-search";

/**
 * #2212: below the breakpoint every popover is a sheet under the header, its cascade shows one
 * column at a time, and focus moves into it.
 *
 * The client sizes are the layout engine's for a phone in portrait (375x812) and a desktop window
 * (1280x720), measured 2026-10-24 on 5ad51c9c1; a sheet does not read its trigger's rect. A coarse
 * pointer is what a touch device's browser answers to `(pointer: coarse)`.
 */

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock("@/lib/actions/company-follows", () => ({
  followCompanyAction: vi.fn(),
  unfollowCompanyAction: vi.fn(),
}));
const { resolveSearchMock } = vi.hoisted(() => ({ resolveSearchMock: vi.fn<OccupationSearchResolver>() }));
vi.mock("@/lib/occupation-search/resolve-occupation-search", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/occupation-search/resolve-occupation-search")>()),
  resolveOccupationSearch: resolveSearchMock,
}));

const taxonomy: TaxonomyTree = {
  employmentTypes: [{ conceptId: "PFZr_Syz_cUq", label: "Vanlig anställning" }],
  worktimeExtents: [],
  regions: [
    {
      conceptId: "CifL_Rzy_Mku",
      label: "Stockholms län",
      municipalities: [{ conceptId: "AvNB_uwa_6n6", label: "Stockholm" }],
    },
    {
      conceptId: "oDpK_oQy_3Zc",
      label: "Västra Götalands län",
      municipalities: [{ conceptId: "PVZL_BQT_XtL", label: "Göteborg" }],
    },
  ],
  occupationFields: [
    {
      conceptId: "apaJ_2ja_LuF",
      label: "Data/IT",
      occupationGroups: [{ conceptId: IT_GROUP.id, label: IT_GROUP.label }],
    },
  ],
};

const reference: CriterionReference = {
  sniVersion: "SNI2025.v1",
  kommunVersion: "2025",
  sni: [
    {
      code: "J",
      name: "Informations- och kommunikationsverksamhet",
      divisions: [{ code: "62", name: "Dataprogrammering", leaves: [{ code: "62100", name: "Dataprogrammering" }] }],
    },
  ],
  lan: [{ code: "01", name: "Stockholms län", kommuner: [{ code: "0180", name: "Stockholm" }] }],
};

function stubViewport(clientWidth: number, clientHeight: number) {
  vi.spyOn(Element.prototype, "clientWidth", "get").mockImplementation(function (this: Element) {
    return this === document.documentElement ? clientWidth : 0;
  });
  vi.spyOn(Element.prototype, "clientHeight", "get").mockImplementation(function (this: Element) {
    return this === document.documentElement ? clientHeight : 0;
  });
}

function stubCoarsePointer() {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: query === "(pointer: coarse)",
    media: query,
    onchange: null,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  }));
}

beforeEach(() => {
  resolveSearchMock.mockReset();
  resolveSearchMock.mockResolvedValue({ kind: "ok", result: ruleAnswer.it });
});
afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

function renderJobb() {
  render(
    <JobbHeroFilters
      taxonomy={taxonomy}
      initialOccupationGroup={[]}
      initialRegion={[]}
      initialMunicipality={[]}
      initialRemote={false}
      initialEmploymentType={[]}
      initialWorktimeExtent={[]}
      initialMatchGrades={[]}
      initialMatchningOff={false}
      initialIncludeRelated={false}
      initialHideApplied={false}
      initialOnlyMatched={false}
      hasStatedDesiredOccupation
      hasSeeker={false}
      q=""
      employer={[]}
      sortBy="PublishedAtDesc"
    />,
  );
}

function expectSheet(dialog: HTMLElement) {
  expect(dialog).toHaveAttribute("data-placement", "sheet");
  expect(dialog.style.top).toBe("");
  expect(dialog.style.left).toBe("");
  expect(dialog.style.width).toBe("");
}

describe("every popover is a sheet on a phone (#2212)", () => {
  it.each(["Ort", "Yrke", "Filter", "Matchning"])("/jobb %s at 375x812", async (name) => {
    stubViewport(375, 812);
    renderJobb();
    await userEvent.setup().click(screen.getByRole("button", { name: new RegExp(`^${name}`) }));

    expectSheet(screen.getByRole("dialog", { name }));
  });

  it("/foretag/sok Ort and Bransch at 375x812", async () => {
    stubViewport(375, 812);
    const user = userEvent.setup();
    render(<ForetagSokSearchbar reference={reference} referenceOk namn="" sni={[]} kommun={[]} />);

    await user.click(screen.getByRole("button", { name: "Välj ort eller län" }));
    expectSheet(screen.getByRole("dialog", { name: "Ort eller län" }));
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Stäng" }));

    await user.click(screen.getByRole("button", { name: /Välj bransch/ }));
    expectSheet(screen.getByRole("dialog", { name: "Välj bransch" }));
  });

  it("a hero chip at 375x812", async () => {
    stubViewport(375, 812);
    render(
      <HeroChip
        label="Senaste sökningar"
        icon={<Clock size={14} aria-hidden="true" />}
        count={1}
        items={[{ id: "a1", label: "projektledare" }]}
        getKey={(it) => it.id}
        getHref={(it) => `/jobb?q=${it.label}`}
        getLabel={(it) => it.label}
        emptyText="Inga senaste sökningar än."
      />,
    );
    await userEvent.setup().click(screen.getByRole("button", { name: /Senaste sökningar/ }));

    expectSheet(screen.getByRole("dialog", { name: "Senaste sökningar" }));
  });

  it("names the dialog the same in both placements", async () => {
    const user = userEvent.setup();
    stubViewport(1280, 720);
    renderJobb();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    expect(screen.getByRole("dialog", { name: "Ort" })).not.toHaveAttribute("data-placement");
    await user.keyboard("{Escape}");

    vi.restoreAllMocks();
    stubViewport(375, 812);
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    expect(screen.getByRole("dialog", { name: "Ort" })).toHaveAttribute("data-placement", "sheet");
  });
});

describe("a sheet's cascade shows one column at a time (#2212)", () => {
  it("drills into a län and back, with focus on the heading and then on the län's row", async () => {
    stubViewport(375, 812);
    const user = userEvent.setup();
    renderJobb();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    const dialog = screen.getByRole("dialog", { name: "Ort" });
    const columns = dialog.querySelectorAll<HTMLElement>(".jp-popover__col");
    expect(columns).toHaveLength(2);
    const [groups, items] = [columns[0]!, columns[1]!];

    // Level one: the län, and Distans beside them; the kommun column waits.
    expect(groups).not.toHaveAttribute("hidden");
    expect(items).toHaveAttribute("hidden");
    expect(within(dialog).getByRole("checkbox", { name: /^Distans/ })).toBeInTheDocument();

    await user.click(within(dialog).getByRole("button", { name: "Stockholms län" }));

    // Level two: the län's kommuner under its name, which holds focus; Distans is not here.
    expect(groups).toHaveAttribute("hidden");
    expect(items).not.toHaveAttribute("hidden");
    const heading = within(dialog).getByText("Stockholms län", { selector: ".jp-popover__grouphead" });
    expect(heading).toHaveFocus();
    expect(within(dialog).getByRole("group", { name: "Stockholms län" })).toBe(items);
    expect(within(dialog).queryByRole("checkbox", { name: /^Distans/ })).toBeNull();
    expect(within(items).getByRole("checkbox", { name: /^Stockholm/ })).toBeInTheDocument();

    await user.click(within(dialog).getByRole("button", { name: "Tillbaka" }));

    expect(groups).not.toHaveAttribute("hidden");
    expect(items).toHaveAttribute("hidden");
    expect(within(dialog).getByRole("button", { name: "Stockholms län" })).toHaveFocus();
    expect(document.activeElement).not.toBe(document.body);
  });

  it("keeps focus in the dialog when a resize makes an open popover a sheet at level two", async () => {
    // The window narrows from 1280x720 to 375x812 with Ort open and a län picked: the län's row,
    // which held focus, is hidden in the sheet.
    stubViewport(1280, 720);
    const user = userEvent.setup();
    renderJobb();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    await user.click(within(screen.getByRole("dialog", { name: "Ort" })).getByRole("button", { name: "Stockholms län" }));

    vi.restoreAllMocks();
    stubViewport(375, 812);
    act(() => {
      window.dispatchEvent(new Event("resize"));
    });

    const dialog = screen.getByRole("dialog", { name: "Ort" });
    expect(dialog).toHaveAttribute("data-placement", "sheet");
    expect(dialog.contains(document.activeElement)).toBe(true);
    expect(document.activeElement?.closest("[hidden]")).toBeNull();
  });

  it("drills in from the keyboard too", async () => {
    stubViewport(375, 812);
    const user = userEvent.setup();
    renderJobb();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    const dialog = screen.getByRole("dialog", { name: "Ort" });
    within(dialog).getByRole("button", { name: "Västra Götalands län" }).focus();

    await user.keyboard("{Enter}");

    expect(within(dialog).getByText("Västra Götalands län", { selector: ".jp-popover__grouphead" })).toHaveFocus();
  });

  it("keeps Yrke's search field at level one, and a word there shows its answer", async () => {
    stubViewport(375, 812);
    const user = userEvent.setup();
    renderJobb();
    await user.click(screen.getByRole("button", { name: /^Yrke/ }));
    const dialog = screen.getByRole("dialog", { name: "Yrke" });
    const field = within(dialog).getByRole("searchbox", { name: "Sök yrke" });
    await waitFor(() => expect(field).toHaveFocus());

    await user.type(field, "it");
    expect(
      await within(dialog).findByRole("checkbox", { name: (n) => n.startsWith(IT_GROUP.label) }),
    ).toBeInTheDocument();

    await user.clear(field);
    await user.click(await within(dialog).findByRole("button", { name: "Data/IT" }));
    expect(within(dialog).queryByRole("searchbox")).toBeNull();
  });
});

describe("a sheet closes from its band and returns focus to the pill (#2212)", () => {
  it.each(["Ort", "Matchning"])("/jobb %s", async (name) => {
    stubViewport(375, 812);
    const user = userEvent.setup();
    renderJobb();
    const pill = screen.getByRole("button", { name: new RegExp(`^${name}`) });
    await user.click(pill);

    await user.click(within(screen.getByRole("dialog", { name })).getByRole("button", { name: "Stäng" }));

    expect(screen.queryByRole("dialog", { name })).toBeNull();
    expect(pill).toHaveFocus();
  });

  it("has no close button when anchored", async () => {
    stubViewport(1280, 720);
    renderJobb();
    await userEvent.setup().click(screen.getByRole("button", { name: /^Ort/ }));

    expect(within(screen.getByRole("dialog", { name: "Ort" })).queryByRole("button", { name: "Stäng" })).toBeNull();
  });
});

describe("what takes focus when a popover opens (#2212)", () => {
  it("a sheet without a field focuses the dialog, so the pill it covers does not keep focus", async () => {
    stubViewport(375, 812);
    renderJobb();
    await userEvent.setup().click(screen.getByRole("button", { name: /^Ort/ }));

    expect(screen.getByRole("dialog", { name: "Ort" })).toHaveFocus();
  });

  it("a sheet with a field focuses the field for a fine pointer", async () => {
    stubViewport(375, 812);
    renderJobb();
    await userEvent.setup().click(screen.getByRole("button", { name: /^Yrke/ }));

    await waitFor(() => expect(screen.getByRole("searchbox", { name: "Sök yrke" })).toHaveFocus());
  });

  it("a sheet with a field focuses the dialog for a coarse pointer, so no soft keyboard rises", async () => {
    stubViewport(375, 812);
    stubCoarsePointer();
    renderJobb();
    await userEvent.setup().click(screen.getByRole("button", { name: /^Yrke/ }));

    await waitFor(() => expect(screen.getByRole("dialog", { name: "Yrke" })).toHaveFocus());
  });

  it("an anchored popover with a field focuses the dialog for a coarse pointer", async () => {
    // A tablet: 1024x768 is anchored, and its browser answers (pointer: coarse).
    stubViewport(1024, 768);
    stubCoarsePointer();
    renderJobb();
    await userEvent.setup().click(screen.getByRole("button", { name: /^Yrke/ }));

    await waitFor(() => expect(screen.getByRole("dialog", { name: "Yrke" })).toHaveFocus());
  });

  it("an anchored popover without a field leaves focus on its pill for a coarse pointer", async () => {
    stubViewport(1024, 768);
    stubCoarsePointer();
    renderJobb();
    const pill = screen.getByRole("button", { name: /^Ort/ });
    await userEvent.setup().click(pill);

    expect(pill).toHaveFocus();
  });
});
