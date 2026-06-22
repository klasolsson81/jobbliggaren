import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within, act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobbHeroFilters } from "./jobb-hero-filters";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";

const pushMock = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
}));

// useTransition i jsdom kör startTransition synkront nog för push-assert.
const taxonomy: TaxonomyTree = {
  // ADR 0043-amendment 2026-06-13 (Klass 2) — required; not exercised here.
  employmentTypes: [],
  worktimeExtents: [],
  regions: [
    {
      conceptId: "CifL_Rzy_Mku",
      label: "Stockholms län",
      municipalities: [
        { conceptId: "AvNB_uwa_6n6", label: "Stockholm" },
        { conceptId: "zHxw_uJZ_NNh", label: "Solna" },
      ],
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
      occupationGroups: [
        { conceptId: "MVqp_eS8_kDZ", label: "Systemutvecklare" },
        { conceptId: "Q5DF_juj_8do", label: "Mjukvaruarkitekt" },
      ],
    },
    {
      conceptId: "X1bg_e2a_ABC",
      label: "Bygg och anläggning",
      occupationGroups: [{ conceptId: "Z9zz_zzz_zzz", label: "Snickare" }],
    },
  ],
};

beforeEach(() => {
  pushMock.mockClear();
});

function setup(extra?: Partial<Parameters<typeof JobbHeroFilters>[0]>) {
  return render(
    <JobbHeroFilters
      taxonomy={taxonomy}
      initialOccupationGroup={[]}
      initialRegion={[]}
      initialMunicipality={[]}
      initialEmploymentType={[]}
      initialWorktimeExtent={[]}
      q=""
      sortBy="PublishedAtDesc"
      {...extra}
    />,
  );
}

describe("JobbHeroFilters — Ort tvåkolumns Län→Kommun (ADR 0067 Fas E2b)", () => {
  it("höger kolumn är TOM tills ett län valts (E2f Platsbanken-paritet)", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));

    const dialog = screen.getByRole("dialog", { name: "Ort" });
    expect(within(dialog).getByText("Stockholms län")).toBeInTheDocument();
    // Ingen auto-vald första grupp — tomtext + inga kommun-rader.
    expect(
      within(dialog).getByText("Välj ett län till vänster."),
    ).toBeInTheDocument();
    expect(within(dialog).queryByText("Solna")).toBeNull();

    // Klick på län → kommuner + Hela länet-raden visas.
    await user.click(within(dialog).getByText("Stockholms län"));
    expect(within(dialog).getByText("Hela Stockholms län")).toBeInTheDocument();
    expect(within(dialog).getByText("Stockholm")).toBeInTheDocument();
    expect(within(dialog).getByText("Solna")).toBeInTheDocument();
  });

  it("kommun-val commit:ar ?municipality= till URL", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    await user.click(screen.getByText("Stockholms län"));
    await user.click(screen.getByText("Solna"));

    expect(pushMock).toHaveBeenCalledWith("/jobb?municipality=zHxw_uJZ_NNh");
  });

  it("Hela länet togglar ETT region-id — aldrig materialiserade kommun-ids", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    await user.click(screen.getByText("Stockholms län"));
    await user.click(screen.getByText("Hela Stockholms län"));

    expect(pushMock).toHaveBeenCalledWith("/jobb?region=CifL_Rzy_Mku");
  });

  it("kommun-rader RENDERAS markerade när Hela länet är valt (E2f)", async () => {
    const user = userEvent.setup();
    setup({ initialRegion: ["CifL_Rzy_Mku"] });
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    await user.click(screen.getByText("Stockholms län"));

    // Alla kommun-rader + Hela länet-raden visas ikryssade (tydligt vad
    // valet omfattar — Platsbanken-paritet).
    const dialog = screen.getByRole("dialog", { name: "Ort" });
    const checked = within(dialog)
      .getAllByRole("checkbox")
      .filter((el) => el.getAttribute("aria-checked") === "true");
    expect(checked.length).toBe(3); // Hela länet + Stockholm + Solna
  });

  it("Hela {länsnamn}-raden är tri-state 'mixed' vid partiellt val (E2d-Minor)", async () => {
    const user = userEvent.setup();
    // Bara Solna vald (inte hela länet, inte Stockholm) → partiellt.
    setup({ initialMunicipality: ["zHxw_uJZ_NNh"] });
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    await user.click(screen.getByText("Stockholms län"));

    const selectAll = screen.getByText("Hela Stockholms län").closest(
      '[role="checkbox"]',
    );
    expect(selectAll).toHaveAttribute("aria-checked", "mixed");
  });

  it("kommun-klick under helläns-val = hela länet minus den kommunen (E2f)", async () => {
    const user = userEvent.setup();
    setup({ initialRegion: ["CifL_Rzy_Mku"] });
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    await user.click(screen.getByText("Stockholms län"));
    await user.click(screen.getByText("Solna"));

    // Region bort; länets ÖVRIGA kommun (Stockholm) materialiseras.
    expect(pushMock).toHaveBeenCalledWith("/jobb?municipality=AvNB_uwa_6n6");
  });

  it("markering som kompletterar länets alla kommuner kollapsar till Hela länet (E2f)", async () => {
    const user = userEvent.setup();
    setup({ initialMunicipality: ["AvNB_uwa_6n6"] });
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    await user.click(screen.getByText("Stockholms län"));
    await user.click(screen.getByText("Solna"));

    // Stockholm + Solna = hela länet → region-id, kommun-ids bort.
    expect(pushMock).toHaveBeenCalledWith("/jobb?region=CifL_Rzy_Mku");
  });

  it("Hela länet rensar länets egna kommun-val men inte andra läns", async () => {
    const user = userEvent.setup();
    setup({ initialMunicipality: ["zHxw_uJZ_NNh", "PVZL_BQT_XtL"] });
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    await user.click(screen.getByText("Stockholms län"));
    await user.click(screen.getByText("Hela Stockholms län"));

    // Solna (Sthlm) rensad; Göteborg (VG) kvar; region Sthlm in.
    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?region=CifL_Rzy_Mku&municipality=PVZL_BQT_XtL",
    );
  });

  it("cross-län-mix är giltig: helt län + kommun i annat län (backend-union)", async () => {
    const user = userEvent.setup();
    setup({ initialRegion: ["oDpK_oQy_3Zc"] });
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    await user.click(screen.getByText("Stockholms län"));
    await user.click(screen.getByText("Solna"));

    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?region=oDpK_oQy_3Zc&municipality=zHxw_uJZ_NNh",
    );
  });

  it("header-Rensa nollar BÅDA ort-axlarna", async () => {
    const user = userEvent.setup();
    setup({
      initialRegion: ["oDpK_oQy_3Zc"],
      initialMunicipality: ["zHxw_uJZ_NNh"],
    });
    await user.click(screen.getByRole("button", { name: /^Ort/ }));

    const dialog = screen.getByRole("dialog", { name: "Ort" });
    // Vänster-kolumnens (header-)Rensa är den första.
    const [rensa] = within(dialog).getAllByRole("button", { name: "Rensa" });
    expect(rensa).toBeDefined();
    await user.click(rensa!);

    expect(pushMock).toHaveBeenCalledWith("/jobb");
  });

  it("Ort-pillens räknare = region + kommun", () => {
    setup({
      initialRegion: ["oDpK_oQy_3Zc"],
      initialMunicipality: ["zHxw_uJZ_NNh"],
    });
    const ortBtn = screen.getByRole("button", { name: /^Ort/ });
    expect(within(ortBtn).getByText("2")).toBeInTheDocument();
  });

  it("extern URL-ändring (nya props) synkar öns val — E2g-buggen", async () => {
    // Klas-buggen 2026-06-11: toolbar-chippens × rensade filtret men
    // popovern visade länet markerat — öns gamla useState-kopia synkade
    // aldrig. useOptimistic (CTO Variant A): props är sanningen.
    const user = userEvent.setup();
    const { rerender } = render(
      <JobbHeroFilters
        taxonomy={taxonomy}
        initialOccupationGroup={[]}
        initialRegion={["CifL_Rzy_Mku"]}
        initialMunicipality={[]}
        initialEmploymentType={[]}
        initialWorktimeExtent={[]}
        q=""
        sortBy="PublishedAtDesc"
      />,
    );
    expect(
      within(screen.getByRole("button", { name: /^Ort/ })).getByText("1"),
    ).toBeInTheDocument();

    // Extern ändring (chip-× / Rensa alla filter / recent-navigering) →
    // RSC re-render med nya props.
    rerender(
      <JobbHeroFilters
        taxonomy={taxonomy}
        initialOccupationGroup={[]}
        initialRegion={[]}
        initialMunicipality={[]}
        initialEmploymentType={[]}
        initialWorktimeExtent={[]}
        q=""
        sortBy="PublishedAtDesc"
      />,
    );
    expect(
      within(screen.getByRole("button", { name: /^Ort/ })).queryByText("1"),
    ).toBeNull();

    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    await user.click(screen.getByText("Stockholms län"));
    const dialog = screen.getByRole("dialog", { name: "Ort" });
    const checked = within(dialog)
      .getAllByRole("checkbox")
      .filter((el) => el.getAttribute("aria-checked") === "true");
    expect(checked.length).toBe(0);
  });

  it("ESC stänger popovern", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    expect(screen.getByRole("dialog", { name: "Ort" })).toBeInTheDocument();

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog", { name: "Ort" })).toBeNull();
  });
});

describe("JobbHeroFilters — Yrke tvåkolumns", () => {
  it("höger kolumn tom tills yrkesområde valts; val visar yrkesgrupperna (E2f)", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Yrke/ }));

    const dialog = screen.getByRole("dialog", { name: "Yrke" });
    expect(
      within(dialog).getByText("Välj ett yrkesområde till vänster."),
    ).toBeInTheDocument();
    expect(within(dialog).queryByText("Systemutvecklare")).toBeNull();

    await user.click(within(dialog).getByText("Data/IT"));
    expect(
      within(dialog).getByText("Välj alla yrkesgrupper"),
    ).toBeInTheDocument();
    expect(within(dialog).getByText("Systemutvecklare")).toBeInTheDocument();
    expect(within(dialog).getByText("Mjukvaruarkitekt")).toBeInTheDocument();
  });

  it("byter aktiv vänsterkolumn och commit:ar yrkes-conceptId", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Yrke/ }));

    await user.click(screen.getByRole("button", { name: /Bygg och anläggning/ }));
    await user.click(screen.getByText("Snickare"));

    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?occupationGroup=Z9zz_zzz_zzz",
    );
  });

  it("Välj alla yrkesgrupper materialiserar höger-kolumnens ids (enaxel)", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Yrke/ }));
    await user.click(screen.getByText("Data/IT"));
    await user.click(screen.getByText("Välj alla yrkesgrupper"));

    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?occupationGroup=MVqp_eS8_kDZ&occupationGroup=Q5DF_juj_8do",
    );
  });

  it("bevarar q i URL:en när ett yrke väljs (param-bevarande)", async () => {
    const user = userEvent.setup();
    setup({ q: "backend" });
    await user.click(screen.getByRole("button", { name: /^Yrke/ }));
    await user.click(screen.getByText("Data/IT"));
    await user.click(screen.getByText("Systemutvecklare"));

    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?occupationGroup=MVqp_eS8_kDZ&q=backend",
    );
  });
});

describe("JobbHeroFilters — facet-counts + Visa N annonser (E2c)", () => {
  it("renderar per-option-counts i kommun-rader + Hela länet när fetch svarar", async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      const body = url.includes("dimension=Municipality")
        ? { zHxw_uJZ_NNh: 12, AvNB_uwa_6n6: 340 }
        : url.includes("dimension=Region")
          ? { CifL_Rzy_Mku: 1500 }
          : {};
      return new Response(JSON.stringify(body), { status: 200 });
    });
    vi.stubGlobal("fetch", fetchMock);

    try {
      setup();
      await user.click(screen.getByRole("button", { name: /^Ort/ }));
      await user.click(screen.getByText("Stockholms län"));

      // Debounce 300 ms → vänta in counts-renderingen.
      expect(await screen.findByText("(12)")).toBeInTheDocument();
      expect(screen.getByText("(340)")).toBeInTheDocument();
      // "Hela länet"-radens count = region-facetten för aktiva länet.
      expect(screen.getByText(/1\s500/)).toBeInTheDocument();
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it("visar inga counts vid degradering (fetch saknas) — popovern användbar", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    await user.click(screen.getByText("Stockholms län"));

    expect(screen.getByText("Solna")).toBeInTheDocument();
    expect(screen.queryByText(/\(\d/)).toBeNull();
  });

  it("backend-fel (502) ger INGA '(0)'-rader — fel ≠ känd nolla (code-reviewer Major 1)", async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn(
      async () => new Response(JSON.stringify({}), { status: 502 }),
    );
    vi.stubGlobal("fetch", fetchMock);

    try {
      setup();
      await user.click(screen.getByRole("button", { name: /^Ort/ }));
      await user.click(screen.getByText("Stockholms län"));

      // Vänta in debounce + svar; därefter får INGA count-parenteser finnas
      // ("(0)" vid backend-fel vore desinformation — tom dict är tvetydig).
      await vi.waitFor(() => expect(fetchMock).toHaveBeenCalled());
      expect(screen.getByText("Solna")).toBeInTheDocument();
      expect(screen.queryByText(/\(\d/)).toBeNull();
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it("Visa-knappen singular-böjs vid totalCount 1 (design-reviewer Major 1)", async () => {
    const user = userEvent.setup();
    const { publishTotalCount, resetTotalCountForTest } = await import(
      "@/lib/job-ads/total-count-store"
    );
    resetTotalCountForTest();
    setup();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));

    act(() => publishTotalCount(1));
    expect(
      screen.getByRole("button", { name: "Visa 1 annons" }),
    ).toBeInTheDocument();

    act(() => publishTotalCount(2));
    expect(
      screen.getByRole("button", { name: "Visa 2 annonser" }),
    ).toBeInTheDocument();
    resetTotalCountForTest();
  });

  it("Visa annonser-knappen stänger popovern (totalCount opublicerad → utan tal)", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));

    const btn = screen.getByRole("button", { name: "Visa annonser" });
    await user.click(btn);
    expect(screen.queryByRole("dialog", { name: "Ort" })).toBeNull();
    // Stängning är navigations-fri — inga router-pushes från knappen.
    expect(pushMock).not.toHaveBeenCalled();
  });
});

describe("JobbHeroFilters — degraderad taxonomi", () => {
  it("visar civil fallback-text när trädet saknas", async () => {
    const user = userEvent.setup();
    render(
      <JobbHeroFilters
        taxonomy={null}
        initialOccupationGroup={[]}
        initialRegion={[]}
        initialMunicipality={[]}
        initialEmploymentType={[]}
        initialWorktimeExtent={[]}
        q=""
        sortBy="PublishedAtDesc"
      />,
    );
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    expect(
      screen.getByText(/Län kunde inte laddas just nu/),
    ).toBeInTheDocument();
  });
});
