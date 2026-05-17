import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OccupationPicker } from "./occupation-picker";
import type { TaxonomyOccupationField } from "@/lib/dto/taxonomy";

const fields: TaxonomyOccupationField[] = [
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
];

function setup(
  values: string[] = [],
  resolvedLabels = new Map<string, string>()
) {
  const onChange = vi.fn<(next: string[]) => void>();
  render(
    <OccupationPicker
      occupationFields={fields}
      values={values}
      onChange={onChange}
      resolvedLabels={resolvedLabels}
    />
  );
  return { onChange };
}

describe("OccupationPicker (ADR 0043 — Yrkesområde→Yrke, namn ej kod)", () => {
  it("disables the Yrke select until a Yrkesområde is chosen", async () => {
    const user = userEvent.setup();
    setup();

    expect(screen.getByLabelText("Yrke")).toBeDisabled();

    await user.selectOptions(
      screen.getByLabelText("Yrkesområde"),
      "apaJ_2ja_LuF"
    );

    expect(screen.getByLabelText("Yrke")).not.toBeDisabled();
  });

  it("picks an occupation by name and emits its concept-id", async () => {
    const user = userEvent.setup();
    const { onChange } = setup();

    await user.selectOptions(
      screen.getByLabelText("Yrkesområde"),
      "apaJ_2ja_LuF"
    );
    await user.selectOptions(screen.getByLabelText("Yrke"), "MVqp_eS8_kDZ");

    expect(onChange).toHaveBeenCalledWith(["MVqp_eS8_kDZ"]);
  });

  it("renders selected occupations as NAME chips, never concept-id", () => {
    setup(["MVqp_eS8_kDZ"]);
    expect(screen.getByText("Systemutvecklare")).toBeInTheDocument();
    expect(screen.queryByText("MVqp_eS8_kDZ")).not.toBeInTheDocument();
  });

  it("resolves a chip name even when its field is not the active one", () => {
    // Yrke från Bygg-fältet valt men inget yrkesområde aktivt — namnet ska
    // ändå slås upp över hela trädet.
    setup(["Z9zz_zzz_zzz"]);
    expect(screen.getByText("Snickare")).toBeInTheDocument();
  });

  it("removes a chip via its dismiss button", async () => {
    const user = userEvent.setup();
    const { onChange } = setup(["MVqp_eS8_kDZ", "Q5DF_juj_8do"]);

    await user.click(
      screen.getByRole("button", { name: "Ta bort Systemutvecklare" })
    );

    expect(onChange).toHaveBeenCalledWith(["Q5DF_juj_8do"]);
  });

  it("hides an already-selected occupation from its field's options", async () => {
    const user = userEvent.setup();
    setup(["MVqp_eS8_kDZ"]);

    await user.selectOptions(
      screen.getByLabelText("Yrkesområde"),
      "apaJ_2ja_LuF"
    );

    expect(
      screen.queryByRole("option", { name: "Systemutvecklare" })
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole("option", { name: "Mjukvaruarkitekt" })
    ).toBeInTheDocument();
  });

  it("blocks adding past the 10-value cap", () => {
    const ten = Array.from({ length: 10 }, (_, i) => `code_${i}`);
    setup(ten);
    expect(
      screen.getByText(/Du har valt 10 yrken \(max\)/)
    ).toBeInTheDocument();
  });

  it("falls back to the reverse-lookup label for a saved id not in the tree", () => {
    setup(["GONE_occ"], new Map([["GONE_occ", "Tidigare yrkesnamn"]]));
    expect(screen.getByText("Tidigare yrkesnamn")).toBeInTheDocument();
  });

  it("falls back to 'Okänd kod' when neither source has the id", () => {
    setup(["MYSTERY"]);
    expect(screen.getByText("Okänd kod (MYSTERY)")).toBeInTheDocument();
  });
});
