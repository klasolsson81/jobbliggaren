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
  // ADR 0043-amendment 2026-06-13 (Klass 2) — required; not exercised here.
  employmentTypes: [],
  worktimeExtents: [],
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
      employmentType={[]}
      worktimeExtent={[]}
      matchGrades={[]}
      employer={undefined}
      sortBy="PublishedAtDesc"
      initialCommitted={false}
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


// #823 — notisen är en EGEN rad UNDER hjälptexten (den ersätter den inte), och texten finns
// med flit också i komponentens enda aria-live-region. Testerna läser därför den synliga
// notisraden explicit i stället för att matcha texten globalt (som blir strict-mode).
const noticeLine = () =>
  document.querySelector("p.jp-hero__searchhelp--notice")?.textContent ?? "";
const helpLine = () =>
  document.querySelector(
    "p.jp-hero__searchhelp:not(.jp-hero__searchhelp--notice)"
  )?.textContent ?? "";

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

  it("Enter finaliserar pågående ord + commit-intent (E2j ?commit=true)", async () => {
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("combobox"), "volvo{Enter}");
    // E2j: Enter är en commit-punkt → ?commit=true så backend auto-capturerar.
    expect(replaceMock).toHaveBeenCalledWith("/jobb?q=volvo&commit=true", {
      scroll: false,
    });
    expect(screen.getByRole("combobox")).toHaveValue("volvo");
  });

  it("Sök-knappen finaliserar + commit-intent (E2j ?commit=true)", async () => {
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("combobox"), "volvo");
    await user.click(screen.getByRole("button", { name: /^Sök/ }));
    expect(replaceMock).toHaveBeenCalledWith("/jobb?q=volvo&commit=true", {
      scroll: false,
    });
  });

  // #823 — q-min-regeln. Den bor i applyClaimsDelta (enhetstestad i tokenize.test.ts);
  // här pinnas komponentens beteende: söket KÖRS, det för korta ordet används bara inte,
  // och notisen förklarar varför. En tidigare version höll hela navigeringen i stället —
  // det skapade en återvändsgränd ("i göteborg" gick inte att söka) och korrumperade
  // delta-bokföringen. Semantiken speglar backendens SearchQueryParser.
  describe("q-min-regeln (#823)", () => {
    it("Sök med ett tecken: navigerar UTAN q och visar notisen — inget teknisk-fel-kort", async () => {
      const user = userEvent.setup();
      setup();

      await user.type(screen.getByRole("combobox"), "a");
      await user.click(screen.getByRole("button", { name: /^Sök/ }));

      expect(replaceMock).toHaveBeenCalled();
      const [href] = replaceMock.mock.calls[0] ?? [];
      // Ingen q-param → backendens 400-väg nås aldrig.
      expect(String(href)).not.toContain("q=");
      expect(noticeLine()).toMatch(/”a” är kortare än 2 tecken och används inte/);
      // Hjälptexten (sökmodellens instruktion) får INTE försvinna när notisen visas.
      expect(helpLine()).toMatch(/Ord blir taggar i filterraden/);
    });

    it("'i göteborg': ortfiltret körs ändå — residualen 'i' stryks, ingen återvändsgränd", async () => {
      const user = userEvent.setup();
      setup();

      await user.type(screen.getByRole("combobox"), "i göteborg");
      await user.click(screen.getByRole("button", { name: /^Sök/ }));

      const [href] = replaceMock.mock.calls.at(-1) ?? [];
      expect(String(href)).toContain("municipality=PVZL_BQT_XtL");
      expect(String(href)).not.toContain("q=");
      expect(noticeLine()).toMatch(/”i” är kortare än 2 tecken och används inte/);
      // Hjälptexten (sökmodellens instruktion) får INTE försvinna när notisen visas.
      expect(helpLine()).toMatch(/Ord blir taggar i filterraden/);
    });

    it("'a bc' släpps igenom oförändrat — regeln gäller hela q, inte varje ord", async () => {
      const user = userEvent.setup();
      setup();

      await user.type(screen.getByRole("combobox"), "a bc");
      await user.click(screen.getByRole("button", { name: /^Sök/ }));

      const [href] = replaceMock.mock.calls.at(-1) ?? [];
      expect(String(href)).toContain("q=a+bc");
      expect(noticeLine()).toBe("");
    });

    it("notisen försvinner vid nästa redigering — utan att man behöver söka igen", async () => {
      // Ett pågående ord är ingen commit-punkt (CTO VAL 3), så utan självläkning stod
      // notisen kvar och ljög — även med TOMT fält, där ×-knappen inte ens renderas.
      const user = userEvent.setup();
      setup();

      await user.type(screen.getByRole("combobox"), "a");
      await user.click(screen.getByRole("button", { name: /^Sök/ }));
      expect(noticeLine()).toMatch(/”a” är kortare än 2 tecken och används inte/);
      // Hjälptexten (sökmodellens instruktion) får INTE försvinna när notisen visas.
      expect(helpLine()).toMatch(/Ord blir taggar i filterraden/);

      await user.type(screen.getByRole("combobox"), "b");

      expect(noticeLine()).toBe("");
    });

    // VAKTEN för `setAnnouncement("")` vid självläkningen. Utan den ligger notissträngen
    // kvar i live-regionen, och ett UPPREPAT för-kort försök producerar en IDENTISK sträng
    // → ingen DOM-mutation → aria-live fyrar aldrig. Seende användare ser notisen igen;
    // skärmläsaranvändaren får total tystnad efter ett tryck på primär-CTA:n (WCAG 4.1.3).
    // Det observerbara villkoret är att regionen är TOM mellan de två identiska strängarna.
    // (Fixen hade först bara "verified live" som bevis — code-reviewer påpekade att en fix
    // utan assertion är samma defektklass som resten av sessionen.)
    it("live-regionen töms vid redigering, så ett UPPREPAT för-kort försök annonseras igen", async () => {
      const user = userEvent.setup();
      setup();
      // OBS: det finns TVÅ polite live-regioner runt fältet (typeaheadens + heron:s). Ett
      // querySelector hade tagit den FÖRSTA — typeaheadens, som är tom — och gjort testet
      // vakuöst. Aggregera i stället; en selector-miss ger då "" och faller på toMatch.
      const liveRegion = () =>
        Array.from(
          document.querySelectorAll("p[role='status'][aria-live='polite']")
        )
          .map((e) => e.textContent ?? "")
          .join("");

      await user.type(screen.getByRole("combobox"), "a");
      await user.click(screen.getByRole("button", { name: /^Sök/ }));
      expect(liveRegion()).toMatch(/”a” är kortare än 2 tecken/);

      // Redigering → notisen är inaktuell → live-regionen MÅSTE tömmas.
      await user.type(screen.getByRole("combobox"), "b");
      expect(liveRegion()).toBe("");

      // Tillbaka till samma för-korta ord → samma sträng igen. Den fyrar bara om regionen
      // hann bli tom däremellan.
      await user.clear(screen.getByRole("combobox"));
      await user.type(screen.getByRole("combobox"), "a");
      await user.click(screen.getByRole("button", { name: /^Sök/ }));
      expect(liveRegion()).toMatch(/”a” är kortare än 2 tecken/);
    });
  });

  it("live-typing (mellanslag) committar UTAN commit-intent — ingen capture", async () => {
    const user = userEvent.setup();
    setup();
    // Mellanslag = live-förhandsvisning → router.replace UTAN ?commit=true
    // (annars återinförs E2i:s mellanstegsspam i Senaste sökningar).
    await user.type(screen.getByRole("combobox"), "hogia ");
    expect(replaceMock).toHaveBeenCalledWith("/jobb?q=hogia", {
      scroll: false,
    });
    expect(replaceMock).not.toHaveBeenCalledWith(
      "/jobb?q=hogia&commit=true",
      { scroll: false },
    );
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
        employmentType={[]}
        worktimeExtent={[]}
        matchGrades={[]}
        employer={undefined}
        sortBy="PublishedAtDesc"
        initialCommitted={false}
      />,
    );
    expect(input).toHaveValue("göteborg ");

    await user.clear(input);
    await user.keyboard("{Enter}");
    // Enter = commit-punkt → ?commit=true (tom sökning; backend browse-guard
    // capturerar ändå inte, men navigeringen bär commit-intent).
    expect(replaceMock).toHaveBeenLastCalledWith("/jobb?commit=true", {
      scroll: false,
    });
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
    expect(noticeLine()).toMatch(/Söktexten är full \(max 100 tecken\)/);
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
        employmentType={[]}
        worktimeExtent={[]}
        matchGrades={[]}
        employer={undefined}
        sortBy="PublishedAtDesc"
        initialCommitted={false}
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
        employmentType={[]}
        worktimeExtent={[]}
        matchGrades={[]}
        employer={undefined}
        sortBy="PublishedAtDesc"
        initialCommitted={false}
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
        employmentType={[]}
        worktimeExtent={[]}
        matchGrades={[]}
        employer={undefined}
        sortBy="PublishedAtDesc"
        initialCommitted={false}
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
        employmentType={[]}
        worktimeExtent={[]}
        matchGrades={[]}
        employer={undefined}
        sortBy="PublishedAtDesc"
        initialCommitted={false}
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
        employmentType={[]}
        worktimeExtent={[]}
        matchGrades={[]}
        employer={undefined}
        sortBy="PublishedAtDesc"
        initialCommitted={false}
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
      "/jobb?municipality=PVZL_BQT_XtL&q=volvo&commit=true",
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
      "/jobb?q=S%C3%A4ljare+G%C3%B6teborg&commit=true",
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
      "/jobb?municipality=PVZL_BQT_XtL&commit=true",
      { scroll: false },
    );
    expect(input).toHaveValue("Göteborg ");
  });
});

describe("JobbHeroSearch — ×-clear (E2j, CTO VAL 4 = semantik ii)", () => {
  it("×-knappen visas bara när det finns text", async () => {
    const user = userEvent.setup();
    setup();
    expect(
      screen.queryByRole("button", { name: "Rensa sökfältet" }),
    ).not.toBeInTheDocument();
    await user.type(screen.getByRole("combobox"), "vo");
    expect(
      screen.getByRole("button", { name: "Rensa sökfältet" }),
    ).toBeInTheDocument();
  });

  it("× rensar texten + de filter texten gjorde anspråk på (commit-intent)", async () => {
    const user = userEvent.setup();
    setup();
    // Live-commit: municipality (göteborg) + q (volvo).
    await user.type(screen.getByRole("combobox"), "göteborg volvo ");
    await user.click(
      screen.getByRole("button", { name: "Rensa sökfältet" }),
    );
    expect(screen.getByRole("combobox")).toHaveValue("");
    // Båda text-claimen borttagna; ×-clear bär commit-intent (CTO VAL 5).
    expect(replaceMock).toHaveBeenLastCalledWith("/jobb?commit=true", {
      scroll: false,
    });
  });

  it("native ::-webkit-search-cancel-button är inte den enda clear-vägen — egen knapp finns", () => {
    setup({ q: "volvo" });
    // Den kontrollerade knappen (inte native ×) bär clear-semantiken.
    expect(
      screen.getByRole("button", { name: "Rensa sökfältet" }),
    ).toHaveAttribute("type", "button");
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
    // E2j: no-JS-submit ÄR en commit → statiskt hidden commit=true så backend
    // auto-capturerar (JS-vägen interceptar submit och bär commit som suffix).
    expect(
      container.querySelector('input[type="hidden"][name="commit"]'),
    ).toHaveValue("true");
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
        employmentType={[]}
        worktimeExtent={[]}
        matchGrades={[]}
        employer={undefined}
        sortBy="PublishedAtDesc"
        initialCommitted={false}
      />,
    );
    await user.type(screen.getByRole("combobox"), "göteborg ");
    expect(replaceMock).toHaveBeenCalledWith("/jobb?q=g%C3%B6teborg", {
      scroll: false,
    });
  });
});

// #419 pt6 (Klas + CTO A1) — "Spara sökningen"-länken för typeahead-komponerade
// (icke-committade) sök. Visas när ett sparbart sök inte committats med intent;
// klick re-committar MED intent (?commit=true → backend auto-capturerar) + visar en
// kort inline-bekräftelse; länken återkommer när söket redigeras igen.
describe("JobbHeroSearch — 'Spara sökningen'-länk (#419 pt6)", () => {
  it("visar länken när sökningen är osparad (initialCommitted=false + sparbart sök)", () => {
    setup({ q: "utvecklare", initialCommitted: false });
    expect(
      screen.getByRole("button", { name: "Spara sökningen" }),
    ).toBeInTheDocument();
  });

  it("döljer länken när sökningen redan är sparad (initialCommitted=true, dvs efter Enter/Sök)", () => {
    setup({ q: "utvecklare", initialCommitted: true });
    expect(
      screen.queryByRole("button", { name: "Spara sökningen" }),
    ).toBeNull();
  });

  it("döljer länken när det inte finns något sparbart sök (tomt)", () => {
    setup({ initialCommitted: false });
    expect(
      screen.queryByRole("button", { name: "Spara sökningen" }),
    ).toBeNull();
  });

  it("klick capturerar (router.replace med commit=true) + visar bekräftelse + länken försvinner", async () => {
    const user = userEvent.setup();
    setup({ q: "utvecklare", initialCommitted: false });
    await user.click(screen.getByRole("button", { name: "Spara sökningen" }));
    expect(replaceMock).toHaveBeenCalledWith(
      expect.stringContaining("commit=true"),
      { scroll: false },
    );
    // Bekräftelsen visas (synlig span + den persistenta sr-only-annonsen bär samma
    // text → två noder; getAllByText undviker multiple-match-fel).
    expect(
      screen.getAllByText("Sökningen är sparad i Senaste sökningar").length,
    ).toBeGreaterThan(0);
    expect(
      screen.queryByRole("button", { name: "Spara sökningen" }),
    ).toBeNull();
  });

  it("länken återkommer när användaren redigerar söket efter att ha sparat", async () => {
    const user = userEvent.setup();
    setup({ q: "utvecklare", initialCommitted: false });
    await user.click(screen.getByRole("button", { name: "Spara sökningen" }));
    expect(
      screen.queryByRole("button", { name: "Spara sökningen" }),
    ).toBeNull();
    // Live-delta (typa en term + avgränsare) → osparat igen → länken åter.
    await user.type(screen.getByRole("combobox"), "göteborg ");
    expect(
      screen.getByRole("button", { name: "Spara sökningen" }),
    ).toBeInTheDocument();
  });

  it("StripCommitParam-roundtrip efter spara: länken förblir borta (own-roundtrip, ingen re-init)", async () => {
    // nextjs-ui M2 — efter spara-klicket levereras samma filter-state igen (RSC-roundtrip
    // + commit-strip). Own-roundtrip-detektorn klassar basen som EGEN och håller
    // savedByIntent stabil; initialCommitted re-läses inte (useState-init körs en gång).
    const user = userEvent.setup();
    const props = {
      taxonomy,
      q: "utvecklare",
      occupationGroup: [] as string[],
      region: [] as string[],
      municipality: [] as string[],
      employmentType: [] as string[],
      worktimeExtent: [] as string[],
      matchGrades: [] as string[],
      employer: undefined,
      sortBy: "PublishedAtDesc" as const,
      initialCommitted: false,
    };
    const { rerender } = render(<JobbHeroSearch {...props} />);
    await user.click(screen.getByRole("button", { name: "Spara sökningen" }));
    expect(
      screen.queryByRole("button", { name: "Spara sökningen" }),
    ).toBeNull();
    // Färska array-refs → sentinel-grenen körs; samma filter-state → own-roundtrip.
    rerender(
      <JobbHeroSearch
        {...props}
        occupationGroup={[]}
        region={[]}
        municipality={[]}
        employmentType={[]}
        worktimeExtent={[]}
        matchGrades={[]}
      />,
    );
    expect(
      screen.queryByRole("button", { name: "Spara sökningen" }),
    ).toBeNull();
  });
});
