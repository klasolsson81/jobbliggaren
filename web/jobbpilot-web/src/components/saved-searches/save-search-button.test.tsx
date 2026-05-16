import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SaveSearchButton } from "./save-search-button";
import type { ActionResult } from "@/lib/actions/saved-searches";

const createMock =
  vi.fn<
    (prev: ActionResult | null, fd: FormData) => Promise<ActionResult>
  >();

vi.mock("@/lib/actions/saved-searches", () => ({
  createSavedSearchAction: (prev: ActionResult | null, fd: FormData) =>
    createMock(prev, fd),
}));

describe("SaveSearchButton", () => {
  beforeEach(() => {
    createMock.mockReset();
    createMock.mockResolvedValue({ success: true });
  });

  it("disables save and shows guidance when no criteria are set", () => {
    render(
      <SaveSearchButton ssyk={[]} region={[]} q="" sortBy="PublishedAtDesc" />
    );
    expect(
      screen.getByRole("button", { name: "Spara sökning" })
    ).toBeDisabled();
    expect(
      screen.getByText(/Lägg till minst ett filter/)
    ).toBeInTheDocument();
  });

  it("opens the name form when criteria are present", async () => {
    const user = userEvent.setup();
    render(
      <SaveSearchButton
        ssyk={[]}
        region={[]}
        q="java"
        sortBy="PublishedAtDesc"
      />
    );
    await user.click(screen.getByRole("button", { name: "Spara sökning" }));
    expect(
      screen.getByLabelText("Namn på sökningen")
    ).toBeInTheDocument();
  });

  it("enables save when only multi ssyk is set (ADR 0042 Beslut B)", () => {
    render(
      <SaveSearchButton
        ssyk={["MVqp_eS8_kDZ"]}
        region={[]}
        q=""
        sortBy="PublishedAtDesc"
      />
    );
    expect(
      screen.getByRole("button", { name: "Spara sökning" })
    ).toBeEnabled();
  });

  it("carries multi ssyk/region as one hidden input per element", async () => {
    const user = userEvent.setup();
    const { container } = render(
      <SaveSearchButton
        ssyk={["MVqp_eS8_kDZ", "CifL_Rzy_Mku"]}
        region={["r1"]}
        q="java"
        sortBy="ExpiresAtAsc"
      />
    );
    await user.click(screen.getByRole("button", { name: "Spara sökning" }));

    const ssykInputs = container.querySelectorAll('input[name="ssyk"]');
    expect(ssykInputs).toHaveLength(2);
    expect(ssykInputs[0]).toHaveValue("MVqp_eS8_kDZ");
    expect(ssykInputs[1]).toHaveValue("CifL_Rzy_Mku");
    expect(
      container.querySelectorAll('input[name="region"]')
    ).toHaveLength(1);
    expect(container.querySelector('input[name="q"]')).toHaveValue("java");
    expect(
      container.querySelector('input[name="sortBy"]')
    ).toHaveValue("ExpiresAtAsc");
  });
});
