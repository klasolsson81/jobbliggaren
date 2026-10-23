import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, within, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobbHeroFilters } from "./jobb-hero-filters";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import type { OccupationSearchResolver } from "@/lib/occupation-search/resolve-occupation-search";
import { buildJobbHref } from "@/lib/job-ads/search-params";
import {
  EL_GROUP,
  IT_GROUP,
  answer,
  groupRow,
  projektledareRow,
} from "@/test/fixtures/occupation-search";

const pushMock = vi.fn<(href: string) => void>();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
}));

// #2198: the search's client read, so no test reaches a network.
const { resolveSearchMock } = vi.hoisted(() => ({
  resolveSearchMock: vi.fn<OccupationSearchResolver>(),
}));
vi.mock("@/lib/occupation-search/resolve-occupation-search", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/occupation-search/resolve-occupation-search")>()),
  resolveOccupationSearch: resolveSearchMock,
}));

// Fields holding the fixtures' real groups, so a search pick and a browse pick can be compared.
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
      conceptId: "6Hq3_tKo_V57",
      label: "Tekniskt arbete",
      occupationGroups: [{ conceptId: EL_GROUP.id, label: EL_GROUP.label }],
    },
  ],
};

// A word with a space and letters that URL encoding changes, so every encoded form is checked.
const CANARY = "Kanarieyrke Ö";

const backendAnswer = answer([groupRow(IT_GROUP, ["Backend-utvecklare"])]);

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

async function openYrke(user: ReturnType<typeof userEvent.setup>) {
  await user.click(yrkePill());
  await waitFor(() => expect(field()).toHaveFocus());
}

beforeEach(() => {
  pushMock.mockClear();
  resolveSearchMock.mockReset();
  resolveSearchMock.mockResolvedValue({ kind: "ok", result: backendAnswer });
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

    expect(within(yrkeDialog()).getByRole("group", { name: "Yrkesområde" })).toBeInTheDocument();
  });
});

describe("/jobb Yrke: the typed word stays in the popover (#2198 PR 2, ADR 0180 D1)", () => {
  it("a Group-row pick and a variant pick each push the href with only the group axis changed", async () => {
    const user = userEvent.setup();
    const { navigate } = setup("backend");
    await openYrke(user);

    await user.type(field(), CANARY);
    await user.click(await screen.findByRole("checkbox", { name: IT_GROUP.label }));
    expect(pushMock).toHaveBeenLastCalledWith(hrefWithGroups([IT_GROUP.id], "backend"));
    navigate([IT_GROUP.id]);

    resolveSearchMock.mockResolvedValue({ kind: "ok", result: answer([projektledareRow, groupRow(IT_GROUP, ["Projektledare, IT"])]) });
    await user.clear(field());
    await user.type(field(), "projektledare");
    await user.click(await screen.findByRole("button", { name: "Projektledare" }));
    await user.click(screen.getByRole("checkbox", { name: "Projektledare, el" }));
    expect(pushMock).toHaveBeenLastCalledWith(hrefWithGroups([IT_GROUP.id, EL_GROUP.id], "backend"));
  });

  it("no push, no fetch, no URL, storage or cookie ever carries the word", async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({}), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    window.localStorage.clear();
    window.sessionStorage.clear();
    const searchBefore = window.location.search;
    setup("backend");

    await openYrke(user);
    await user.type(field(), CANARY);
    await user.click(await screen.findByRole("checkbox", { name: /^Mjukvaru- och systemutvecklare m\.fl\./ }));
    await user.keyboard("{Escape}");

    const forms = [
      CANARY,
      CANARY.toLowerCase(),
      encodeURIComponent(CANARY),
      new URLSearchParams({ q: CANARY }).toString().slice(2),
    ];
    const pushed = JSON.stringify(pushMock.mock.calls);
    const fetched = JSON.stringify(fetchMock.mock.calls);
    for (const form of forms) {
      expect(pushed).not.toContain(form);
      expect(fetched).not.toContain(form);
      expect(window.location.search).not.toContain(form);
      expect(document.cookie).not.toContain(form);
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
    await screen.findByRole("checkbox", { name: IT_GROUP.label });
    await user.keyboard("{Enter}");
    await user.keyboard("{Escape}");
    await user.keyboard("{Escape}");
    await openYrke(user);

    expect(pushMock).not.toHaveBeenCalled();
  });

  it("the field has no name and no autocomplete, and starts empty whatever the hero search holds", async () => {
    const user = userEvent.setup();
    setup("backend");

    await openYrke(user);

    expect(field()).not.toHaveAttribute("name");
    expect(field()).toHaveAttribute("autocomplete", "off");
    expect(field()).toHaveValue("");
    expect(resolveSearchMock).not.toHaveBeenCalled();
  });

  it("a search pick and a browse pick of the same group push the same href", async () => {
    const user = userEvent.setup();
    setup();

    await openYrke(user);
    await user.type(field(), "backend");
    await user.click(await screen.findByRole("checkbox", { name: IT_GROUP.label }));
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
    await user.type(field(), "backend");
    await screen.findByRole("checkbox", { name: IT_GROUP.label });

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
    await user.type(field(), "backend");
    const row = await screen.findByRole("checkbox", { name: IT_GROUP.label });
    row.focus();

    fireEvent.keyDown(row, { key: "Escape" });

    expect(screen.queryByRole("dialog", { name: "Yrke" })).toBeNull();
    expect(yrkePill()).toHaveFocus();
  });
});

describe("/jobb Yrke: the word's lifetime (#2198 PR 2)", () => {
  const closePaths: ReadonlyArray<[string, (user: ReturnType<typeof userEvent.setup>) => Promise<void>]> = [
    ["Escape", async (user) => { await user.keyboard("{Escape}"); await user.keyboard("{Escape}"); }],
    ["an outside click", async () => { fireEvent.mouseDown(document.body); }],
    ["the footer", async (user) => { await user.click(within(yrkeDialog()).getByRole("button", { name: /^Visa / })); }],
    ["the pill", async (user) => { await user.click(yrkePill()); }],
    ["another pill", async (user) => { await user.click(screen.getByRole("button", { name: /^Ort/ })); }],
  ];

  it.each(closePaths)("reopening after %s shows an empty field and the browse columns", async (_path, close) => {
    const user = userEvent.setup();
    setup();
    await openYrke(user);
    await user.type(field(), "backend");
    await screen.findByRole("checkbox", { name: IT_GROUP.label });

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
    await user.type(field(), "backend");
    const row = await screen.findByRole("checkbox", { name: IT_GROUP.label });

    await user.click(row);
    navigate([IT_GROUP.id]);

    expect(pushMock).toHaveBeenCalledTimes(1);
    expect(yrkeDialog()).toBeInTheDocument();
    expect(field()).toHaveValue("backend");
    expect(screen.getByRole("checkbox", { name: IT_GROUP.label })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("checkbox", { name: IT_GROUP.label })).toHaveFocus();
  });
});

describe("/jobb Yrke: what search mode shows (#2198 PR 2)", () => {
  it("has no Rensa and no Välj alla while results show, and gives browsing back as it was", async () => {
    const user = userEvent.setup();
    setup();
    await openYrke(user);
    await user.click(within(yrkeDialog()).getByRole("button", { name: "Data/IT" }));
    expect(within(yrkeDialog()).getByRole("checkbox", { name: "Välj alla yrkesgrupper" })).toBeInTheDocument();

    await user.type(field(), "backend");
    await screen.findByRole("checkbox", { name: IT_GROUP.label });

    expect(within(yrkeDialog()).queryByRole("checkbox", { name: "Välj alla yrkesgrupper" })).toBeNull();
    expect(within(yrkeDialog()).queryByRole("button", { name: "Rensa" })).toBeNull();
    expect(within(yrkeDialog()).queryByRole("group", { name: "Yrkesområde" })).toBeNull();

    await user.keyboard("{Escape}");

    expect(within(yrkeDialog()).getByRole("button", { name: "Data/IT" })).toHaveAttribute("aria-pressed", "true");
    expect(within(yrkeDialog()).getByRole("checkbox", { name: "Välj alla yrkesgrupper" })).toBeInTheDocument();
  });

  it("counts Group rows from the popover's own facet counts, and nothing else", async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const body = String(input).includes("dimension=OccupationGroup") ? { [IT_GROUP.id]: 42 } : {};
        return new Response(JSON.stringify(body), { status: 200 });
      }),
    );
    resolveSearchMock.mockResolvedValue({ kind: "ok", result: answer([projektledareRow, groupRow(IT_GROUP, ["Projektledare, IT"])]) });
    setup();
    await openYrke(user);
    await user.type(field(), "projektledare");

    expect(await screen.findByRole("checkbox", { name: `${IT_GROUP.label} (42)` })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Projektledare" }));
    expect(screen.getAllByText("(42)")).toHaveLength(1);
    expect(screen.getByRole("checkbox", { name: "Projektledare, IT" })).toBeInTheDocument();
  });
});
