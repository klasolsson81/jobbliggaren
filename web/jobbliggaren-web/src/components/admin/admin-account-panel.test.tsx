import { useRef, useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AdminAccountDetail, AdminAddressedAccount } from "@/lib/admin/view-models";
import {
  dismissAdminToast,
  getAdminToastHeld,
  getAdminToastSnapshot,
  showAdminToast,
} from "@/lib/admin/toast-store";
import { AdminToastHost } from "./admin-toast-host";
import {
  AdminAccountPanel,
  type AdminAccountCommand,
  type AdminAccountCommands,
  type AdminAccountDetails,
  type AdminCommandRefusal,
  type AdminLiveAction,
} from "./admin-account-panel";

const ACTIVE: AdminAddressedAccount = {
  id: "a",
  email: "konto.a@example.test",
  role: "user",
  status: "active",
  emailConfirmed: true,
  registeredAt: "2026-09-28T12:02:00Z",
  applicationCount: 4,
  deletionEarliest: null,
  savedSearchCount: 3,
  resumeCount: 1,
};

/** A pending deletion as the directory reports it: a date, and no counts. */
const PENDING: AdminAccountDetail = {
  ...ACTIVE,
  status: "pendingDeletion",
  deletionEarliest: "2026-10-28",
  applicationCount: null,
  savedSearchCount: null,
  resumeCount: null,
};

const ALL_LIVE: ReadonlySet<AdminLiveAction> = new Set(["changeEmail", "suspend", "reinstate", "scheduleDeletion"]);

function commands(
  run: AdminAccountCommands["run"] = async () => null,
  live: ReadonlySet<AdminLiveAction> = ALL_LIVE,
): AdminAccountCommands {
  return { live, run, deletionEarliestIfScheduledNow: "2026-11-03" };
}

function loaded(account: AdminAccountDetail | null): AdminAccountDetails {
  return account === null ? { kind: "loading" } : { kind: "loaded", data: account };
}

function renderPanel(
  account: AdminAccountDetail | null,
  onCommand: (account: AdminAddressedAccount, command: AdminAccountCommand) => Promise<AdminCommandRefusal> = async () => null,
  live: ReadonlySet<AdminLiveAction> = ALL_LIVE,
) {
  const onClose = vi.fn();
  render(
    <AdminAccountPanel account={account} details={loaded(account)} onClose={onClose} commands={commands(onCommand, live)} />,
  );
  return { onClose };
}

/** The text a reader sees: a busy label's hidden form is not part of it. */
function shownText(node: Node): string {
  if (node instanceof Element && node.getAttribute("aria-hidden") === "true") return "";
  if (node.nodeType === Node.TEXT_NODE) return node.textContent ?? "";
  return [...node.childNodes].map(shownText).join("");
}

function actionNames() {
  return within(screen.getByRole("region", { name: "Åtgärder" }))
    .getAllByRole("button")
    .map(shownText);
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

  it("offers a suspended account its reinstatement instead of its suspension", () => {
    renderPanel({ ...ACTIVE, status: "suspended" });
    expect(actionNames()).toContain("Häv suspendering");
    expect(actionNames()).not.toContain("Suspendera konto");
  });

  it("offers no edit and no second deletion to an account already pending deletion", () => {
    renderPanel(PENDING);
    expect(actionNames()).toEqual([
      "Agera som användaren Kommer snart",
      "Ångra radering Kommer snart",
      "Radera permanent Kommer snart",
    ]);
  });

  it("states a pending deletion's date first, and leaves out the counts it does not know", () => {
    renderPanel(PENDING);

    const dialog = screen.getByRole("dialog", { name: "konto.a@example.test" });
    expect(within(dialog).getByText("Under radering")).toBeInTheDocument();
    expect(within(dialog).getByText("Raderas slutgiltigt").nextElementSibling).toHaveTextContent("Tidigast 2026-10-28");
    for (const label of ["Ansökningar", "Sparade sökningar", "CV:n"]) {
      expect(within(dialog).queryByText(label)).toBeNull();
    }
  });

  it("says whether the address is confirmed, and offers an unconfirmed one's marking as Kommer snart", () => {
    // The retired password registration (ADR 0142) left addresses unconfirmed; the current writer never does
    // (AdminAccountsDirectoryTests.The_current_writer_creates_every_account_confirmed), and
    // AdminAccountsDirectoryTests.Email_confirmed_reads_the_column pins that the directory reports the flag.
    renderPanel({ ...ACTIVE, emailConfirmed: false });

    const dialog = screen.getByRole("dialog", { name: "konto.a@example.test" });
    expect(within(dialog).getByText("E-post").nextElementSibling).toHaveTextContent("Ej bekräftad");
    expect(actionNames()).toContain("Markera e-post som bekräftad Kommer snart");
  });

  it("has no actions for an account without a profile, and says what becomes of it", () => {
    renderPanel({
      ...ACTIVE,
      status: "profileMissing",
      registeredAt: null,
      applicationCount: null,
      savedSearchCount: null,
      resumeCount: null,
    });

    const dialog = screen.getByRole("dialog", { name: "konto.a@example.test" });
    expect(within(dialog).getByText("Ofullständig")).toBeInTheDocument();
    expect(within(dialog).queryByRole("region", { name: "Åtgärder" })).toBeNull();
    expect(within(dialog).getByText(/tas bort automatiskt/)).toBeInTheDocument();
    expect(within(dialog).getByText("Registrerad").nextElementSibling).toHaveTextContent("Uppgift saknas");
    expect(within(dialog).queryByText("Ansökningar")).toBeNull();
  });

  it("names an unknown value for a screen reader, which skips a lone dash", () => {
    renderPanel(ACTIVE);

    const lastLogin = screen.getByText("Senast inloggad").nextElementSibling;
    expect(lastLogin?.querySelector('[aria-hidden="true"]')).toHaveTextContent("–");
    expect(lastLogin?.querySelector(".sr-only")).toHaveTextContent("Uppgift saknas");
  });

  it("shows the row at once and a loading line until the details arrive, then the details", () => {
    const { rerender } = render(
      <AdminAccountPanel account={ACTIVE} details={{ kind: "loading" }} onClose={() => {}} commands={commands()} />,
    );

    const dialog = screen.getByRole("dialog", { name: "konto.a@example.test" });
    expect(within(dialog).getByRole("status")).toHaveTextContent("Hämtar kontots uppgifter…");
    expect(within(dialog).queryByRole("region", { name: "Åtgärder" })).toBeNull();

    rerender(<AdminAccountPanel account={ACTIVE} details={loaded(ACTIVE)} onClose={() => {}} commands={commands()} />);
    expect(within(dialog).queryByRole("status")).toBeNull();
    expect(within(dialog).getByRole("region", { name: "Åtgärder" })).toBeInTheDocument();
  });

  it("shows the failure its caller words in place of the details, with the retry it offers", async () => {
    const onRetry = vi.fn();
    render(
      <AdminAccountPanel
        account={ACTIVE}
        details={{ kind: "failed", message: "Kontots uppgifter kunde inte hämtas. Försök igen om en stund.", recovery: "retry" }}
        onClose={() => {}}
        commands={commands()}
        onRetry={onRetry}
      />,
    );

    expect(screen.getByRole("alert")).toHaveTextContent("Kontots uppgifter kunde inte hämtas.");
    expect(screen.queryByRole("region", { name: "Åtgärder" })).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: "Försök igen" }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it("offers a sign-in link instead of a retry when the session has ended", () => {
    render(
      <AdminAccountPanel
        account={ACTIVE}
        details={{ kind: "failed", message: "Du är inte inloggad längre. Logga in och försök igen.", recovery: "signIn" }}
        onClose={() => {}}
        onRetry={() => {}}
      />,
    );

    expect(screen.getByRole("link", { name: "Logga in" })).toHaveAttribute("href", "/logga-in");
    expect(screen.queryByRole("button", { name: "Försök igen" })).toBeNull();
  });

  it("names an account that no longer exists by its address alone", () => {
    render(<AdminAccountPanel account={ACTIVE} details={{ kind: "gone" }} onClose={() => {}} />);

    const dialog = screen.getByRole("dialog", { name: "konto.a@example.test" });
    expect(within(dialog).getByRole("alert")).toHaveTextContent("Kontot finns inte längre.");
    expect(within(dialog).queryByText("Aktiv")).toBeNull();
    expect(within(dialog).queryByText("Användare")).toBeNull();
  });

  it("returns focus to the caller's fallback when the row that opened it has gone", async () => {
    let removeOpener: () => void = () => {};
    function Harness() {
      const [account, setAccount] = useState<AdminAccountDetail | null>(null);
      const [opener, setOpener] = useState(true);
      const fallback = useRef<HTMLDivElement>(null);
      removeOpener = () => setOpener(false);
      return (
        <>
          <div ref={fallback} tabIndex={-1} data-testid="fallback" />
          {opener ? (
            <button type="button" onClick={() => setAccount(ACTIVE)}>
              Öppna
            </button>
          ) : null}
          <AdminAccountPanel
            account={account}
            details={loaded(account)}
            onClose={() => setAccount(null)}
            fallbackFocus={() => fallback.current}
          />
        </>
      );
    }
    render(<Harness />);

    await userEvent.click(screen.getByRole("button", { name: "Öppna" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Stäng" })).toHaveFocus());
    act(() => removeOpener());
    await userEvent.keyboard("{Escape}");

    await waitFor(() => expect(screen.getByTestId("fallback")).toHaveFocus());
  });

  it("offers every action as Kommer snart where no command is built", () => {
    render(<AdminAccountPanel account={ACTIVE} details={loaded(ACTIVE)} onClose={() => {}} />);

    expect(actionNames().every((name) => name.endsWith("Kommer snart"))).toBe(true);
  });

  it("names an account without an address as unknown and offers it no action, should one ever exist", () => {
    // Declared unreachable: every writer of an account in src stores an address (account creation and the
    // address swap). Identity's column is nullable all the same, so this pins only that the read side
    // degrades safely.
    const unnamed: AdminAccountDetail = { ...ACTIVE, email: null };
    render(<AdminAccountPanel account={unnamed} details={loaded(unnamed)} onClose={() => {}} commands={commands()} />);

    expect(screen.getByRole("dialog", { name: "Uppgift saknas" })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Åtgärder" })).toBeNull();
    expect(screen.getByText("Kontot har ingen e-postadress.")).toBeInTheDocument();
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
    expect(getAdminToastSnapshot()?.message).toBe("Kontot konto.a@example.test är suspenderat.");
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
        <AdminAccountPanel account={ACTIVE} details={loaded(ACTIVE)} onClose={onClose} commands={commands()} />
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
    await user.click(screen.getByRole("button", { name: "Stäng meddelandet" }));
    expect(getAdminToastSnapshot()).toBeNull();
    expect(onClose).not.toHaveBeenCalled();
    fireEvent.pointerDown(document.body);
    fireEvent.click(document.body);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("returns focus to the action that opened a confirmation the reader cancelled", async () => {
    renderPanel(ACTIVE);

    const suspend = screen.getByRole("button", { name: "Suspendera konto" });
    await userEvent.click(suspend);
    await userEvent.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Avbryt" }));

    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    await waitFor(() => expect(screen.getByRole("button", { name: "Suspendera konto" })).toHaveFocus());
  });

  it("names a direct command while it runs, then publishes its receipt and moves focus to the title", async () => {
    let settle: (refusal: AdminCommandRefusal) => void = () => {};
    renderPanel({ ...ACTIVE, status: "suspended" }, () => new Promise((resolve) => (settle = resolve)));

    await userEvent.click(screen.getByRole("button", { name: "Häv suspendering" }));
    expect(screen.getByRole("button", { name: "Häver…" })).toBeDisabled();

    settle(null);
    await waitFor(() => expect(screen.getByRole("heading", { name: "konto.a@example.test" })).toHaveFocus());
    expect(getAdminToastSnapshot()?.message).toBe("Suspenderingen av konto.a@example.test är hävd.");
  });

  it("holds the receipt's clock while it is open, and lets it go when it closes", () => {
    const { rerender } = render(
      <AdminAccountPanel account={ACTIVE} details={loaded(ACTIVE)} onClose={() => {}} commands={commands()} />,
    );
    expect(getAdminToastHeld()).toBe(true);

    rerender(<AdminAccountPanel account={null} details={loaded(null)} onClose={() => {}} commands={commands()} />);
    expect(getAdminToastHeld()).toBe(false);
  });
});
