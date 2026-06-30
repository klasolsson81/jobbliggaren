import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobbResultsToolbar } from "./jobb-results-toolbar";

const pushMock = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
}));

const resolvedLabels: Record<string, string> = {
  CifL_Rzy_Mku: "Stockholms län",
  MVqp_eS8_kDZ: "Systemutvecklare",
  zHxw_uJZ_NNh: "Solna",
};

// Gemensam bas så varje test slipper räkna upp alla props (matchActive lades i
// issue #292 — defaultar till true här eftersom de flesta sort/chip-testerna
// kör i det förvalda PÅ-läget med angivet yrke).
type ToolbarOverrides = Partial<
  React.ComponentProps<typeof JobbResultsToolbar>
>;

function renderToolbar(over: ToolbarOverrides = {}) {
  return render(
    <JobbResultsToolbar
      totalCount={5}
      occupationGroup={[]}
      region={[]}
      municipality={[]}
      employmentType={[]}
      worktimeExtent={[]}
      matchGrades={[]}
      includeRelated={false}
      savedOnly={false}
      appliedOnly={false}
      hideApplied={false}
      hasSeeker={false}
      resolvedLabels={{}}
      q=""
      sortBy="PublishedAtDesc"
      hasStatedDesiredOccupation
      matchActive
      {...over}
    />,
  );
}

beforeEach(() => {
  pushMock.mockClear();
});

// #408 — matchnings-/status-kontrollerna bor nu bakom toolbar-pillar (popovers).
// Hjälpare som öppnar respektive popover så de inre kontrollerna monteras i DOM.
async function openMatchPopover(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: /^Matchning/ }));
}
async function openStatusPopover(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: /^Status/ }));
}

describe("JobbResultsToolbar — träffar + chips + sort", () => {
  it("visar mono-formaterat antal träffar", () => {
    const { container } = renderToolbar({ totalCount: 1234 });
    // issue #292 — i PÅ-läget (matchActive default true) finns nu TVÅ role=status
    // (träffräknaren + grad-filtrets hjälprad), så bare getByRole("status") är
    // tvetydigt. Träffräknaren är den distinkta `.jp-results-count`-noden.
    // sv-SE grupperar med NBSP.
    const count = container.querySelector(".jp-results-count");
    expect(count).not.toBeNull();
    expect(count).toHaveTextContent(/1\s234 träffar/);
  });

  it("renderar aktiva chips via resolverad label och tar bort vid ×", async () => {
    const user = userEvent.setup();
    renderToolbar({
      totalCount: 3,
      occupationGroup: ["MVqp_eS8_kDZ"],
      region: ["CifL_Rzy_Mku"],
      resolvedLabels,
      q: "backend",
    });
    expect(screen.getByText("Stockholms län")).toBeInTheDocument();
    expect(screen.getByText("Systemutvecklare")).toBeInTheDocument();

    await user.click(
      screen.getByRole("button", { name: "Ta bort filter Stockholms län" }),
    );
    // region bort, occupationGroup + q bevarade. E2j: toolbar-handling =
    // avsiktlig sökning → commit-intent (?commit=true, Klas-val 2026-06-12).
    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?occupationGroup=MVqp_eS8_kDZ&q=backend&commit=true",
    );
  });

  it("kommun-chip renderas och × tar bort rätt axel (E2b)", async () => {
    const user = userEvent.setup();
    renderToolbar({
      totalCount: 2,
      region: ["CifL_Rzy_Mku"],
      municipality: ["zHxw_uJZ_NNh"],
      resolvedLabels,
    });
    expect(screen.getByText("Solna")).toBeInTheDocument();

    await user.click(
      screen.getByRole("button", { name: "Ta bort filter Solna" }),
    );
    // municipality bort, region bevarad.
    expect(pushMock).toHaveBeenCalledWith("/jobb?region=CifL_Rzy_Mku&commit=true");
  });

  it("fallback-label för okänd conceptId", () => {
    renderToolbar({ totalCount: 1, region: ["XX_unknown"] });
    expect(screen.getByText("Okänd kod (XX_unknown)")).toBeInTheDocument();
  });

  it("q-orden visas som taggar med Search-semantik och × tar bort ordet (E2i)", async () => {
    const user = userEvent.setup();
    renderToolbar({ totalCount: 3, q: "volvo lastbil" });
    expect(screen.getByText("volvo")).toBeInTheDocument();
    expect(screen.getByText("lastbil")).toBeInTheDocument();

    await user.click(
      screen.getByRole("button", { name: "Ta bort sökordet volvo" }),
    );
    expect(pushMock).toHaveBeenCalledWith("/jobb?q=lastbil&commit=true");
  });

  it("Rensa sökord och filter nollar ALLT inkl. q (E2i Klas-beslut — ersätter E2e-domen)", async () => {
    const user = userEvent.setup();
    renderToolbar({
      totalCount: 3,
      occupationGroup: ["MVqp_eS8_kDZ"],
      region: ["CifL_Rzy_Mku"],
      municipality: ["zHxw_uJZ_NNh"],
      resolvedLabels,
      q: "backend",
    });
    await user.click(
      screen.getByRole("button", { name: "Rensa sökord och filter" }),
    );
    expect(pushMock).toHaveBeenCalledWith("/jobb?commit=true");
  });

  it("Rensa-länken bevarar icke-default sortBy (E2e, code-reviewer Minor 1)", async () => {
    const user = userEvent.setup();
    renderToolbar({
      totalCount: 3,
      occupationGroup: ["MVqp_eS8_kDZ"],
      resolvedLabels,
      q: "backend",
      sortBy: "ExpiresAtAsc",
    });
    await user.click(
      screen.getByRole("button", { name: "Rensa sökord och filter" }),
    );
    expect(pushMock).toHaveBeenCalledWith("/jobb?sortBy=ExpiresAtAsc&commit=true");
  });

  it("Rensa-länken visas inte utan aktiva chips (E2e)", () => {
    renderToolbar({ totalCount: 5 });
    expect(
      screen.queryByRole("button", { name: "Rensa sökord och filter" }),
    ).toBeNull();
  });

  it("sort-alternativen bär labels (Relevans / Sortera efter matchning / Datum (nyast) / Ansökningsdatum (sista ansökan))", () => {
    renderToolbar({ q: "ab" });
    expect(screen.getByRole("option", { name: "Relevans" })).toBeInTheDocument();
    expect(
      screen.getByRole("option", { name: "Sortera efter matchning" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("option", { name: "Datum (nyast)" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("option", { name: "Ansökningsdatum (sista ansökan)" }),
    ).toBeInTheDocument();
  });

  it("match-sort-alternativet visas och är ALDRIG disablat utan söktext (F4-14, till skillnad från Relevance)", () => {
    renderToolbar();
    const opt = screen.getByRole("option", {
      name: "Sortera efter matchning",
    }) as HTMLOptionElement;
    expect(opt.disabled).toBe(false);
  });

  it("match-sort-byte commit:ar sortBy=MatchDesc och bevarar q + filter (F4-14)", async () => {
    const user = userEvent.setup();
    renderToolbar({
      occupationGroup: ["MVqp_eS8_kDZ"],
      resolvedLabels,
      q: "data",
    });
    await user.selectOptions(
      screen.getByLabelText("Sortera"),
      "Sortera efter matchning",
    );
    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?occupationGroup=MVqp_eS8_kDZ&q=data&sortBy=MatchDesc&commit=true",
    );
  });

  it("Relevance-alternativet är disablat utan söktext (ADR 0042 Beslut D)", () => {
    renderToolbar();
    const opt = screen.getByRole("option", {
      name: "Relevans",
    }) as HTMLOptionElement;
    expect(opt.disabled).toBe(true);
  });

  it("Relevance-alternativet är aktivt med q ≥ 2 tecken", () => {
    renderToolbar({ q: "ab" });
    const opt = screen.getByRole("option", {
      name: "Relevans",
    }) as HTMLOptionElement;
    expect(opt.disabled).toBe(false);
  });

  it("sort-byte commit:ar sortBy och bevarar q + filter", async () => {
    const user = userEvent.setup();
    renderToolbar({
      occupationGroup: ["MVqp_eS8_kDZ"],
      resolvedLabels,
      q: "data",
    });
    await user.selectOptions(screen.getByLabelText("Sortera"), "Relevans");
    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?occupationGroup=MVqp_eS8_kDZ&q=data&sortBy=Relevance&commit=true",
    );
  });

  // issue #292 — Gate (b): match-sort-alternativet + select-koercionen styrs av
  // matchActive (matchnings-axelns huvudbrytare), inte av yrket ensamt.
  describe("match-sort-gate (issue #292)", () => {
    it("DÖLJER MatchDesc-alternativet när matchActive=false", () => {
      renderToolbar({ matchActive: false, hasStatedDesiredOccupation: false });
      expect(
        screen.queryByRole("option", { name: "Sortera efter matchning" }),
      ).toBeNull();
    });

    it("DÖLJER MatchDesc även med angivet yrke om matchningen är AV (matchActive=false)", () => {
      // Yrke angett men huvudbrytaren av → matchActive=false → ingen match-sort.
      renderToolbar({ matchActive: false, hasStatedDesiredOccupation: true });
      expect(
        screen.queryByRole("option", { name: "Sortera efter matchning" }),
      ).toBeNull();
    });

    it("coercar en aktiv MatchDesc-URL till PublishedAtDesc i select-värdet när matchActive=false", () => {
      renderToolbar({
        matchActive: false,
        hasStatedDesiredOccupation: false,
        sortBy: "MatchDesc",
      });
      // Select-värdet faller till default (Datum (nyast)) — paritet med
      // jobb-results.tsx effectiveSortBy-koercionen (URL ≠ faktisk ordning aldrig).
      const select = screen.getByLabelText("Sortera") as HTMLSelectElement;
      expect(select.value).toBe("PublishedAtDesc");
    });

    it("ERBJUDER MatchDesc när matchActive=true", () => {
      renderToolbar({ matchActive: true });
      expect(
        screen.getByRole("option", { name: "Sortera efter matchning" }),
      ).toBeInTheDocument();
    });
  });

  // issue #292 — matchningsgrad-/huvudbrytar-kontrollen i toolbaren. #408 — den
  // bor nu bakom [Matchning ▾]-pillen (popover). Switch/kryssrutor monteras först
  // när pillen öppnas; URL-semantiken är OFÖRÄNDRAD.
  describe("matchningsgrad-filter + huvudbrytare (issue #292, #408 popover)", () => {
    it("DÖLJER pillen (och kontrollen) helt när hasStatedDesiredOccupation är false", () => {
      renderToolbar({
        hasStatedDesiredOccupation: false,
        matchActive: false,
      });
      // Pillen finns inte (graden kan inte beräknas utan angivet yrke).
      expect(
        screen.queryByRole("button", { name: /^Matchning/ }),
      ).toBeNull();
      // Och därmed ingen switch.
      expect(screen.queryByRole("switch", { name: "Matchning" })).toBeNull();
    });

    it("VISAR pillen när hasStatedDesiredOccupation är true (även om matchningen är AV)", () => {
      // Gate (c): pillen renderas så switchen kan slå PÅ matchningen igen.
      renderToolbar({
        hasStatedDesiredOccupation: true,
        matchActive: false,
      });
      expect(
        screen.getByRole("button", { name: /^Matchning/ }),
      ).toBeInTheDocument();
    });

    it("pillen är data-active=true när matchActive (PÅ)", () => {
      renderToolbar({ matchActive: true, matchGrades: [] });
      expect(
        screen.getByRole("button", { name: /^Matchning/ }),
      ).toHaveAttribute("data-active", "true");
    });

    it("pillen är data-active=false när matchActive=false (även med stale matchGrades i URL:en)", () => {
      renderToolbar({ matchActive: false, matchGrades: ["Strong"] });
      expect(
        screen.getByRole("button", { name: /^Matchning/ }),
      ).toHaveAttribute("data-active", "false");
    });

    it("count-badge speglar antal smalnade grad-val (2 valda → badge 2)", () => {
      renderToolbar({ matchActive: true, matchGrades: ["Good", "Strong"] });
      const pill = screen.getByRole("button", { name: /^Matchning/ });
      expect(pill.querySelector(".jp-hero-pill__count")).toHaveTextContent("2");
    });

    it("ingen count-badge när alla grader visas (tom matchGrades)", () => {
      renderToolbar({ matchActive: true, matchGrades: [] });
      const pill = screen.getByRole("button", { name: /^Matchning/ });
      expect(pill.querySelector(".jp-hero-pill__count")).toBeNull();
    });

    it("pillen öppnar popovern (role=dialog Matchning) och stänger den vid återklick", async () => {
      const user = userEvent.setup();
      renderToolbar({ matchActive: true });
      expect(screen.queryByRole("dialog", { name: "Matchning" })).toBeNull();
      await openMatchPopover(user);
      expect(
        screen.getByRole("dialog", { name: "Matchning" }),
      ).toBeInTheDocument();
      await openMatchPopover(user);
      expect(screen.queryByRole("dialog", { name: "Matchning" })).toBeNull();
    });

    it("ESC stänger Matchnings-popovern (a11y kriterium 7)", async () => {
      const user = userEvent.setup();
      renderToolbar({ matchActive: true });
      await openMatchPopover(user);
      expect(
        screen.getByRole("dialog", { name: "Matchning" }),
      ).toBeInTheDocument();
      await user.keyboard("{Escape}");
      expect(screen.queryByRole("dialog", { name: "Matchning" })).toBeNull();
    });

    it("slå AV switchen (PÅ → AV) → matchning=off + tömmer grader, UTAN commit-flaggan", async () => {
      const user = userEvent.setup();
      renderToolbar({ matchActive: true, matchGrades: [] });
      await openMatchPopover(user);
      await user.click(screen.getByRole("switch", { name: "Matchning" }));
      // matchGrades är runtime-view-state → ingen ?commit=true.
      expect(pushMock).toHaveBeenCalledWith("/jobb?matchning=off");
    });

    it("slå PÅ switchen (AV → PÅ) → ren /jobb (tar bort off, lämnar grader tomma = alla visas)", async () => {
      const user = userEvent.setup();
      renderToolbar({ matchActive: false, matchGrades: [] });
      await openMatchPopover(user);
      await user.click(screen.getByRole("switch", { name: "Matchning" }));
      // Toggle PÅ emitterar ren /jobb (issue #292 — ej längre ?matchGrades=...).
      expect(pushMock).toHaveBeenCalledWith("/jobb");
    });

    it("avmarkera SISTA graden navigerar till tom lista (= alla visas), switchen förblir PÅ", async () => {
      const user = userEvent.setup();
      renderToolbar({ matchActive: true, matchGrades: ["Strong"] });
      await openMatchPopover(user);
      await user.click(screen.getByRole("checkbox", { name: "Stark match" }));
      // Tom matchGrades + matchningOff=false → buildJobbHref utelämnar bägge
      // paramen → ren /jobb. (issue #292: empty = alla visas, fortfarande PÅ.)
      expect(pushMock).toHaveBeenCalledWith("/jobb");
    });

    it("#300 PR-5 — related-toggle PÅ navigerar med ?relaterade=on (utan commit-flaggan)", async () => {
      const user = userEvent.setup();
      renderToolbar({ matchActive: true, includeRelated: false });
      await openMatchPopover(user);
      await user.click(
        screen.getByRole("switch", { name: "Visa relaterade också" }),
      );
      // Runtime-view-state → ingen ?commit=true.
      expect(pushMock).toHaveBeenCalledWith("/jobb?relaterade=on");
    });

    it("#300 PR-5 — related-toggle AV droppar Related ur matchGrades (state/URL-divergens-skydd)", async () => {
      const user = userEvent.setup();
      // Toggle på + Related smalnat i grad-listan → slå AV → Related droppas,
      // ?relaterade=on försvinner, men de övriga grad-valen behålls.
      renderToolbar({
        matchActive: true,
        includeRelated: true,
        matchGrades: ["Basic", "Related", "Good"],
      });
      await openMatchPopover(user);
      await user.click(
        screen.getByRole("switch", { name: "Visa relaterade också" }),
      );
      expect(pushMock).toHaveBeenCalledWith(
        "/jobb?matchGrades=Basic&matchGrades=Good",
      );
    });

    it("#300 PR-5 — Matchning AV nollar även includeRelated (forget-semantik)", async () => {
      const user = userEvent.setup();
      renderToolbar({
        matchActive: true,
        includeRelated: true,
        matchGrades: ["Related"],
      });
      await openMatchPopover(user);
      await user.click(screen.getByRole("switch", { name: "Matchning" }));
      // matchning=off, matchGrades tömt, relaterade borttaget → ren off-URL.
      expect(pushMock).toHaveBeenCalledWith("/jobb?matchning=off");
    });

    it("ingen inline hjälprad längre — hjälpen bor i pillens '?' InfoDialog (#408 kriterium 1)", () => {
      const HELP = /Filtrerar listan efter hur väl annonserna passar/;
      renderToolbar({ matchActive: true, hasStatedDesiredOccupation: true });
      // Texten finns INTE inline (den öppnas via dialogen, inte renderad i toolbaren).
      expect(screen.queryByText(HELP)).toBeNull();
    });

    it("'?' InfoDialog visar BÅDA verbatim-styckena (help + relatedToggleHelp, #408 kriterium 9)", async () => {
      const user = userEvent.setup();
      renderToolbar({ matchActive: true, hasStatedDesiredOccupation: true });
      await user.click(screen.getByRole("button", { name: "Vad är detta?" }));
      expect(
        await screen.findByText(/Filtrerar listan efter hur väl annonserna passar/),
      ).toBeInTheDocument();
      expect(
        screen.getByText(/Tar med yrken som liknar dina valda/),
      ).toBeInTheDocument();
    });
  });

  // #408 — toolbar-lokala grad-chips (ROW 2). En per smalnad grad; × kör samma
  // navigate-väg (onMatchGradesChange med graden borttagen). Visas ALDRIG när
  // alla visas ([]) eller matchningen av.
  describe("grad-chips (#408)", () => {
    it("renderar en chip per smalnad grad när matchActive", () => {
      renderToolbar({ matchActive: true, matchGrades: ["Good", "Strong"] });
      expect(screen.getByText("Bra match")).toBeInTheDocument();
      expect(screen.getByText("Stark match")).toBeInTheDocument();
    });

    it("INGA grad-chips när alla grader visas (tom matchGrades)", () => {
      renderToolbar({ matchActive: true, matchGrades: [] });
      expect(screen.queryByText("Bra match")).toBeNull();
      expect(screen.queryByText("Stark match")).toBeNull();
    });

    it("INGA grad-chips när matchningen är av (matchActive=false) trots matchGrades i URL:en", () => {
      renderToolbar({ matchActive: false, matchGrades: ["Strong"] });
      expect(screen.queryByText("Stark match")).toBeNull();
    });

    it("× på en grad-chip kör onMatchGradesChange med graden borttagen (navigate, utan commit)", async () => {
      const user = userEvent.setup();
      renderToolbar({ matchActive: true, matchGrades: ["Good", "Strong"] });
      await user.click(
        screen.getByRole("button", { name: "Ta bort filter Bra match" }),
      );
      // Good borttagen, Strong kvar → ?matchGrades=Strong (ingen ?commit=true).
      expect(pushMock).toHaveBeenCalledWith("/jobb?matchGrades=Strong");
    });
  });

  // #383 / #408 — status-facetterna bakom [Status ▾]-pillen. ORTOGONALA mot
  // matchningen: pillen renderas på hasSeeker, INTE på angivet yrke. Navigerar
  // utan commit-flaggan (runtime-view-state).
  describe("status-facetterna (#383, #408 popover)", () => {
    it("DÖLJER status-pillen när hasSeeker=false", () => {
      renderToolbar({ hasSeeker: false });
      expect(screen.queryByRole("button", { name: /^Status/ })).toBeNull();
      expect(screen.queryByRole("group", { name: "Status" })).toBeNull();
    });

    it("VISAR status-pillen när hasSeeker=true (oavsett yrke/matchning)", () => {
      renderToolbar({
        hasSeeker: true,
        hasStatedDesiredOccupation: false,
        matchActive: false,
      });
      expect(
        screen.getByRole("button", { name: /^Status/ }),
      ).toBeInTheDocument();
    });

    it("status-pillen öppnar popovern (role=dialog Status) med kryssrute-gruppen", async () => {
      const user = userEvent.setup();
      renderToolbar({ hasSeeker: true });
      await openStatusPopover(user);
      expect(
        screen.getByRole("dialog", { name: "Status" }),
      ).toBeInTheDocument();
      expect(
        screen.getByRole("group", { name: "Status" }),
      ).toBeInTheDocument();
      expect(
        screen.getByRole("checkbox", { name: "Sparade" }),
      ).toBeInTheDocument();
    });

    it("utanför-klick stänger Status-popovern (a11y kriterium 7)", async () => {
      const user = userEvent.setup();
      renderToolbar({ hasSeeker: true });
      await openStatusPopover(user);
      expect(screen.getByRole("dialog", { name: "Status" })).toBeInTheDocument();
      // Klick på träffräknaren (utanför panelen + triggern) stänger.
      await user.click(screen.getByText(/träffar/));
      expect(screen.queryByRole("dialog", { name: "Status" })).toBeNull();
    });

    it("klick på Sparade navigerar med ?sparade=on (utan commit-flaggan)", async () => {
      const user = userEvent.setup();
      renderToolbar({ hasSeeker: true });
      await openStatusPopover(user);
      await user.click(screen.getByRole("checkbox", { name: "Sparade" }));
      expect(pushMock).toHaveBeenCalledTimes(1);
      expect(pushMock).toHaveBeenCalledWith(expect.stringContaining("sparade=on"));
      // Runtime-view-state — ingen recent-search-capture.
      expect(pushMock).not.toHaveBeenCalledWith(
        expect.stringContaining("commit=true"),
      );
    });

    it("MUTEX via popovern: Dölj ansökta när Ansökta var på → ?doljAnsokta=on utan ?ansokta", async () => {
      const user = userEvent.setup();
      renderToolbar({ hasSeeker: true, appliedOnly: true });
      await openStatusPopover(user);
      await user.click(screen.getByRole("checkbox", { name: "Dölj ansökta" }));
      expect(pushMock).toHaveBeenCalledWith(
        expect.stringContaining("doljAnsokta=on"),
      );
      // appliedOnly (?ansokta=on, gemener) slogs av av mutex:en — bara
      // doljAnsokta=on (versal A i camelCase) finns kvar.
      expect(pushMock).not.toHaveBeenCalledWith(
        expect.stringContaining("ansokta=on"),
      );
    });

    it("count-badge speglar antal aktiva status-facetter (Sparade + Dölj ansökta → 2)", () => {
      renderToolbar({ hasSeeker: true, savedOnly: true, hideApplied: true });
      const pill = screen.getByRole("button", { name: /^Status/ });
      expect(pill.querySelector(".jp-hero-pill__count")).toHaveTextContent("2");
    });
  });

  // #408 — toolbar-lokala status-chips (ROW 2). En per aktiv facett; × kör
  // onStatusChange (mutex bevaras i state-objektet).
  describe("status-chips (#408)", () => {
    it("renderar en chip per aktiv facett", () => {
      renderToolbar({ hasSeeker: true, savedOnly: true, hideApplied: true });
      expect(screen.getByText("Sparade")).toBeInTheDocument();
      expect(screen.getByText("Dölj ansökta")).toBeInTheDocument();
    });

    it("× på en status-chip kör onStatusChange med facetten avstängd (navigate, utan commit)", async () => {
      const user = userEvent.setup();
      renderToolbar({ hasSeeker: true, savedOnly: true });
      await user.click(
        screen.getByRole("button", { name: "Ta bort filter Sparade" }),
      );
      // savedOnly avstängd → ren /jobb (ingen ?commit=true).
      expect(pushMock).toHaveBeenCalledWith("/jobb");
    });

    it("INGA status-chips utan aktiva facetter", () => {
      renderToolbar({ hasSeeker: true });
      expect(screen.queryByText("Sparade")).toBeNull();
    });
  });

  // #408 — grad/status läggs ALDRIG i den delade buildChipModels (SPOT med hero-
  // fältets in-field-chips). Verifiera att en hero-fältmiljö (gemensam helper)
  // aldrig skulle se grad/status — här via att grad-chips bara dyker upp i
  // toolbaren och inte påverkar q/dimension-chip-ordningen.
  describe("grad/status SPOT-isolering (#408)", () => {
    it("grad-chip + q-chip samexisterar utan att grad hamnar i sök/q-chip-listan", () => {
      renderToolbar({
        matchActive: true,
        matchGrades: ["Strong"],
        q: "data",
      });
      // q-chipen finns med Search-semantik (sök-borttagning), grad-chipen med
      // filter-borttagning — skilda aria-labels bevisar att de inte slogs ihop.
      expect(
        screen.getByRole("button", { name: "Ta bort sökordet data" }),
      ).toBeInTheDocument();
      expect(
        screen.getByRole("button", { name: "Ta bort filter Stark match" }),
      ).toBeInTheDocument();
    });
  });
});
