import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { TaxonomyRegion } from "@/lib/dto/taxonomy";
import { RegionMunicipalityCascade } from "./region-municipality-cascade";
import type { OrtSelection } from "@/lib/job-ads/ort-selection";

const regions: ReadonlyArray<TaxonomyRegion> = [
  {
    conceptId: "r_sthlm",
    label: "Stockholms län",
    municipalities: [
      { conceptId: "m_sthlm", label: "Stockholm" },
      { conceptId: "m_solna", label: "Solna" },
    ],
  },
  {
    conceptId: "r_vg",
    label: "Västra Götalands län",
    municipalities: [{ conceptId: "m_gbg", label: "Göteborg" }],
  },
];

function renderCascade(
  overrides?: Partial<React.ComponentProps<typeof RegionMunicipalityCascade>>
) {
  const onChange = vi.fn<(next: OrtSelection) => void>();
  render(
    <RegionMunicipalityCascade
      regions={regions}
      selectedRegions={[]}
      selectedMunicipalities={[]}
      onChange={onChange}
      idPrefix="test"
      {...overrides}
    />
  );
  return { onChange };
}

describe("RegionMunicipalityCascade — dual-axis (Spår 3 PR-D)", () => {
  it("'Hela länet' togglar länets concept-id i region-axeln (ETT id, ingen kommun)", async () => {
    const user = userEvent.setup();
    const { onChange } = renderCascade();

    await user.click(screen.getByRole("button", { name: "Lägg till orter" }));
    await user.click(screen.getByRole("button", { name: "Stockholms län" }));
    await user.click(
      screen.getByRole("checkbox", { name: "Hela Stockholms län" })
    );

    expect(onChange).toHaveBeenCalledWith({
      region: ["r_sthlm"],
      municipality: [],
    });
  });

  it("kommun-klick togglar kommun-axeln (inte region)", async () => {
    const user = userEvent.setup();
    const { onChange } = renderCascade();

    await user.click(screen.getByRole("button", { name: "Lägg till orter" }));
    await user.click(screen.getByRole("button", { name: "Stockholms län" }));
    await user.click(screen.getByRole("checkbox", { name: "Solna" }));

    expect(onChange).toHaveBeenCalledWith({
      region: [],
      municipality: ["m_solna"],
    });
  });

  it("kommun som kompletterar länets ALLA kommuner kollapsar till region-id", async () => {
    const user = userEvent.setup();
    const { onChange } = renderCascade({ selectedMunicipalities: ["m_sthlm"] });

    await user.click(screen.getByRole("button", { name: "Lägg till orter" }));
    await user.click(screen.getByRole("button", { name: "Stockholms län" }));
    // Stockholm redan vald → klicka Solna kompletterar alla 2 kommuner.
    await user.click(screen.getByRole("checkbox", { name: "Solna" }));

    expect(onChange).toHaveBeenCalledWith({
      region: ["r_sthlm"],
      municipality: [],
    });
  });

  it("seedar pinnade chips ur valda län OCH kommuner (län först)", () => {
    renderCascade({
      selectedRegions: ["r_vg"],
      selectedMunicipalities: ["m_solna"],
    });
    const pinned = screen.getByRole("list", { name: "Valda orter" });
    expect(
      within(pinned).getByRole("button", { name: "Ta bort Västra Götalands län" })
    ).toBeInTheDocument();
    expect(
      within(pinned).getByRole("button", { name: "Ta bort Solna" })
    ).toBeInTheDocument();
  });

  it("ta bort en läns-chip rensar hela länets kolumn (region + ev. kommuner)", async () => {
    const user = userEvent.setup();
    const { onChange } = renderCascade({
      selectedRegions: ["r_sthlm"],
      selectedMunicipalities: ["m_gbg"],
    });

    await user.click(
      screen.getByRole("button", { name: "Ta bort Stockholms län" })
    );

    // Stockholms läns region-id bort; Göteborg (annat län) orört.
    expect(onChange).toHaveBeenCalledWith({
      region: [],
      municipality: ["m_gbg"],
    });
  });

  it("kommun-filter smalnar av listan tvärs över alla län", async () => {
    const user = userEvent.setup();
    renderCascade();

    await user.click(screen.getByRole("button", { name: "Lägg till orter" }));
    await user.type(
      screen.getByRole("textbox", { name: "Filtrera kommuner" }),
      "göte"
    );

    expect(screen.getByRole("checkbox", { name: "Göteborg" })).toBeInTheDocument();
    expect(screen.queryByRole("checkbox", { name: "Solna" })).toBeNull();
  });

  it("vänsterkolumnen är en knapp-grupp (ingen falsk listbox-roll)", async () => {
    const user = userEvent.setup();
    renderCascade();

    await user.click(screen.getByRole("button", { name: "Lägg till orter" }));

    // Idiom-fix (CTO-verdikt 2026-06-22): län-raderna NAVIGERAR aktiv grupp
    // → riktiga <button>, ingen role="listbox"/option-claim som lovar
    // piltangenter vi aldrig hade.
    expect(screen.queryByRole("listbox")).toBeNull();
    expect(screen.queryByRole("option")).toBeNull();
    expect(
      screen.getByRole("button", { name: "Stockholms län" })
    ).toBeInTheDocument();
  });

  it("län-raden är tangentbordsaktiverbar (Tab-fokus + Enter)", async () => {
    const user = userEvent.setup();
    const { onChange } = renderCascade();

    await user.click(screen.getByRole("button", { name: "Lägg till orter" }));

    const lan = screen.getByRole("button", { name: "Stockholms län" });
    lan.focus();
    expect(lan).toHaveFocus();
    // Native <button> aktiveras av Enter utan egen onKeyDown → kommun-kolumnen
    // avslöjas (aria-pressed sätts). Inget värde committas av navigeringen.
    await user.keyboard("{Enter}");
    expect(lan).toHaveAttribute("aria-pressed", "true");
    expect(onChange).not.toHaveBeenCalled();
    expect(
      screen.getByRole("checkbox", { name: "Hela Stockholms län" })
    ).toBeInTheDocument();
  });
});

describe("#551 punkt 4 — Distans som eget ort-val", () => {
  it("raden renderas INTE när ytan saknar distans-axeln (remote utelämnad)", async () => {
    const user = userEvent.setup();
    renderCascade();
    await user.click(screen.getByRole("button", { name: "Lägg till orter" }));
    expect(screen.queryByRole("checkbox", { name: "Distans" })).toBeNull();
  });

  it("raden renderas när ytan bär axeln, även när den är av", async () => {
    const user = userEvent.setup();
    renderCascade({ remote: false });
    await user.click(screen.getByRole("button", { name: "Lägg till orter" }));
    expect(
      screen.getByRole("checkbox", { name: "Distans" })
    ).toHaveAttribute("aria-checked", "false");
  });

  it("klick slår PÅ distans utan att röra de två id-axlarna", async () => {
    const user = userEvent.setup();
    const { onChange } = renderCascade({
      remote: false,
      selectedRegions: ["r_vg"],
      selectedMunicipalities: ["m_solna"],
    });
    await user.click(screen.getByRole("button", { name: "Lägg till orter" }));
    await user.click(screen.getByRole("checkbox", { name: "Distans" }));
    expect(onChange).toHaveBeenCalledWith({
      region: ["r_vg"],
      municipality: ["m_solna"],
      remote: true,
    });
  });

  // DEN här är hela skälet till att distans ligger UTANFÖR OrtSelection och bärs
  // av en commit-wrapper: en normaliserar-retur bär bara {region, municipality},
  // så utan wrappern hade varje kommun-klick tyst släckt användarens distans-val
  // — ett fel typsystemet inte kan se, eftersom fälten är oberoende.
  it("ett kommun-klick BÄR distans-valet vidare oförändrat", async () => {
    const user = userEvent.setup();
    const { onChange } = renderCascade({ remote: true });
    await user.click(screen.getByRole("button", { name: "Lägg till orter" }));
    await user.click(screen.getByRole("button", { name: "Stockholms län" }));
    await user.click(screen.getByRole("checkbox", { name: "Solna" }));
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ municipality: ["m_solna"], remote: true })
    );
  });

  it("ett helläns-klick BÄR distans-valet vidare oförändrat", async () => {
    const user = userEvent.setup();
    const { onChange } = renderCascade({ remote: true });
    await user.click(screen.getByRole("button", { name: "Lägg till orter" }));
    await user.click(screen.getByRole("button", { name: "Stockholms län" }));
    await user.click(
      screen.getByRole("checkbox", { name: "Hela Stockholms län" })
    );
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ region: ["r_sthlm"], remote: true })
    );
  });

  it("Distans visas som en chip och kan tas bort därifrån", async () => {
    const user = userEvent.setup();
    const { onChange } = renderCascade({ remote: true });
    const chips = screen.getByLabelText("Valda orter");
    expect(within(chips).getByText("Distans")).toBeInTheDocument();
    await user.click(within(chips).getByRole("button"));
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ remote: false })
    );
  });

  it("Rensa nollar distans tillsammans med de två id-axlarna", async () => {
    const user = userEvent.setup();
    const { onChange } = renderCascade({
      remote: true,
      selectedRegions: ["r_vg"],
    });
    await user.click(screen.getByRole("button", { name: "Rensa" }));
    expect(onChange).toHaveBeenCalledWith({
      region: [],
      municipality: [],
      remote: false,
    });
  });

  it("en yta UTAN axeln får aldrig ett remote-fält i sin commit", async () => {
    const user = userEvent.setup();
    const { onChange } = renderCascade();
    await user.click(screen.getByRole("button", { name: "Lägg till orter" }));
    await user.click(screen.getByRole("button", { name: "Stockholms län" }));
    await user.click(screen.getByRole("checkbox", { name: "Solna" }));
    const arg = onChange.mock.calls[0]?.[0];
    expect(arg).toBeDefined();
    expect(Object.hasOwn(arg!, "remote")).toBe(false);
  });
});
