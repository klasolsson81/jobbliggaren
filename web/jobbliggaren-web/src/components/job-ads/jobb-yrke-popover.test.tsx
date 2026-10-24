import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, within, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobbHeroFilters } from "./jobb-hero-filters";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import type { OccupationSearchResolver } from "@/lib/occupation-search/resolve-occupation-search";
import { buildJobbHref } from "@/lib/job-ads/search-params";
import { EL_GROUP, IT_GROUP, USK_VARD_GROUP, ruleAnswer } from "@/test/fixtures/occupation-search";

const pushMock = vi.fn<(href: string) => void>();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
}));

// #2198: the search's client read, so no test reaches a network. Every answer is
// OccupationSearchRule's own v30 output (`ruleAnswer`).
const { resolveSearchMock } = vi.hoisted(() => ({
  resolveSearchMock: vi.fn<OccupationSearchResolver>(),
}));
vi.mock("@/lib/occupation-search/resolve-occupation-search", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/occupation-search/resolve-occupation-search")>()),
  resolveOccupationSearch: resolveSearchMock,
}));

// The undersköterska answer's other group, from the same v30 answer.
const USK_HEMTJANST_GROUP = {
  id: "jY19_knH_MJp",
  label: "Undersköterskor, hemtjänst, hemsjukvård, äldreboende och habilitering",
} as const;

// The v30 fields of every group these tests assert, so a search pick and a browse pick compare.
const taxonomy: TaxonomyTree = {
  employmentTypes: [],
  worktimeExtents: [],
  regions: [
    {
      conceptId: "CifL_Rzy_Mku",
      label: "Stockholms län",
      municipalities: [{ conceptId: "AvNB_uwa_6n6", label: "Stockholm" }],
    },
  ],
  occupationFields: [
    {
      conceptId: "apaJ_2ja_LuF",
      label: "Data/IT",
      occupationGroups: [{ conceptId: IT_GROUP.id, label: IT_GROUP.label }],
    },
    {
      conceptId: "NYW6_mP6_vwf",
      label: "Hälso- och sjukvård",
      occupationGroups: [
        { conceptId: USK_HEMTJANST_GROUP.id, label: USK_HEMTJANST_GROUP.label },
        { conceptId: USK_VARD_GROUP.id, label: USK_VARD_GROUP.label },
      ],
    },
    {
      conceptId: "6Hq3_tKo_V57",
      label: "Yrken med teknisk inriktning",
      occupationGroups: [{ conceptId: EL_GROUP.id, label: EL_GROUP.label }],
    },
  ],
};

// A word with a space and letters that URL encoding changes, so every encoded form is checked. Its
// first token is ASCII, so it survives lower-casing, tokenising and percent-encoding unchanged.
const CANARY = "Kanarieyrke Ö";
const CANARY_TOKEN = "kanarieyrke";

function hero(q: string, occupationGroup: ReadonlyArray<string>) {
  return (
    <JobbHeroFilters
      taxonomy={taxonomy}
      initialOccupationGroup={occupationGroup}
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
      hasStatedDesiredOccupation={false}
      hasSeeker={false}
      q={q}
      employer={[]}
      sortBy="PublishedAtDesc"
    />
  );
}

/**
 * Renders the host. The mocked router never navigates, so `navigate` stands in for what a push does
 * in production: the page renders again with the new URL's groups, and the host is not remounted.
 */
function setup(q = "") {
  const view = render(hero(q, []));
  return { ...view, navigate: (groups: ReadonlyArray<string>) => view.rerender(hero(q, groups)) };
}

/** The href the host builds when only the occupation-group axis changes. */
function hrefWithGroups(occupationGroup: string[], q = "") {
  return buildJobbHref({
    q,
    occupationGroup,
    region: [],
    municipality: [],
    remote: false,
    employmentType: [],
    worktimeExtent: [],
    matchGrades: [],
    matchningOff: false,
    includeRelated: false,
    hideApplied: false,
    onlyMatched: false,
    employer: [],
    sortBy: "PublishedAtDesc",
  });
}

const yrkePill = () => screen.getByRole("button", { name: /^Yrke/ });
const yrkeDialog = () => screen.getByRole("dialog", { name: "Yrke" });
const field = () => screen.getByRole("searchbox", { name: "Sök yrke" });
const itRow = () => screen.findByRole("checkbox", { name: new RegExp(`^${IT_GROUP.label.replace(/[.]/g, "\\.")}`) });

async function openYrke(user: ReturnType<typeof userEvent.setup>) {
  await user.click(yrkePill());
  await waitFor(() => expect(field()).toHaveFocus());
}

beforeEach(() => {
  pushMock.mockClear();
  resolveSearchMock.mockReset();
  resolveSearchMock.mockResolvedValue({ kind: "ok", result: ruleAnswer.it });
});
afterEach(() => {
  vi.unstubAllGlobals();
});

describe("/jobb Yrke and Ort open (#2198 PR 2)", () => {
  it("Ort opens without moving focus and without a search", async () => {
    const user = userEvent.setup();
    const { container } = setup();
    const pill = screen.getByRole("button", { name: /^Ort/ });

    await user.click(pill);

    expect(screen.getByRole("dialog", { name: "Ort" })).toBeInTheDocument();
    expect(pill).toHaveFocus();
    expect(container.querySelector(".jp-popover__search")).toBeNull();
    expect(container.querySelector(".jp-popover__results")).toBeNull();
    expect(screen.queryByRole("searchbox")).toBeNull();
  });

  it("Yrke opens with focus in the search field, above the browse columns", async () => {
    const user = userEvent.setup();
    setup();

    await openYrke(user);

    const columns = within(yrkeDialog()).getByRole("group", { name: "Yrkesområde" });
    expect(field().compareDocumentPosition(columns) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});

describe("/jobb Yrke: the typed word stays in the popover (#2198 PR 2, ADR 0180 D1)", () => {
  it("a Group-row pick and a variant pick each push the href with only the group axis changed", async () => {
    const user = userEvent.setup();
    const { navigate } = setup("backend");
    await openYrke(user);

    await user.type(field(), CANARY);
    await user.click(await itRow());
    expect(pushMock).toHaveBeenCalledTimes(1);
    expect(pushMock).toHaveBeenLastCalledWith(hrefWithGroups([IT_GROUP.id], "backend"));
    navigate([IT_GROUP.id]);

    resolveSearchMock.mockResolvedValue({ kind: "ok", result: ruleAnswer.underskoterska });
    await user.clear(field());
    await user.type(field(), "undersköterska");
    await user.click(await screen.findByRole("button", { name: "Undersköterska" }));
    await user.click(screen.getByRole("checkbox", { name: "Undersköterska, hemtjänst, äldreboende och habilitering" }));
    expect(pushMock).toHaveBeenCalledTimes(2);
    expect(pushMock).toHaveBeenLastCalledWith(hrefWithGroups([IT_GROUP.id, USK_HEMTJANST_GROUP.id], "backend"));
  });

  it("no push, no other fetch, no URL, storage or cookie ever carries the word", async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({}), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    window.localStorage.clear();
    window.sessionStorage.clear();
    const searchBefore = window.location.search;
    setup("backend");

    await openYrke(user);
    await user.type(field(), CANARY);
    await user.click(await itRow());
    await user.keyboard("{Escape}");

    expect(fetchMock).toHaveBeenCalled();
    const forms = [
      CANARY,
      CANARY.toLowerCase(),
      encodeURIComponent(CANARY),
      new URLSearchParams({ q: CANARY }).toString().slice(2),
    ];
    const channels = {
      push: JSON.stringify(pushMock.mock.calls),
      fetch: JSON.stringify(fetchMock.mock.calls),
      url: window.location.search,
      cookie: document.cookie,
    };
    for (const [channel, text] of Object.entries(channels)) {
      for (const form of forms) expect(text, channel).not.toContain(form);
      expect(text.toLowerCase(), channel).not.toContain(CANARY_TOKEN);
    }
    expect(window.location.search).toBe(searchBefore);
    expect(window.localStorage).toHaveLength(0);
    expect(window.sessionStorage).toHaveLength(0);
    expect(resolveSearchMock).toHaveBeenCalledWith(CANARY, expect.any(AbortSignal));
  });

  it("typing, Enter, both Escapes, closing and reopening push nothing", async () => {
    const user = userEvent.setup();
    setup();

    await openYrke(user);
    await user.type(field(), CANARY);
    await itRow();
    await user.keyboard("{Enter}");
    await user.keyboard("{Escape}");
    await user.keyboard("{Escape}");
    await openYrke(user);

    expect(pushMock).not.toHaveBeenCalled();
  });

  it("the field has no name, no form and no autocomplete, and starts empty whatever the hero search holds", async () => {
    const user = userEvent.setup();
    setup("backend");

    await openYrke(user);

    expect(field()).not.toHaveAttribute("name");
    expect(field()).not.toHaveAttribute("form");
    expect(field().closest("form")).toBeNull();
    expect(field()).toHaveAttribute("autocomplete", "off");
    expect(field()).toHaveValue("");
    expect(resolveSearchMock).not.toHaveBeenCalled();
  });

  it("a search pick and a browse pick of the same group push the same href", async () => {
    const user = userEvent.setup();
    setup();

    await openYrke(user);
    await user.type(field(), "it");
    await user.click(await itRow());
    const fromSearch = pushMock.mock.calls.at(-1)?.[0];
    await user.keyboard("{Escape}");
    await user.keyboard("{Escape}");

    pushMock.mockClear();
    await openYrke(user);
    await user.click(within(yrkeDialog()).getByRole("button", { name: "Data/IT" }));
    await user.click(within(yrkeDialog()).getByRole("checkbox", { name: IT_GROUP.label }));

    expect(fromSearch).toBe(hrefWithGroups([IT_GROUP.id]));
    expect(pushMock).toHaveBeenLastCalledWith(fromSearch);
  });
});

describe("/jobb Yrke: Escape (#2198 PR 2, ADR 0180 D5)", () => {
  it("with text clears the field and keeps the popover and focus; a second Escape closes and focuses the pill", async () => {
    const user = userEvent.setup();
    setup();
    await openYrke(user);
    await user.type(field(), "it");
    await itRow();

    await user.keyboard("{Escape}");

    expect(field()).toHaveValue("");
    expect(field()).toHaveFocus();
    expect(yrkeDialog()).toBeInTheDocument();

    await user.keyboard("{Escape}");

    expect(screen.queryByRole("dialog", { name: "Yrke" })).toBeNull();
    expect(yrkePill()).toHaveFocus();
  });

  it("from a result row closes with one press", async () => {
    const user = userEvent.setup();
    setup();
    await openYrke(user);
    await user.type(field(), "it");
    const row = await itRow();
    row.focus();

    fireEvent.keyDown(row, { key: "Escape" });

    expect(screen.queryByRole("dialog", { name: "Yrke" })).toBeNull();
    expect(yrkePill()).toHaveFocus();
  });

  it("keeps its Escape listener while the word is typed, so the listener is in place for the Escape", async () => {
    const user = userEvent.setup();
    setup();
    await openYrke(user);
    const addListener = vi.spyOn(document, "addEventListener");

    try {
      await user.type(field(), "it");
      await itRow();

      expect(addListener.mock.calls.filter(([type]) => type === "keydown")).toHaveLength(0);
    } finally {
      addListener.mockRestore();
    }
  });
});

describe("/jobb Yrke: the word's lifetime (#2198 PR 2)", () => {
  // Each path leaves the typed word in the field, so only the remount on reopening can empty it.
  const closePaths: ReadonlyArray<[string, (user: ReturnType<typeof userEvent.setup>) => Promise<void>]> = [
    ["Escape from a result row", async () => { const row = await itRow(); row.focus(); fireEvent.keyDown(row, { key: "Escape" }); }],
    ["an outside click", async () => { fireEvent.mouseDown(document.body); }],
    ["the footer", async (user) => { await user.click(within(yrkeDialog()).getByRole("button", { name: /^Visa / })); }],
    ["the pill", async (user) => { await user.click(yrkePill()); }],
    ["another pill", async (user) => { await user.click(screen.getByRole("button", { name: /^Ort/ })); }],
  ];

  it.each(closePaths)("reopening after %s shows an empty field and the browse columns", async (_path, close) => {
    const user = userEvent.setup();
    setup();
    await openYrke(user);
    await user.type(field(), "it");
    await itRow();

    await close(user);
    expect(screen.queryByRole("dialog", { name: "Yrke" })).toBeNull();
    await openYrke(user);

    expect(field()).toHaveValue("");
    expect(within(yrkeDialog()).getByRole("group", { name: "Yrkesområde" })).toBeInTheDocument();
  });

  it("a pick keeps the popover open with the word, the answer and focus on the row", async () => {
    const user = userEvent.setup();
    const { navigate } = setup();
    await openYrke(user);
    await user.type(field(), "it");
    const row = await itRow();

    await user.click(row);
    navigate([IT_GROUP.id]);

    expect(pushMock).toHaveBeenCalledTimes(1);
    expect(yrkeDialog()).toBeInTheDocument();
    expect(field()).toHaveValue("it");
    expect(await itRow()).toHaveAttribute("aria-checked", "true");
    expect(await itRow()).toHaveFocus();
  });
});

describe("/jobb Yrke: what search mode shows (#2198 PR 2)", () => {
  it("has no Rensa and no Välj alla while results show, and gives browsing back as it was", async () => {
    const user = userEvent.setup();
    const { navigate } = setup();
    navigate([IT_GROUP.id]);
    await openYrke(user);
    await user.click(within(yrkeDialog()).getByRole("button", { name: "Data/IT" }));
    expect(within(yrkeDialog()).getByRole("checkbox", { name: "Välj alla yrkesgrupper" })).toBeInTheDocument();
    expect(within(yrkeDialog()).getAllByRole("button", { name: "Rensa" }).length).toBeGreaterThan(0);

    await user.type(field(), "it");
    await itRow();

    expect(within(yrkeDialog()).queryByRole("checkbox", { name: "Välj alla yrkesgrupper" })).toBeNull();
    expect(within(yrkeDialog()).queryByRole("button", { name: "Rensa" })).toBeNull();
    expect(within(yrkeDialog()).queryByRole("group", { name: "Yrkesområde" })).toBeNull();

    await user.keyboard("{Escape}");

    expect(within(yrkeDialog()).getByRole("button", { name: "Data/IT" })).toHaveAttribute("aria-pressed", "true");
    expect(within(yrkeDialog()).getByRole("checkbox", { name: "Välj alla yrkesgrupper" })).toBeInTheDocument();
    expect(within(yrkeDialog()).getAllByRole("button", { name: "Rensa" }).length).toBeGreaterThan(0);
  });

  it("counts Group rows from the popover's own facet counts, and never a Word row or a variant", async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const body = String(input).includes("dimension=OccupationGroup") ? { [USK_VARD_GROUP.id]: 42 } : {};
        return new Response(JSON.stringify(body), { status: 200 });
      }),
    );
    resolveSearchMock.mockResolvedValue({ kind: "ok", result: ruleAnswer.underskoterska });
    setup();
    await openYrke(user);
    await user.type(field(), "undersköterska");

    expect(await screen.findByRole("checkbox", { name: `${USK_VARD_GROUP.label} (42)` })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Undersköterska" }));
    expect(screen.getAllByText("(42)")).toHaveLength(1);
    expect(screen.getByRole("checkbox", { name: "Undersköterska, vård- och specialavdelning och mottagning" })).toBeInTheDocument();
  });
});
