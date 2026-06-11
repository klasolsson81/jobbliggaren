import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobbHeroSearch } from "./jobb-hero-search";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";

const pushMock = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
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

/** Stubbar suggest-endpointen med en fast förslagslista (wire-form: kind=int). */
function stubSuggest(items: Array<{ kind: number; conceptId: string | null; label: string }>) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async () => new Response(JSON.stringify(items), { status: 200 })),
  );
}

beforeEach(() => pushMock.mockClear());
afterEach(() => vi.unstubAllGlobals());

describe("JobbHeroSearch — fri text (residual-q)", () => {
  it("submit av fri söktext → q i URL:en", async () => {
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("combobox"), "volvo");
    await user.click(screen.getByRole("button", { name: /Sök/ }));

    expect(pushMock).toHaveBeenCalledWith("/jobb?q=volvo");
  });

  it("submit bevarar aktiva dimensioner (param-bevarande)", async () => {
    const user = userEvent.setup();
    setup({ occupationGroup: ["MVqp_eS8_kDZ"] });
    await user.type(screen.getByRole("combobox"), "volvo");
    await user.click(screen.getByRole("button", { name: /Sök/ }));

    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?occupationGroup=MVqp_eS8_kDZ&q=volvo",
    );
  });
});

describe("JobbHeroSearch — typeahead-chip-komponist", () => {
  it("Kommun-förslag → municipality-chip i URL:en (ej q)", async () => {
    stubSuggest([{ kind: 2, conceptId: "PVZL_BQT_XtL", label: "Göteborg" }]);
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("combobox"), "göte");
    await user.click(
      await screen.findByRole("option", { name: "Göteborg" }, { timeout: 2000 }),
    );

    expect(pushMock).toHaveBeenCalledWith("/jobb?municipality=PVZL_BQT_XtL");
  });

  it("Yrkesområde-förslag → materialiserar barn-yrkesgrupper (VAL 2a)", async () => {
    stubSuggest([{ kind: 3, conceptId: "apaJ_2ja_LuF", label: "Data/IT" }]);
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("combobox"), "data");
    await user.click(
      await screen.findByRole("option", { name: "Data/IT" }, { timeout: 2000 }),
    );

    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?occupationGroup=MVqp_eS8_kDZ&occupationGroup=Q5DF_juj_8do",
    );
  });

  it("Title-förslag → q (fri text), inte en dimension", async () => {
    stubSuggest([{ kind: 0, conceptId: null, label: "AI engineer" }]);
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("combobox"), "ai");
    await user.click(
      await screen.findByRole(
        "option",
        { name: "AI engineer" },
        { timeout: 2000 },
      ),
    );

    expect(pushMock).toHaveBeenCalledWith("/jobb?q=AI+engineer");
  });

  it("dimension-chip läggs ovanpå committad q (chips=AND, residual bevaras)", async () => {
    stubSuggest([{ kind: 2, conceptId: "PVZL_BQT_XtL", label: "Göteborg" }]);
    const user = userEvent.setup();
    // committad q = "volvo" (ur URL/props); väljer Göteborg-chip.
    setup({ q: "volvo" });
    await user.clear(screen.getByRole("combobox"));
    await user.type(screen.getByRole("combobox"), "göte");
    await user.click(
      await screen.findByRole("option", { name: "Göteborg" }, { timeout: 2000 }),
    );

    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?municipality=PVZL_BQT_XtL&q=volvo",
    );
  });
});

describe("JobbHeroSearch — fält-synk mot URL (E2g-principen)", () => {
  it("fältet visar committad q och speglar extern URL-ändring", () => {
    const { rerender } = setup({ q: "systemutvecklare" });
    expect(screen.getByRole("combobox")).toHaveValue("systemutvecklare");

    // Extern ändring (recent-navigering / Rensa alla filter) → nya props.
    rerender(
      <JobbHeroSearch
        taxonomy={taxonomy}
        q="sjuksköterska"
        occupationGroup={[]}
        region={[]}
        municipality={[]}
        sortBy="PublishedAtDesc"
      />,
    );
    expect(screen.getByRole("combobox")).toHaveValue("sjuksköterska");
  });
});

describe("JobbHeroSearch — no-JS GET-form-fallback", () => {
  it("renderar en äkta GET-form med q + hidden inputs för aktiva filter", () => {
    const { container } = setup({
      occupationGroup: ["MVqp_eS8_kDZ"],
      region: ["CifL_Rzy_Mku"],
    });
    const form = container.querySelector("form");
    expect(form).toHaveAttribute("action", "/jobb");
    expect(form).toHaveAttribute("method", "get");
    expect(screen.getByRole("combobox")).toHaveAttribute("name", "q");
    // Hidden inputs bär dimensionerna för native submit utan JS.
    expect(
      container.querySelector('input[type="hidden"][name="occupationGroup"]'),
    ).toHaveValue("MVqp_eS8_kDZ");
    expect(
      container.querySelector('input[type="hidden"][name="region"]'),
    ).toHaveValue("CifL_Rzy_Mku");
  });
});
