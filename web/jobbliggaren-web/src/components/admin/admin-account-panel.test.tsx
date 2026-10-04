import { afterEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AdminAccountDetail } from "@/lib/admin/view-models";
import { dismissAdminToast, getAdminToastSnapshot, showAdminToast } from "@/lib/admin/toast-store";
import { AdminToastHost } from "./admin-toast-host";
import {
  AdminAccountPanel,
  type AdminAccountCommand,
  type AdminCommandRefusal,
  type AdminLiveAction,
} from "./admin-account-panel";

const ACTIVE: AdminAccountDetail = {
  id: "a",
  email: "konto.a@example.test",
  role: "user",
  status: "active",
  registeredAt: "2026-09-28T12:02:00Z",
  applicationCount: 4,
  deletionEarliest: null,
  savedSearchCount: 3,
  resumeCount: 1,
};

const ALL_LIVE: ReadonlySet<AdminLiveAction> = new Set(["changeEmail", "suspend", "reinstate", "scheduleDeletion"]);

function renderPanel(
  account: AdminAccountDetail | null,
  onCommand: (account: AdminAccountDetail, command: AdminAccountCommand) => Promise<AdminCommandRefusal> = async () => null,
  live: ReadonlySet<AdminLiveAction> = ALL_LIVE,
) {
  const onClose = vi.fn();
  render(
    <AdminAccountPanel
      account={account}
      onClose={onClose}
      live={live}
      onCommand={onCommand}
      deletionEarliestIfScheduledNow="2026-11-03T08:00:00Z"
    />,
  );
  return { onClose };
}

function actionNames() {
  return within(screen.getByRole("region", { name: "Åtgärder" }))
    .getAllByRole("button")
    .map((button) => button.textContent);
}

afterEach(() => {
  const toast = getAdminToastSnapshot();
  if (toast !== null) dismissAdminToast(toast.token);
});

describe("AdminAccountPanel (ADR 0150, handoff 10–12)", () => {
  it("renders nothing while no account is open", () => {
    renderPanel(null);
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("names the account by its address, shows its facts, and starts on the close button", async () => {
    renderPanel(ACTIVE);

    const dialog = screen.getByRole("dialog", { name: "konto.a@example.test" });
    expect(within(dialog).getByText("Användare")).toBeInTheDocument();
    expect(within(dialog).getByText("2026-09-28 14:02")).toBeInTheDocument();
    expect(within(dialog).getByText("Sparade sökningar").nextElementSibling).toHaveTextContent("3");
    expect(within(dialog).getByText("Senast inloggad").nextElementSibling).toHaveTextContent("–");
    await waitFor(() => expect(screen.getByRole("button", { name: "Stäng" })).toHaveFocus());
  });

  it("offers an active account's actions in the handoff's order, the unbuilt ones saying Kommer snart", () => {
    renderPanel(ACTIVE);

    expect(actionNames()).toEqual([
      "Agera som användaren Kommer snart",
      "Ändra e-postadress",
      "Skicka inloggningslänk Kommer snart",
      "Suspendera konto",
      "Radera konto",
      "Radera permanent Kommer snart",
    ]);
    const unbuilt = screen.getByRole("button", { name: "Agera som användaren Kommer snart" });
    expect(unbuilt).toHaveAttribute("aria-disabled", "true");
    expect(unbuilt).not.toBeDisabled();
  });

  it("offers a suspended account its reinstatement and a pending deletion its (unbuilt) restore", () => {
    renderPanel({ ...ACTIVE, status: "suspended" });
    expect(actionNames()).toContain("Häv suspendering");
    expect(actionNames()).not.toContain("Suspendera konto");
  });

  it("offers no edit and no second deletion to an account already pending deletion", () => {
    renderPanel({ ...ACTIVE, status: "pendingDeletion", deletionEarliest: "2026-10-28T08:00:00Z" });
    expect(actionNames()).toEqual([
      "Agera som användaren Kommer snart",
      "Ångra radering Kommer snart",
      "Radera permanent Kommer snart",
    ]);
  });

  it("renders an action as Kommer snart when it is not live, and pressing it does nothing", async () => {
    const onCommand = vi.fn(async () => null);
    renderPanel(ACTIVE, onCommand, new Set());

    const suspend = screen.getByRole("button", { name: "Suspendera konto Kommer snart" });
    await userEvent.click(suspend);
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(onCommand).not.toHaveBeenCalled();
  });

  it("suspends only after a confirmation that starts on Avbryt, then confirms with a receipt", async () => {
    const onCommand = vi.fn(async () => null);
    renderPanel(ACTIVE, onCommand);

    await userEvent.click(screen.getByRole("button", { name: "Suspendera konto" }));
    const confirm = screen.getByRole("alertdialog", { name: "Suspendera konto.a@example.test?" });
    await waitFor(() => expect(within(confirm).getByRole("button", { name: "Avbryt" })).toHaveFocus());
    expect(onCommand).not.toHaveBeenCalled();

    await userEvent.click(within(confirm).getByRole("button", { name: "Suspendera konto" }));
    expect(onCommand).toHaveBeenCalledWith(ACTIVE, { kind: "suspend" });
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(getAdminToastSnapshot()?.message).toBe("konto.a@example.test är suspenderat.");
  });

  it("keeps the confirmation open with the refusal when the command is refused, and shows no receipt", async () => {
    renderPanel(ACTIVE, async () => "Du kan inte göra det med ditt eget konto.");

    await userEvent.click(screen.getByRole("button", { name: "Radera konto" }));
    const confirm = screen.getByRole("alertdialog", { name: "Radera konto.a@example.test?" });
    expect(confirm).toHaveTextContent("raderas slutgiltigt tidigast 3 nov. 2026");
    await userEvent.click(within(confirm).getByRole("button", { name: "Radera konto" }));

    expect(await within(confirm).findByRole("alert")).toHaveTextContent("Du kan inte göra det med ditt eget konto.");
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
    expect(getAdminToastSnapshot()).toBeNull();
  });

  it("asks for a new address, refuses an empty or unchanged one, and sends a request, never a change", async () => {
    const onCommand = vi.fn(async () => null);
    renderPanel(ACTIVE, onCommand);

    await userEvent.click(screen.getByRole("button", { name: "Ändra e-postadress" }));
    const field = screen.getByLabelText("Ny e-postadress");
    await waitFor(() => expect(field).toHaveFocus());

    await userEvent.click(screen.getByRole("button", { name: "Skicka bekräftelse" }));
    expect(await screen.findByText("Skriv in den nya e-postadressen.")).toBeInTheDocument();
    expect(field).toHaveAttribute("aria-invalid", "true");

    await userEvent.type(field, "KONTO.A@example.test");
    await userEvent.click(screen.getByRole("button", { name: "Skicka bekräftelse" }));
    expect(await screen.findByText(/samma som kontots nuvarande/)).toBeInTheDocument();

    await userEvent.clear(field);
    await userEvent.type(field, "ny.adress@example.test");
    await userEvent.click(screen.getByRole("button", { name: "Skicka bekräftelse" }));
    expect(onCommand).toHaveBeenCalledWith(ACTIVE, { kind: "changeEmail", newEmail: "ny.adress@example.test" });
    await waitFor(() => expect(screen.queryByLabelText("Ny e-postadress")).toBeNull());
    expect(getAdminToastSnapshot()?.message).toContain("ny.adress@example.test");
    expect(screen.getByRole("dialog", { name: "konto.a@example.test" })).toBeInTheDocument();
  });

  it("leaves edit mode on Escape without closing the panel, and returns to the button that opened it", async () => {
    const { onClose } = renderPanel(ACTIVE);

    await userEvent.click(screen.getByRole("button", { name: "Ändra e-postadress" }));
    await waitFor(() => expect(screen.getByLabelText("Ny e-postadress")).toHaveFocus());
    await userEvent.keyboard("{Escape}");

    expect(screen.queryByLabelText("Ny e-postadress")).toBeNull();
    expect(onClose).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.getByRole("button", { name: "Ändra e-postadress" })).toHaveFocus());

    await userEvent.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalled();
  });

  it("stays open when the pointer goes down on the receipt toast, and closes on any other outside press", async () => {
    const onClose = vi.fn();
    render(
      <>
        <AdminAccountPanel
          account={ACTIVE}
          onClose={onClose}
          live={ALL_LIVE}
          onCommand={async () => null}
          deletionEarliestIfScheduledNow="2026-11-03T08:00:00Z"
        />
        <AdminToastHost />
      </>,
    );
    act(() => {
      showAdminToast("konto.a@example.test är suspenderat.");
    });
    // Radix arms its outside-press listener one task after the dialog opens.
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)));

    // The open dialog sets pointer-events: none on the body, and jsdom does not load the stylesheet
    // that gives the toast its own back.
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    await user.click(screen.getByRole("button", { name: "Stäng meddelandet", hidden: true }));
    expect(getAdminToastSnapshot()).toBeNull();
    expect(onClose).not.toHaveBeenCalled();
    fireEvent.pointerDown(document.body);
    fireEvent.click(document.body);
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
