import { Component, type ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AdminConfirmDialog } from "./admin-confirm-dialog";

function dialog(onConfirm: () => Promise<string | null>, onCancel = vi.fn()) {
  return (
    <AdminConfirmDialog
      open
      title="Suspendera konto.a@example.test?"
      body="Kontot låses och alla sessioner loggas ut."
      confirmLabel="Suspendera konto"
      busyLabel="Suspenderar…"
      onConfirm={onConfirm}
      onCancel={onCancel}
    />
  );
}

function renderDialog(onConfirm: () => Promise<string | null>) {
  const onCancel = vi.fn();
  render(dialog(onConfirm, onCancel));
  return { onCancel };
}

class Boundary extends Component<{ readonly children: ReactNode }, { readonly failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() {
    return { failed: true };
  }
  render() {
    return this.state.failed ? <p>Något gick fel.</p> : this.props.children;
  }
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe("AdminConfirmDialog (DESIGN.md §6)", () => {
  it("is an alert dialog named by its question and described by its effect, starting on Avbryt", async () => {
    renderDialog(async () => null);

    const confirmation = screen.getByRole("alertdialog", {
      name: "Suspendera konto.a@example.test?",
      description: "Kontot låses och alla sessioner loggas ut.",
    });
    expect(confirmation).toBeInTheDocument();
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

  it("names the running command on its button, keeps both choices disabled and ignores Escape", async () => {
    let settle: (refusal: string | null) => void = () => {};
    const { onCancel } = renderDialog(() => new Promise((resolve) => (settle = resolve)));

    await userEvent.click(screen.getByRole("button", { name: "Suspendera konto" }));
    expect(screen.getByRole("button", { name: "Avbryt" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Suspenderar…" })).toBeDisabled();
    await userEvent.keyboard("{Escape}");
    expect(onCancel).not.toHaveBeenCalled();

    settle(null);
    await waitFor(() => expect(screen.getByRole("button", { name: "Suspendera konto" })).toBeEnabled());
  });

  it("shows a refusal as an alert that takes focus, and clears it when the action is tried again", async () => {
    let settle: (refusal: string | null) => void = () => {};
    renderDialog(() => new Promise((resolve) => (settle = resolve)));

    await userEvent.click(screen.getByRole("button", { name: "Suspendera konto" }));
    settle("Du kan inte suspendera ditt eget konto.");
    const refusal = await screen.findByRole("alert");
    expect(refusal).toHaveTextContent("Du kan inte suspendera ditt eget konto.");
    await waitFor(() => expect(refusal).toHaveFocus());

    await userEvent.click(screen.getByRole("button", { name: "Suspendera konto" }));
    expect(screen.queryByRole("alert")).toBeNull();

    // React entangles pending async actions, so a test leaves none behind.
    settle(null);
    await waitFor(() => expect(screen.getByRole("button", { name: "Suspendera konto" })).toBeEnabled());
  });

  it("hands a command that throws to the nearest error boundary instead of holding the dialog open", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    render(<Boundary>{dialog(async () => Promise.reject(new Error("network")))}</Boundary>);

    await userEvent.click(screen.getByRole("button", { name: "Suspendera konto" }));
    expect(await screen.findByText("Något gick fel.")).toBeInTheDocument();
    expect(screen.queryByRole("alertdialog")).toBeNull();
  });
});
