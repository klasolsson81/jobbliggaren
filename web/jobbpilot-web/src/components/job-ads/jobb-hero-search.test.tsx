import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
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

describe("JobbHeroSearch — fältet SPEGLAR söket (E2i, CTO VAL 1 = C′)", () => {
  it("initieras till kanonisk spegel av URL-staten", () => {
    setup({ q: "volvo", municipality: ["PVZL_BQT_XtL"] });
    expect(screen.getByRole("combobox")).toHaveValue("Göteborg volvo");
  });

  it("taxonomi-ord + mellanslag → dimension committas, TEXTEN STÅR KVAR", async () => {
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("combobox"), "göteborg ");
    expect(replaceMock).toHaveBeenCalledWith(
      "/jobb?municipality=PVZL_BQT_XtL",
      { scroll: false },
    );
    // E2d/E2h-felklassen död: ordet försvinner ALDRIG ur fältet.
    expect(screen.getByRole("combobox")).toHaveValue("göteborg ");
  });

  it("omatchat ord + mellanslag → fritext-q committas, texten kvar", async () => {
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("combobox"), "hogia ");
    expect(replaceMock).toHaveBeenCalledWith("/jobb?q=hogia", {
      scroll: false,
    });
    expect(screen.getByRole("combobox")).toHaveValue("hogia ");
  });

  it("Klas-flödet: 'göteborg volvo heltid ' → ort + två q-ord, allt kvar i fältet", async () => {
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("combobox"), "göteborg volvo heltid ");
    expect(replaceMock).toHaveBeenLastCalledWith(
      "/jobb?municipality=PVZL_BQT_XtL&q=volvo+heltid",
      { scroll: false },
    );
    expect(screen.getByRole("combobox")).toHaveValue(
      "göteborg volvo heltid ",
    );
  });

  it("pågående ord (caret i ordet) committas INTE", async () => {
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("combobox"), "volvo");
    expect(replaceMock).not.toHaveBeenCalled();
  });

  it("Enter finaliserar pågående ord", async () => {
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("combobox"), "volvo{Enter}");
    expect(replaceMock).toHaveBeenCalledWith("/jobb?q=volvo", {
      scroll: false,
    });
    expect(screen.getByRole("combobox")).toHaveValue("volvo");
  });

  it("Sök-knappen finaliserar", async () => {
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("combobox"), "volvo");
    await user.click(screen.getByRole("button", { name: /Sök/ }));
    expect(replaceMock).toHaveBeenCalledWith("/jobb?q=volvo", {
      scroll: false,
    });
  });

  it("radering av ord + Enter släpper anspråket (delta-remove)", async () => {
    const user = userEvent.setup();
    const { rerender } = setup();
    const input = screen.getByRole("combobox");
    await user.type(input, "göteborg ");
    expect(replaceMock).toHaveBeenCalledWith(
      "/jobb?municipality=PVZL_BQT_XtL",
      { scroll: false },
    );
    // Egen RSC-roundtrip landar (base ikapp) — texten ska INTE röras
    // (own-commit-detektionen via lastCommitted).
    rerender(
      <JobbHeroSearch
        taxonomy={taxonomy}
        q=""
        occupationGroup={[]}
        region={[]}
        municipality={["PVZL_BQT_XtL"]}
        sortBy="PublishedAtDesc"
      />,
    );
    expect(input).toHaveValue("göteborg ");

    await user.clear(input);
    await user.keyboard("{Enter}");
    expect(replaceMock).toHaveBeenLastCalledWith("/jobb", { scroll: false });
  });

  it("delta rör ALDRIG dimensioner texten inte gör anspråk på (I1 — popover-valda)", async () => {
    const user = userEvent.setup();
    // region kom utifrån (popover/URL) — serialiseras till texten; vi
    // verifierar att ett NYTT ord inte raderar den.
    setup({ region: ["CifL_Rzy_Mku"] });
    await user.type(screen.getByRole("combobox"), " volvo ");
    expect(replaceMock).toHaveBeenLastCalledWith(
      "/jobb?region=CifL_Rzy_Mku&q=volvo",
      { scroll: false },
    );
  });

  it("q-max-guard: ordet vägras, notisen visas, texten kvar", async () => {
    const user = userEvent.setup();
    setup({ q: "a".repeat(95) });
    await user.type(screen.getByRole("combobox"), " jättelångt ");
    expect(
      screen.getByText(/Söktexten är full \(max 100 tecken\)/),
    ).toBeInTheDocument();
  });
});

describe("JobbHeroSearch — roundtrip-race (CTO-addendum BESLUT 1)", () => {
  it("mellanliggande egen props-leverans serialiserar INTE om texten (två commits i flykt)", async () => {
    const user = userEvent.setup();
    const { rerender } = setup();
    const input = screen.getByRole("combobox");
    // Två commits i flykt: S1 = {Göteborg}, S2 = {Göteborg, q:volvo}.
    await user.type(input, "göteborg volvo ");
    expect(input).toHaveValue("göteborg volvo ");

    // S1-props landar EFTER S2 committats — mellanliggande EGEN leverans
    // får inte mis-klassas som extern (texten skulle re-serialiseras
    // kanoniskt mitt under skrivning — E2d/E2h-felklassen).
    rerender(
      <JobbHeroSearch
        taxonomy={taxonomy}
        q=""
        occupationGroup={[]}
        region={[]}
        municipality={["PVZL_BQT_XtL"]}
        sortBy="PublishedAtDesc"
      />,
    );
    expect(input).toHaveValue("göteborg volvo ");

    // S2 landar — fortfarande egen, texten orörd.
    rerender(
      <JobbHeroSearch
        taxonomy={taxonomy}
        q="volvo"
        occupationGroup={[]}
        region={[]}
        municipality={["PVZL_BQT_XtL"]}
        sortBy="PublishedAtDesc"
      />,
    );
    expect(input).toHaveValue("göteborg volvo ");
  });
});

describe("JobbHeroSearch — extern divergens (C′ regel 2/3)", () => {
  it("extern navigering (nya props) → texten serialiseras om", () => {
    const { rerender } = setup({ q: "volvo" });
    expect(screen.getByRole("combobox")).toHaveValue("volvo");
    rerender(
      <JobbHeroSearch
        taxonomy={taxonomy}
        q="sjuksköterska"
        occupationGroup={[]}
        region={["CifL_Rzy_Mku"]}
        municipality={[]}
        sortBy="PublishedAtDesc"
      />,
    );
    expect(screen.getByRole("combobox")).toHaveValue(
      "Stockholms län sjuksköterska",
    );
  });

  it("toolbar-× (ren borttagning) → kirurgisk text-edit som bevarar ordningen", async () => {
    const user = userEvent.setup();
    const { rerender } = setup();
    await user.type(
      screen.getByRole("combobox"),
      "volvo göteborg lastbil ",
    );
    // Extern borttagning av Göteborg (toolbar-×) → bara det ordet plockas.
    rerender(
      <JobbHeroSearch
        taxonomy={taxonomy}
        q="volvo lastbil"
        occupationGroup={[]}
        region={[]}
        municipality={[]}
        sortBy="PublishedAtDesc"
      />,
    );
    expect(screen.getByRole("combobox")).toHaveValue("volvo lastbil");
  });

  it("Rensa allt → fältet töms", async () => {
    const user = userEvent.setup();
    const { rerender } = setup();
    await user.type(screen.getByRole("combobox"), "göteborg volvo ");
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
    expect(screen.getByRole("combobox")).toHaveValue("");
  });
});

describe("JobbHeroSearch — förslags-val skriver in label-texten", () => {
  it("val ersätter pågående ord med labeln; dimension committas; annons", async () => {
    stubSuggest([{ kind: 2, conceptId: "PVZL_BQT_XtL", label: "Göteborg" }]);
    const user = userEvent.setup();
    setup({ q: "volvo" });
    const input = screen.getByRole("combobox");
    // Fältet speglar "volvo"; skriv vidare.
    await user.type(input, " göte");
    await user.click(
      await screen.findByRole("option", { name: "Göteborg" }, {
        timeout: 2000,
      }),
    );
    expect(replaceMock).toHaveBeenCalledWith(
      "/jobb?municipality=PVZL_BQT_XtL&q=volvo",
      { scroll: false },
    );
    expect(input).toHaveValue("volvo Göteborg ");
    expect(screen.getByText("Lade till Göteborg")).toBeInTheDocument();
  });

  it("Title-label MED taxonomi-ord skrivs INTE in i texten (I1 — code-reviewer Major 2)", async () => {
    // "Säljare Göteborg" som Title: parse av labeln skulle claima
    // Municipality:Göteborg medan staten får q-ord → permanent I1-brott om
    // labeln skrevs in. Gaten utelämnar insättningen; staten får orden.
    stubSuggest([{ kind: 0, conceptId: null, label: "Säljare Göteborg" }]);
    const user = userEvent.setup();
    setup();
    const input = screen.getByRole("combobox");
    await user.type(input, "sälj");
    await user.click(
      await screen.findByRole(
        "option",
        { name: "Säljare Göteborg" },
        { timeout: 2000 },
      ),
    );
    // q får båda orden (compose Title-append) — ingen municipality-param.
    expect(replaceMock).toHaveBeenCalledWith(
      "/jobb?q=S%C3%A4ljare+G%C3%B6teborg",
      { scroll: false },
    );
    // Texten claimar INTE labeln (utkastet borttaget, ingen insättning).
    expect(input).toHaveValue("");
  });

  it("Tab väljer markerat förslag (Klas-spec)", async () => {
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
    expect(input).toHaveValue("Göteborg ");
  });
});

describe("JobbHeroSearch — no-JS-stöd", () => {
  it("GET-form med hidden inputs för committade params; synliga inputen namnlös", () => {
    const { container } = setup({
      q: "volvo",
      occupationGroup: ["MVqp_eS8_kDZ"],
    });
    const form = container.querySelector("form");
    expect(form).toHaveAttribute("action", "/jobb");
    expect(form).toHaveAttribute("method", "get");
    // Spegel-texten får ALDRIG vara q (dubbel-filtrering) — committad q
    // bärs som hidden input.
    expect(screen.getByRole("combobox")).not.toHaveAttribute("name");
    expect(
      container.querySelector('input[type="hidden"][name="q"]'),
    ).toHaveValue("volvo");
    expect(
      container.querySelector('input[type="hidden"][name="occupationGroup"]'),
    ).toHaveValue("MVqp_eS8_kDZ");
  });

  it("hjälptexten bär tagg-/Tab-instruktionen; ingen placeholder", () => {
    setup();
    expect(
      screen.getByText(/Ord blir taggar i filterraden vid träffarna/),
    ).toBeInTheDocument();
    expect(screen.getByRole("combobox")).not.toHaveAttribute("placeholder");
  });
});

describe("JobbHeroSearch — degraderad taxonomi", () => {
  it("utan träd blir orden fritext-q", async () => {
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
    expect(replaceMock).toHaveBeenCalledWith("/jobb?q=g%C3%B6teborg", {
      scroll: false,
    });
  });
});
