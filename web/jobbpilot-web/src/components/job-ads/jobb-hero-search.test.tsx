import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobbHeroSearch } from "./jobb-hero-search";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";

const replaceMock = vi.fn();
const pushMock = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock, replace: replaceMock }),
}));

const taxonomy: TaxonomyTree = {
  regions: [
    {
      conceptId: "CifL_Rzy_Mku",
      label: "Stockholms län",
      municipalities: [{ conceptId: "zHxw_uJZ_NNh", label: "Solna" }],
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
  ],
};

function setup(extra?: Partial<Parameters<typeof JobbHeroSearch>[0]>) {
  return render(
    <JobbHeroSearch
      taxonomy={taxonomy}
      q=""
      occupationGroup={[]}
      region={[]}
      municipality={[]}
      sortBy="PublishedAtDesc"
      {...extra}
    />,
  );
}

function stubSuggest(
  items: Array<{ kind: number; conceptId: string | null; label: string }>,
) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async () => new Response(JSON.stringify(items), { status: 200 })),
  );
}

beforeEach(() => {
  replaceMock.mockClear();
  pushMock.mockClear();
});
afterEach(() => vi.unstubAllGlobals());

describe("JobbHeroSearch — chips deriveras ur URL:en (E2h, CTO VAL 1=A)", () => {
  it("renderar dimension- och q-ord-chips ur props", () => {
    setup({
      q: "volvo lastbil",
      municipality: ["PVZL_BQT_XtL"],
      occupationGroup: ["MVqp_eS8_kDZ"],
    });
    // Ordning: ort → yrkesgrupp → q-ord (buildChipModels).
    expect(screen.getByText("Göteborg")).toBeInTheDocument();
    expect(screen.getByText("Systemutvecklare")).toBeInTheDocument();
    expect(screen.getByText("volvo")).toBeInTheDocument();
    expect(screen.getByText("lastbil")).toBeInTheDocument();
  });

  it("chip-× tar bort ur rätt axel via router.replace (samma operation som toolbar-×)", async () => {
    const user = userEvent.setup();
    setup({ q: "volvo", municipality: ["PVZL_BQT_XtL"] });
    await user.click(
      screen.getByRole("button", { name: "Ta bort Göteborg" }),
    );
    expect(replaceMock).toHaveBeenCalledWith("/jobb?q=volvo", {
      scroll: false,
    });
  });

  it("q-ord-chip-× tar bort bara det ordet ur q", async () => {
    const user = userEvent.setup();
    setup({ q: "volvo lastbil" });
    await user.click(screen.getByRole("button", { name: "Ta bort volvo" }));
    expect(replaceMock).toHaveBeenCalledWith("/jobb?q=lastbil", {
      scroll: false,
    });
  });

  it("extern URL-ändring (nya props) speglas i chipsen — ingen lokal kopia", () => {
    const { rerender } = setup({ municipality: ["PVZL_BQT_XtL"] });
    expect(screen.getByText("Göteborg")).toBeInTheDocument();
    rerender(
      <JobbHeroSearch
        taxonomy={taxonomy}
        q=""
        occupationGroup={[]}
        region={[]}
        municipality={[]}
        sortBy="PublishedAtDesc"
      />,
    );
    expect(screen.queryByText("Göteborg")).toBeNull();
  });
});

describe("JobbHeroSearch — tokenisering vid skrivning (E2h)", () => {
  it("mellanslag efter taxonomi-ord → dimension-chip live-committas (replace)", async () => {
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("combobox"), "göteborg ");
    expect(replaceMock).toHaveBeenCalledWith(
      "/jobb?municipality=PVZL_BQT_XtL",
      { scroll: false },
    );
    // Utkastet är tömt — ordet blev chip.
    expect(screen.getByRole("combobox")).toHaveValue("");
  });

  it("mellanslag efter omatchat ord → fritext-q-chip", async () => {
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("combobox"), "hogia ");
    expect(replaceMock).toHaveBeenCalledWith("/jobb?q=hogia", {
      scroll: false,
    });
  });

  it("pågående ord committas INTE förrän avgränsare/Enter", async () => {
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("combobox"), "volvo");
    expect(replaceMock).not.toHaveBeenCalled();
    expect(screen.getByRole("combobox")).toHaveValue("volvo");
  });

  it("Enter finaliserar utkastet som fritext (Sök = utkast-commit)", async () => {
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("combobox"), "volvo{Enter}");
    expect(replaceMock).toHaveBeenCalledWith("/jobb?q=volvo", {
      scroll: false,
    });
  });

  it("Sök-knappen finaliserar utkastet", async () => {
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("combobox"), "volvo");
    await user.click(screen.getByRole("button", { name: /Sök/ }));
    expect(replaceMock).toHaveBeenCalledWith("/jobb?q=volvo", {
      scroll: false,
    });
  });

  it("nytt ord ovanpå befintliga filter bevarar dem (param-bevarande)", async () => {
    const user = userEvent.setup();
    setup({ q: "volvo", municipality: ["PVZL_BQT_XtL"] });
    await user.type(screen.getByRole("combobox"), "lastbil ");
    expect(replaceMock).toHaveBeenCalledWith(
      "/jobb?municipality=PVZL_BQT_XtL&q=volvo+lastbil",
      { scroll: false },
    );
  });

  it("q-max-guard: ordet vägras, stannar i fältet och hjälptexten skiftar", async () => {
    const user = userEvent.setup();
    setup({ q: "a".repeat(95) });
    await user.type(screen.getByRole("combobox"), "jättelångt ");
    expect(replaceMock).not.toHaveBeenCalled();
    expect(screen.getByRole("combobox")).toHaveValue("jättelångt");
    expect(
      screen.getByText(/Söktexten är full \(max 100 tecken\)/),
    ).toBeInTheDocument();
  });

  it("Backspace i tomt fält tar bort sista chipen", async () => {
    const user = userEvent.setup();
    setup({ q: "volvo", municipality: ["PVZL_BQT_XtL"] });
    const input = screen.getByRole("combobox");
    await user.click(input);
    await user.keyboard("{Backspace}");
    // Sista chipen är q-ordet "volvo" (ordning: dimensioner → q-ord).
    expect(replaceMock).toHaveBeenCalledWith(
      "/jobb?municipality=PVZL_BQT_XtL",
      { scroll: false },
    );
  });
});

describe("JobbHeroSearch — förslags-val → chip i fältet (E2d-buggen död)", () => {
  it("val av taxonomi-förslag committar chip och tömmer BARA utkastet", async () => {
    stubSuggest([{ kind: 2, conceptId: "PVZL_BQT_XtL", label: "Göteborg" }]);
    const user = userEvent.setup();
    setup({ q: "volvo" });
    const input = screen.getByRole("combobox");
    await user.type(input, "göte");
    await user.click(
      await screen.findByRole("option", { name: "Göteborg" }, {
        timeout: 2000,
      }),
    );
    // Chip committad med bevarad q — INGEN direkt-sök-och-töm-av-allt.
    expect(replaceMock).toHaveBeenCalledWith(
      "/jobb?municipality=PVZL_BQT_XtL&q=volvo",
      { scroll: false },
    );
    // Utkastet ("göte") ersattes av chipet; committade chips står kvar.
    expect(input).toHaveValue("");
    expect(screen.getByText("volvo")).toBeInTheDocument();
    // aria-live-annonsen (F4-mitigering 3, kongruensfri form per
    // design-reviewer M3).
    expect(screen.getByText("Lade till Göteborg")).toBeInTheDocument();
  });

  it("chip-borttagning annonseras via aria-live", async () => {
    const user = userEvent.setup();
    setup({ municipality: ["PVZL_BQT_XtL"] });
    await user.click(
      screen.getByRole("button", { name: "Ta bort Göteborg" }),
    );
    expect(screen.getByText("Tog bort Göteborg")).toBeInTheDocument();
  });

  it("Tab väljer markerat förslag (Klas-spec tabba-klart)", async () => {
    stubSuggest([{ kind: 2, conceptId: "PVZL_BQT_XtL", label: "Göteborg" }]);
    const user = userEvent.setup();
    setup();
    const input = screen.getByRole("combobox");
    await user.type(input, "göte");
    await screen.findByRole("option", { name: "Göteborg" }, { timeout: 2000 });
    await user.keyboard("{ArrowDown}");
    await user.tab();
    expect(replaceMock).toHaveBeenCalledWith(
      "/jobb?municipality=PVZL_BQT_XtL",
      { scroll: false },
    );
  });
});

describe("JobbHeroSearch — no-JS-stöd", () => {
  it("formen är GET mot /jobb med hidden inputs för aktiva filter + committad q", () => {
    const { container } = setup({
      q: "volvo",
      occupationGroup: ["MVqp_eS8_kDZ"],
      region: ["CifL_Rzy_Mku"],
    });
    const form = container.querySelector("form");
    expect(form).toHaveAttribute("action", "/jobb");
    expect(form).toHaveAttribute("method", "get");
    expect(
      container.querySelector('input[type="hidden"][name="q"]'),
    ).toHaveValue("volvo");
    expect(
      container.querySelector('input[type="hidden"][name="occupationGroup"]'),
    ).toHaveValue("MVqp_eS8_kDZ");
    expect(
      container.querySelector('input[type="hidden"][name="region"]'),
    ).toHaveValue("CifL_Rzy_Mku");
  });

  it("hjälptexten bär tagg-/Tab-instruktionen (ingen placeholder)", () => {
    setup();
    expect(
      screen.getByText(/Ord blir taggar när du skriver mellanslag/),
    ).toBeInTheDocument();
    expect(screen.getByRole("combobox")).not.toHaveAttribute("placeholder");
  });
});

describe("JobbHeroSearch — degraderad taxonomi", () => {
  it("fungerar utan träd: ord blir fritext-chips", async () => {
    const user = userEvent.setup();
    render(
      <JobbHeroSearch
        taxonomy={null}
        q=""
        occupationGroup={[]}
        region={[]}
        municipality={[]}
        sortBy="PublishedAtDesc"
      />,
    );
    await user.type(screen.getByRole("combobox"), "göteborg ");
    // URLSearchParams percent-encodar icke-ASCII (ö → %C3%B6).
    expect(replaceMock).toHaveBeenCalledWith("/jobb?q=g%C3%B6teborg", {
      scroll: false,
    });
  });
});
