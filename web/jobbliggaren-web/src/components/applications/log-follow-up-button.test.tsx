import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { LogFollowUpButton } from "./log-follow-up-button";

const logFollowUpAction = vi.hoisted(() =>
  vi.fn(async () => ({ success: true as const })),
);
vi.mock("@/lib/actions/applications", () => ({
  logFollowUpAction,
}));

describe("LogFollowUpButton", () => {
  it.each(["Escape", "Avbryt"] as const)(
    "returns focus to the button after %s",
    async (close) => {
      const user = userEvent.setup();
      render(
        <LogFollowUpButton
          applicationId="11111111-2222-3333-4444-555555555555"
          contextTitle="Backend-utvecklare"
          contextCompany="Volvo"
          toastCompany="Volvo"
        />,
      );
      const opener = screen.getByRole("button", { name: "Logga uppföljning" });
      await user.click(opener);
      const dialog = await screen.findByRole("dialog", {
        name: "Logga uppföljning",
      });

      if (close === "Escape") await user.keyboard("{Escape}");
      else await user.click(within(dialog).getByRole("button", { name: close }));

      await waitFor(() => expect(opener).toHaveFocus());
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(logFollowUpAction).not.toHaveBeenCalled();
    },
  );
});
