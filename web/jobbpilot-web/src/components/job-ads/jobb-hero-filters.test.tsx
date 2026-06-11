import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobbHeroFilters } from "./jobb-hero-filters";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";

const pushMock = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
}));

// useTransition i jsdom kör startTransition synkront nog för push-assert.
const taxonomy: TaxonomyTree = {
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
      q=""
      sortBy="PublishedAtDesc"
      {...extra}
    />,
  );
}

describe("JobbHeroFilters — Ort tvåkolumns Län→Kommun (ADR 0067 Fas E2b)", () => {
  it("öppnar Ort-popovern: län till vänster, första länets kommuner till höger", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));

    const dialog = screen.getByRole("dialog", { name: "Län" });
    expect(within(dialog).getByText("Stockholms län")).toBeInTheDocument();
    expect(
      within(dialog).getByText("Västra Götalands län"),
    ).toBeInTheDocument();
    // Första länet aktivt per default → dess kommuner + Hela länet-rad syns.
    expect(within(dialog).getByText("Hela länet")).toBeInTheDocument();
    expect(within(dialog).getByText("Stockholm")).toBeInTheDocument();
    expect(within(dialog).getByText("Solna")).toBeInTheDocument();
  });

  it("kommun-val commit:ar ?municipality= till URL", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    await user.click(screen.getByText("Solna"));

    expect(pushMock).toHaveBeenCalledWith("/jobb?municipality=zHxw_uJZ_NNh");
  });

  it("Hela länet togglar ETT region-id — aldrig materialiserade kommun-ids", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    await user.click(screen.getByText("Hela länet"));

    expect(pushMock).toHaveBeenCalledWith("/jobb?region=CifL_Rzy_Mku");
  });

  it("kommun-val i valt län ersätter helläns-valet (per-län-normalisering)", async () => {
    const user = userEvent.setup();
    setup({ initialRegion: ["CifL_Rzy_Mku"] });
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    await user.click(screen.getByText("Solna"));

    // Region X bort, kommunen in — ingen AND-noll-fälla, ingen dubbel-state.
    expect(pushMock).toHaveBeenCalledWith("/jobb?municipality=zHxw_uJZ_NNh");
  });

  it("Hela länet rensar länets egna kommun-val men inte andra läns", async () => {
    const user = userEvent.setup();
    setup({ initialMunicipality: ["zHxw_uJZ_NNh", "PVZL_BQT_XtL"] });
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    await user.click(screen.getByText("Hela länet"));

    // Solna (Sthlm) rensad; Göteborg (VG) kvar; region Sthlm in.
    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?region=CifL_Rzy_Mku&municipality=PVZL_BQT_XtL",
    );
  });

  it("cross-län-mix är giltig: helt län + kommun i annat län (backend-union)", async () => {
    const user = userEvent.setup();
    setup({ initialRegion: ["oDpK_oQy_3Zc"] });
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    // Aktivt län är första (Stockholms län) — välj Solna där.
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

    const dialog = screen.getByRole("dialog", { name: "Län" });
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

  it("ESC stänger popovern", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    expect(screen.getByRole("dialog", { name: "Län" })).toBeInTheDocument();

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog", { name: "Län" })).toBeNull();
  });
});

describe("JobbHeroFilters — Yrke tvåkolumns", () => {
  it("visar yrkesområden och yrken för aktivt område", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Yrke/ }));

    const dialog = screen.getByRole("dialog", { name: "Yrkesområde" });
    // Första gruppen aktiv per default → dess yrkesgrupper syns.
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

    await user.click(screen.getByRole("option", { name: /Bygg och anläggning/ }));
    await user.click(screen.getByText("Snickare"));

    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?occupationGroup=Z9zz_zzz_zzz",
    );
  });

  it("Välj alla yrkesgrupper materialiserar höger-kolumnens ids (enaxel)", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Yrke/ }));
    await user.click(screen.getByText("Välj alla yrkesgrupper"));

    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?occupationGroup=MVqp_eS8_kDZ&occupationGroup=Q5DF_juj_8do",
    );
  });

  it("bevarar q i URL:en när ett yrke väljs (param-bevarande)", async () => {
    const user = userEvent.setup();
    setup({ q: "backend" });
    await user.click(screen.getByRole("button", { name: /^Yrke/ }));
    await user.click(screen.getByText("Systemutvecklare"));

    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?occupationGroup=MVqp_eS8_kDZ&q=backend",
    );
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
