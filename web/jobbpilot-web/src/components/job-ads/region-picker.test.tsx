import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RegionPicker } from "./region-picker";
import type { TaxonomyRegion } from "@/lib/dto/taxonomy";

const regions: TaxonomyRegion[] = [
  { conceptId: "CifL_Rzy_Mku", label: "Stockholms län" },
  { conceptId: "oDpK_oQy_3Zc", label: "Västra Götalands län" },
  { conceptId: "zBon_yRC_nABC", label: "Skåne län" },
];

function setup(
  values: string[] = [],
  resolvedLabels = new Map<string, string>()
) {
  const onChange = vi.fn<(next: string[]) => void>();
  render(
    <RegionPicker
      regions={regions}
      values={values}
      onChange={onChange}
      resolvedLabels={resolvedLabels}
    />
  );
  return { onChange };
}

describe("RegionPicker (ADR 0043 — Län, enkelnivå, namn ej kod)", () => {
  it("selects a län by name and emits its concept-id", async () => {
    const user = userEvent.setup();
    const { onChange } = setup();

    await user.selectOptions(screen.getByLabelText("Län"), "CifL_Rzy_Mku");

    expect(onChange).toHaveBeenCalledWith(["CifL_Rzy_Mku"]);
  });

  it("renders selected values as NAME chips, never concept-id", () => {
    setup(["CifL_Rzy_Mku"]);
    expect(screen.getByText("Stockholms län")).toBeInTheDocument();
    expect(screen.queryByText("CifL_Rzy_Mku")).not.toBeInTheDocument();
  });

  it("removes a chip via its dismiss button", async () => {
    const user = userEvent.setup();
    const { onChange } = setup(["CifL_Rzy_Mku", "oDpK_oQy_3Zc"]);

    await user.click(
      screen.getByRole("button", { name: "Ta bort Stockholms län" })
    );

    expect(onChange).toHaveBeenCalledWith(["oDpK_oQy_3Zc"]);
  });

  it("hides already-selected options from the select", () => {
    setup(["CifL_Rzy_Mku"]);
    expect(
      screen.queryByRole("option", { name: "Stockholms län" })
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole("option", { name: "Skåne län" })
    ).toBeInTheDocument();
  });

  it("blocks adding past the 10-value cap and disables the select", () => {
    const ten = Array.from({ length: 10 }, (_, i) => `code_${i}`);
    setup(ten);
    expect(screen.getByLabelText("Län")).toBeDisabled();
    expect(screen.getByText(/Du har valt 10 län \(max\)/)).toBeInTheDocument();
  });

  it("falls back to the reverse-lookup label for an id not in the tree", () => {
    setup(["GONE_id"], new Map([["GONE_id", "Tidigare länsnamn"]]));
    expect(screen.getByText("Tidigare länsnamn")).toBeInTheDocument();
  });

  it("falls back to 'Okänd kod' when neither tree nor reverse-lookup has the id", () => {
    setup(["MYSTERY"]);
    expect(screen.getByText("Okänd kod (MYSTERY)")).toBeInTheDocument();
  });
});
