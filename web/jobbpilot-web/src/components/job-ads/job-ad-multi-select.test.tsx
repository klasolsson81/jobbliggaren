import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobAdMultiSelect } from "./job-ad-multi-select";

function setup(values: string[] = []) {
  const onChange = vi.fn<(next: string[]) => void>();
  render(
    <JobAdMultiSelect
      label="Yrkesområde"
      hint="JobTech-yrkeskod"
      values={values}
      onChange={onChange}
    />
  );
  return { onChange };
}

describe("JobAdMultiSelect (ADR 0042 Beslut B)", () => {
  it("adds a valid concept-id via the Lägg till button", async () => {
    const user = userEvent.setup();
    const { onChange } = setup();

    await user.type(screen.getByLabelText("Yrkesområde"), "MVqp_eS8_kDZ");
    await user.click(screen.getByRole("button", { name: "Lägg till" }));

    expect(onChange).toHaveBeenCalledWith(["MVqp_eS8_kDZ"]);
  });

  it("rejects an invalid concept-id format and shows an error", async () => {
    const user = userEvent.setup();
    const { onChange } = setup();

    await user.type(screen.getByLabelText("Yrkesområde"), "inv alid!");
    await user.click(screen.getByRole("button", { name: "Lägg till" }));

    expect(screen.getByRole("alert")).toHaveTextContent(
      /1–32 tecken/
    );
    expect(onChange).not.toHaveBeenCalled();
  });

  it("renders selected values as removable chips", async () => {
    const user = userEvent.setup();
    const { onChange } = setup(["a1", "b2"]);

    expect(screen.getByText("a1")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Ta bort a1" }));

    expect(onChange).toHaveBeenCalledWith(["b2"]);
  });

  it("blocks adding past the 10-value cap and disables input", () => {
    const ten = Array.from({ length: 10 }, (_, i) => `code${i}`);
    setup(ten);
    expect(screen.getByLabelText("Yrkesområde")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Lägg till" })).toBeDisabled();
    expect(screen.getByText(/Max 10 val tillagda/)).toBeInTheDocument();
  });

  it("rejects a duplicate value", async () => {
    const user = userEvent.setup();
    const { onChange } = setup(["a1"]);

    await user.type(screen.getByLabelText("Yrkesområde"), "a1");
    await user.click(screen.getByRole("button", { name: "Lägg till" }));

    expect(screen.getByRole("alert")).toHaveTextContent(/redan tillagd/);
    expect(onChange).not.toHaveBeenCalled();
  });
});
