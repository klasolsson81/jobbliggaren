import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AdminConfirmDialog } from "./admin-confirm-dialog";

function renderDialog(onConfirm: () => Promise<string | null>) {
  const onCancel = vi.fn();
  render(
    <AdminConfirmDialog
      open
      title="Suspendera konto.a@example.test?"
      body="Kontot låses och alla sessioner loggas ut."
      confirmLabel="Suspendera konto"
      onConfirm={onConfirm}
      onCancel={onCancel}
    />,
  );
  return { onCancel };
}

describe("AdminConfirmDialog (DESIGN.md §6)", () => {
  it("is an alert dialog named by its question and described by its effect, starting on Avbryt", async () => {
    renderDialog(async () => null);

    const dialog = screen.getByRole("alertdialog", {
      name: "Suspendera konto.a@example.test?",
      description: "Kontot låses och alla sessioner loggas ut.",
    });
    expect(dialog).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("button", { name: "Avbryt" })).toHaveFocus());
  });

  it("cancels from Avbryt and from Escape without running the action", async () => {
    const onConfirm = vi.fn(async () => null);
    const { onCancel } = renderDialog(onConfirm);

    await userEvent.click(screen.getByRole("button", { name: "Avbryt" }));
    expect(onCancel).toHaveBeenCalledTimes(1);
    await userEvent.keyboard("{Escape}");
    expect(onCancel).toHaveBeenCalledTimes(2);
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it("disables both choices and ignores Escape while the action is running", async () => {
    let settle: (refusal: string | null) => void = () => {};
    const { onCancel } = renderDialog(() => new Promise((resolve) => (settle = resolve)));

    await userEvent.click(screen.getByRole("button", { name: "Suspendera konto" }));
    expect(screen.getByRole("button", { name: "Avbryt" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Suspendera konto" })).toBeDisabled();
    await userEvent.keyboard("{Escape}");
    expect(onCancel).not.toHaveBeenCalled();

    settle(null);
    await waitFor(() => expect(screen.getByRole("button", { name: "Suspendera konto" })).toBeEnabled());
  });

  it("shows a refusal as an alert in the dialog and clears it when the action is tried again", async () => {
    let settle: (refusal: string | null) => void = () => {};
    renderDialog(() => new Promise((resolve) => (settle = resolve)));

    await userEvent.click(screen.getByRole("button", { name: "Suspendera konto" }));
    settle("Du kan inte göra det med ditt eget konto.");
    expect(await screen.findByRole("alert")).toHaveTextContent("Du kan inte göra det med ditt eget konto.");

    await userEvent.click(screen.getByRole("button", { name: "Suspendera konto" }));
    expect(screen.queryByRole("alert")).toBeNull();
  });
});
