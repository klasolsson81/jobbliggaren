import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DeleteApplicationButton } from "./delete-application-button";
import type { ActionResult } from "@/lib/actions/_action-result";

const deleteApplicationActionMock =
  vi.fn<(id: string) => Promise<ActionResult>>();
const pushMock = vi.fn();

vi.mock("@/lib/actions/applications", () => ({
  deleteApplicationAction: (id: string) => deleteApplicationActionMock(id),
}));
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
}));

describe("DeleteApplicationButton (#782 — detalj-footer)", () => {
  beforeEach(() => {
    deleteApplicationActionMock.mockReset();
    deleteApplicationActionMock.mockResolvedValue({ success: true });
    pushMock.mockReset();
  });

  it("navigerar till listan efter lyckad radering (raden man står på är död)", async () => {
    const user = userEvent.setup();
    render(<DeleteApplicationButton applicationId="app-1" />);

    await user.click(screen.getByRole("button", { name: "Radera ansökan" }));
    await user.click(
      within(await screen.findByRole("dialog")).getByRole("button", {
        name: "Radera ansökan",
      }),
    );

    await waitFor(() =>
      expect(deleteApplicationActionMock).toHaveBeenCalledWith("app-1")
    );
    expect(pushMock).toHaveBeenCalledWith("/ansokningar");
  });

  it("navigerar INTE vid misslyckad radering", async () => {
    deleteApplicationActionMock.mockResolvedValue({
      success: false,
      error: "Det gick inte att radera ansökan. Försök igen.",
    });
    const user = userEvent.setup();
    render(<DeleteApplicationButton applicationId="app-1" />);

    await user.click(screen.getByRole("button", { name: "Radera ansökan" }));
    await user.click(
      within(await screen.findByRole("dialog")).getByRole("button", {
        name: "Radera ansökan",
      }),
    );

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(pushMock).not.toHaveBeenCalled();
  });

  it.each(["Escape", "Avbryt"] as const)(
    "returns focus to the delete button after %s",
    async (close) => {
      const user = userEvent.setup();
      render(<DeleteApplicationButton applicationId="app-1" />);
      const opener = screen.getByRole("button", { name: "Radera ansökan" });
      await user.click(opener);
      const dialog = await screen.findByRole("dialog");

      if (close === "Escape") await user.keyboard("{Escape}");
      else await user.click(within(dialog).getByRole("button", { name: close }));

      await waitFor(() => expect(opener).toHaveFocus());
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(deleteApplicationActionMock).not.toHaveBeenCalled();
    },
  );
});
