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
    { conceptId: "CifL_Rzy_Mku", label: "Stockholms län" },
    { conceptId: "oDpK_oQy_3Zc", label: "Västra Götalands län" },
  ],
  occupationFields: [
    {
      conceptId: "apaJ_2ja_LuF",
      label: "Data/IT",
      occupations: [
        { conceptId: "MVqp_eS8_kDZ", label: "Systemutvecklare" },
        { conceptId: "Q5DF_juj_8do", label: "Mjukvaruarkitekt" },
      ],
    },
    {
      conceptId: "X1bg_e2a_ABC",
      label: "Bygg och anläggning",
      occupations: [{ conceptId: "Z9zz_zzz_zzz", label: "Snickare" }],
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
      initialSsyk={[]}
      initialRegion={[]}
      q=""
      sortBy="PublishedAtDesc"
      {...extra}
    />,
  );
}

describe("JobbHeroFilters — Ort enkelkolumns (ADR 0055 amendment)", () => {
  it("öppnar Ort-popovern och listar län + Välj alla län", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));

    const dialog = screen.getByRole("dialog", { name: "Län" });
    expect(within(dialog).getByText("Välj alla län")).toBeInTheDocument();
    expect(within(dialog).getByText("Stockholms län")).toBeInTheDocument();
    expect(
      within(dialog).getByText("Västra Götalands län"),
    ).toBeInTheDocument();
  });

  it("live-commit:ar conceptId till URL vid klick på ett län", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    await user.click(screen.getByText("Stockholms län"));

    expect(pushMock).toHaveBeenCalledWith("/jobb?region=CifL_Rzy_Mku");
  });

  it("Välj alla län commit:ar samtliga conceptId", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Ort/ }));
    await user.click(screen.getByText("Välj alla län"));

    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?region=CifL_Rzy_Mku&region=oDpK_oQy_3Zc",
    );
  });

  it("Rensa visas endast vid val och nollar axeln", async () => {
    const user = userEvent.setup();
    setup({ initialRegion: ["CifL_Rzy_Mku"] });
    await user.click(screen.getByRole("button", { name: /^Ort/ }));

    const dialog = screen.getByRole("dialog", { name: "Län" });
    const rensa = within(dialog).getByRole("button", { name: "Rensa" });
    await user.click(rensa);

    expect(pushMock).toHaveBeenCalledWith("/jobb");
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
    // Första gruppen aktiv per default → dess yrken syns.
    expect(within(dialog).getByText("Välj alla yrken")).toBeInTheDocument();
    expect(within(dialog).getByText("Systemutvecklare")).toBeInTheDocument();
    expect(within(dialog).getByText("Mjukvaruarkitekt")).toBeInTheDocument();
  });

  it("byter aktiv vänsterkolumn och commit:ar yrkes-conceptId", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: /^Yrke/ }));

    await user.click(screen.getByRole("option", { name: /Bygg och anläggning/ }));
    await user.click(screen.getByText("Snickare"));

    expect(pushMock).toHaveBeenCalledWith("/jobb?ssyk=Z9zz_zzz_zzz");
  });

  it("bevarar q i URL:en när ett yrke väljs (param-bevarande)", async () => {
    const user = userEvent.setup();
    setup({ q: "backend" });
    await user.click(screen.getByRole("button", { name: /^Yrke/ }));
    await user.click(screen.getByText("Systemutvecklare"));

    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?ssyk=MVqp_eS8_kDZ&q=backend",
    );
  });
});

describe("JobbHeroFilters — degraderad taxonomi", () => {
  it("visar civil fallback-text när trädet saknas", async () => {
    const user = userEvent.setup();
    render(
      <JobbHeroFilters
        taxonomy={null}
        initialSsyk={[]}
        initialRegion={[]}
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
