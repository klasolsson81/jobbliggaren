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

  // issue #292 — matchningsgrad-/huvudbrytar-kontrollen i toolbaren.
  describe("matchningsgrad-filter + huvudbrytare (issue #292)", () => {
    it("DÖLJER kontrollen helt när hasStatedDesiredOccupation är false", () => {
      renderToolbar({
        hasStatedDesiredOccupation: false,
        matchActive: false,
      });
      // Switchen finns inte (graden kan inte beräknas utan angivet yrke).
      expect(screen.queryByRole("switch", { name: "Matchning" })).toBeNull();
    });

    it("VISAR kontrollen när hasStatedDesiredOccupation är true (även om matchningen är AV)", () => {
      // Gate (c): kontrollen renderas så switchen kan slå PÅ matchningen igen.
      renderToolbar({
        hasStatedDesiredOccupation: true,
        matchActive: false,
      });
      expect(
        screen.getByRole("switch", { name: "Matchning" }),
      ).toBeInTheDocument();
    });

    it("switchen speglar matchActive (PÅ), INTE selected.length (matchGrades tomt)", () => {
      // Default-läget: yrke angett, matchningen PÅ, inga grader smalnade.
      renderToolbar({ matchActive: true, matchGrades: [] });
      expect(
        screen.getByRole("switch", { name: "Matchning" }),
      ).toHaveAttribute("aria-checked", "true");
    });

    it("switchen speglar matchActive (AV) ÄVEN när matchGrades har värden i URL:en", () => {
      // matchActive=false men en stale matchGrades-URL → switchen ska visa AV
      // (speglar axeln, inte grad-listan). Bevisar att isOn=active, ej length.
      renderToolbar({ matchActive: false, matchGrades: ["Strong"] });
      expect(
        screen.getByRole("switch", { name: "Matchning" }),
      ).toHaveAttribute("aria-checked", "false");
    });

    it("slå AV switchen (PÅ → AV) → matchning=off + tömmer grader, UTAN commit-flaggan", async () => {
      const user = userEvent.setup();
      renderToolbar({ matchActive: true, matchGrades: [] });
      await user.click(screen.getByRole("switch", { name: "Matchning" }));
      // matchGrades är runtime-view-state → ingen ?commit=true.
      expect(pushMock).toHaveBeenCalledWith("/jobb?matchning=off");
    });

    it("slå PÅ switchen (AV → PÅ) → ren /jobb (tar bort off, lämnar grader tomma = alla visas)", async () => {
      const user = userEvent.setup();
      renderToolbar({ matchActive: false, matchGrades: [] });
      await user.click(screen.getByRole("switch", { name: "Matchning" }));
      // Toggle PÅ emitterar ren /jobb (issue #292 — ej längre ?matchGrades=...).
      expect(pushMock).toHaveBeenCalledWith("/jobb");
    });

    it("avmarkera SISTA graden navigerar till tom lista (= alla visas), switchen förblir PÅ", async () => {
      const user = userEvent.setup();
      renderToolbar({ matchActive: true, matchGrades: ["Strong"] });
      await user.click(screen.getByRole("checkbox", { name: "Stark match" }));
      // Tom matchGrades + matchningOff=false → buildJobbHref utelämnar bägge
      // paramen → ren /jobb. (issue #292: empty = alla visas, fortfarande PÅ.)
      expect(pushMock).toHaveBeenCalledWith("/jobb");
    });

    it("hjälpraden visas när matchningen är PÅ (matchActive), inte i av-läget", () => {
      const HELP = /Filtrerar listan efter din matchningsnivå/;
      const { rerender } = renderToolbar({
        matchActive: false,
        hasStatedDesiredOccupation: true,
      });
      expect(screen.queryByText(HELP)).toBeNull();

      rerender(
        <JobbResultsToolbar
          totalCount={5}
          occupationGroup={[]}
          region={[]}
          municipality={[]}
          employmentType={[]}
          worktimeExtent={[]}
          matchGrades={[]}
          resolvedLabels={{}}
          q=""
          sortBy="PublishedAtDesc"
          hasStatedDesiredOccupation
          matchActive
        />,
      );
      expect(screen.getByText(HELP)).toBeInTheDocument();
    });
  });
});
