import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { render as rawRender } from "@testing-library/react/pure";
import { NextIntlClientProvider } from "next-intl";
import enMessages from "../../../messages/en";
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
  // Klass 2 — servern resolvar dem fortfarande via /taxonomy/labels, men chipet
  // namnger dem ur katalogen på conceptId (#1537).
  gro4_cWF_6D7: "Vikariat",
};

// Gemensam bas så varje test slipper räkna upp alla props (matchActive lades i
// issue #292 — defaultar till true här eftersom de flesta sort/chip-testerna
// kör i det förvalda PÅ-läget med angivet yrke).
type ToolbarOverrides = Partial<
  React.ComponentProps<typeof JobbResultsToolbar>
>;

function toolbar(over: ToolbarOverrides = {}) {
  return (
    <JobbResultsToolbar
      totalCount={5}
      occupationGroup={[]}
      region={[]}
      remote={false}
      municipality={[]}
      employmentType={[]}
      worktimeExtent={[]}
      matchGrades={[]}
      includeRelated={false}
      matchningOff={false}
      hideApplied={false}
      onlyMatched={false}
      employer={[]}
      resolvedLabels={{}}
      q=""
      sortBy="PublishedAtDesc"
      hasStatedDesiredOccupation
      matchActive
      {...over}
    />
  );
}

function renderToolbar(over: ToolbarOverrides = {}) {
  return render(toolbar(over));
}

// `render` går genom shimen som hårdkodar locale="sv"; det engelska fallet renderas
// via `/pure`, som alias-ankaret lämnar oomskrivet.
function renderToolbarInEnglish(over: ToolbarOverrides = {}) {
  return rawRender(
    <NextIntlClientProvider
      locale="en"
      messages={enMessages}
      timeZone="Europe/Stockholm"
    >
      {toolbar(over)}
    </NextIntlClientProvider>,
  );
}

beforeEach(() => {
  pushMock.mockClear();
});

// 2026-06-30 — Matchning + Dölj ansökta flyttade till hero-filterraden
// (jobb-hero-filters.test.tsx täcker dem). Toolbaren har nu bara count + sort +
// chips (sök/q + grad). Grad-chip-× navigerar via onMatchGradesChange.

describe("JobbResultsToolbar — träffar + chips + sort", () => {
  it("visar mono-formaterat antal träffar", () => {
    const { container } = renderToolbar({ totalCount: 1234 });
    // sv-SE grupperar med NBSP.
    const count = container.querySelector(".jp-results-count");
    expect(count).not.toBeNull();
    expect(count).toHaveTextContent(/1\s234 träffar/);
    // #1505 — the counter is NOT a live region: it renders with its own text, which is the shape
    // ARIA22 rules out. Restoring the role here would re-create the defect AND double the
    // announcement the surface region now carries. Nothing else in the blocking suites pins this
    // (`jobb-results.test.tsx` stubs this island out), so it is pinned at the element itself.
    expect(count).not.toHaveAttribute("role");
    expect(count).not.toHaveAttribute("aria-live");
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

  it("klass 2-chipet namnges ur katalogen; ort och yrke passerar oöversatta (#1537)", () => {
    // Renderas under `en`, inte `sv`: där är katalogvärdet BYTE-IDENTISKT med serverns
    // etikett, så ett test under `sv` passerar även om raden reduceras till
    // `return resolved` — det diskriminerar inte grenen det namnger (code-reviewer,
    // omkontroll 2026-08-28). Under `en` skiljer de sig, och då fäller det.
    renderToolbarInEnglish({
      totalCount: 2,
      employmentType: ["gro4_cWF_6D7"],
      region: ["CifL_Rzy_Mku"],
      resolvedLabels,
    });

    expect(screen.getByText("Substitute position")).toBeInTheDocument();
    // Serverns etikett får inte nå chipet — det är den halvan som fäller
    // `return resolved`.
    expect(screen.queryByText("Vikariat")).toBeNull();
    // Ort är registerdata och passerar oöversatt i varje locale.
    expect(screen.getByText("Stockholms län")).toBeInTheDocument();
    expect(screen.queryByText(/gro4_cWF_6D7/)).toBeNull();
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

  // Param-bevarande: "Dölj ansökta"-toggle:n ägs nu av hero-raden, men toolbarens
  // egna navigeringar (sort/Rensa) MÅSTE bära ?doljAnsokta=on vidare — annars
  // återaktiveras ansökta jobb tyst (samma felklass som matchningOff, nextjs-ui-
  // engineer Major 2026-06-30).
  it("sort-byte bevarar ?doljAnsokta=on (toggle:n ägs av hero:n)", async () => {
    const user = userEvent.setup();
    renderToolbar({
      occupationGroup: ["MVqp_eS8_kDZ"],
      resolvedLabels,
      q: "data",
      hideApplied: true,
    });
    await user.selectOptions(screen.getByLabelText("Sortera"), "Relevans");
    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?occupationGroup=MVqp_eS8_kDZ&doljAnsokta=on&q=data&sortBy=Relevance&commit=true",
    );
  });

  it("Rensa sökord och filter bevarar ?doljAnsokta=on", async () => {
    const user = userEvent.setup();
    renderToolbar({
      occupationGroup: ["MVqp_eS8_kDZ"],
      resolvedLabels,
      q: "data",
      hideApplied: true,
    });
    await user.click(
      screen.getByRole("button", { name: "Rensa sökord och filter" }),
    );
    expect(pushMock).toHaveBeenCalledWith("/jobb?doljAnsokta=on&commit=true");
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

  // #454 PR-0 — toolbar-lokal arbetsgivar-chip (grad-chips-mönstret): renderas
  // när ?employer= är aktiv, formaterad NNNNNN-NNNN, × committar som varje annan chip (#1471).
  describe("arbetsgivar-chip (#454 PR-0)", () => {
    it("renderar chippen med formaterat org.nr när employer är satt", () => {
      renderToolbar({ employer: ["5560125790"] });
      expect(screen.getByText("Arbetsgivare 556012-5790")).toBeInTheDocument();
    });

    it("namnger arbetsgivaren när sidan kunde härleda namnet (#1546)", () => {
      // Ett val gjort på NAMN i typeaheaden kvitterades tidigare med tio siffror. Namnet kommer
      // ur träffarna sidan redan renderat — ingen uppslagstjänst, ingen URL-nyckel.
      renderToolbar({ employer: ["5560125790"], employerName: "Nordiska Bygg AB" });
      expect(screen.getByText("Arbetsgivare Nordiska Bygg AB")).toBeInTheDocument();
      expect(screen.queryByText(/556012-5790/)).toBeNull();
    });

    it("faller tillbaka på org.nr när namnet inte kunde härledas (#1546)", () => {
      // Tom träffmängd (t.ex. en sida bortom sista) → inga rader att läsa namnet ur. Chippet
      // degraderar till dagens beteende i stället för att bli tomt.
      renderToolbar({ employer: ["5560125790"], employerName: undefined });
      expect(screen.getByText("Arbetsgivare 556012-5790")).toBeInTheDocument();
    });

    it("×-knappens namn följer chippens etikett, inte org.nr (#1546)", () => {
      renderToolbar({ employer: ["5560125790"], employerName: "Nordiska Bygg AB" });
      expect(
        screen.getByRole("button", { name: "Ta bort filter Arbetsgivare Nordiska Bygg AB" }),
      ).toBeInTheDocument();
    });

    it("FLERA arbetsgivare kollapsar till EN chip — aldrig en rad råa org.nr", () => {
      // #1547 — Översiktens summor sätter hela bevakningsmängden på axeln. En chip per värde
      // skrev ut "Arbetsgivare 559141-1326 · Arbetsgivare 556012-5790 · …" som enda beskrivning
      // av vad användaren tittar på.
      renderToolbar({ employer: ["5560125790", "5591411326", "5566524301"] });

      expect(screen.getByText("3 arbetsgivare")).toBeInTheDocument();
      expect(screen.queryByText(/Arbetsgivare 5/)).toBeNull();
      expect(screen.getAllByRole("button", { name: /Ta bort filter/ })).toHaveLength(1);
    });

    it("× på den kollapsade chippen tömmer HELA axeln, inte ett värde", () => {
      // The axis is set as one set (every watched company or none), so removing a single value
      // would leave a filter the user cannot name and never asked for.
      const user = userEvent.setup();
      renderToolbar({ employer: ["5560125790", "5591411326"] });

      return user
        .click(screen.getByRole("button", { name: "Ta bort filter 2 arbetsgivare" }))
        .then(() => {
          expect(pushMock).toHaveBeenCalledWith("/jobb?commit=true");
        });
    });

    it("ingen chip utan employer", () => {
      renderToolbar({ employer: [] });
      expect(screen.queryByText(/Arbetsgivare /)).toBeNull();
    });

    it("× tar bort employer ur URL:en med commit-intent (E2j) och bevarar övrig state", async () => {
      const user = userEvent.setup();
      renderToolbar({
        employer: ["5560125790"],
        matchGrades: ["Strong"],
        matchActive: true,
      });
      await user.click(
        screen.getByRole("button", {
          name: "Ta bort filter Arbetsgivare 556012-5790",
        }),
      );
      // employer borta, grad-filtret kvar; ?commit=true som varje annan chip — sedan #1471
      // bär replayen arbetsgivar-axeln, så undantaget från E2j har ingen grund kvar.
      expect(pushMock).toHaveBeenCalledWith("/jobb?matchGrades=Strong&commit=true");
    });

    it("sort-byte bevarar employer (param-bevarande-basen)", async () => {
      const user = userEvent.setup();
      renderToolbar({ employer: ["5560125790"] });
      await user.selectOptions(
        screen.getByLabelText("Sortera"),
        "ExpiresAtAsc",
      );
      const pushed = pushMock.mock.calls.at(-1)?.[0] as string;
      expect(pushed).toContain("employer=5560125790");
      expect(pushed).toContain("sortBy=ExpiresAtAsc");
    });
  });

  // #408 — grad/status läggs ALDRIG i den delade buildChipModels (SPOT med hero-
  // fältets in-field-chips). Verifiera att en hero-fältmiljö (gemensam helper)
  // aldrig skulle se grad/status — här via att grad-chips bara dyker upp i
  // toolbaren och inte påverkar q/dimension-chip-ordningen.
  describe("grad SPOT-isolering (#408)", () => {
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
