import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { ApplicationLoadError } from "./application-load-error";

describe("ApplicationLoadError (#1827 M6)", () => {
  it("announces the failure with its title and its body", () => {
    render(
      <ApplicationLoadError
        title="Kunde inte ladda ansökan"
        body="Ett tekniskt fel uppstod. Försök ladda om sidan om en stund."
      />,
    );

    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Kunde inte ladda ansökan");
    expect(alert).toHaveTextContent(
      "Ett tekniskt fel uppstod. Försök ladda om sidan om en stund.",
    );
  });
});
