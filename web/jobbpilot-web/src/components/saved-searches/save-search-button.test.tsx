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
    render(<SaveSearchButton ssyk="" region="" q="" sortBy="PublishedAtDesc" />);
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
      <SaveSearchButton ssyk="" region="" q="java" sortBy="PublishedAtDesc" />
    );
    await user.click(screen.getByRole("button", { name: "Spara sökning" }));
    expect(
      screen.getByLabelText("Namn på sökningen")
    ).toBeInTheDocument();
  });

  it("carries the current filter values as hidden inputs", async () => {
    const user = userEvent.setup();
    const { container } = render(
      <SaveSearchButton
        ssyk="MVqp_eS8_kDZ"
        region=""
        q="java"
        sortBy="ExpiresAtAsc"
      />
    );
    await user.click(screen.getByRole("button", { name: "Spara sökning" }));

    expect(
      container.querySelector('input[name="ssyk"]')
    ).toHaveValue("MVqp_eS8_kDZ");
    expect(container.querySelector('input[name="q"]')).toHaveValue("java");
    expect(
      container.querySelector('input[name="sortBy"]')
    ).toHaveValue("ExpiresAtAsc");
  });
});
