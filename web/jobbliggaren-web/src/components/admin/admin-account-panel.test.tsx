import { useEffect, useRef, useState } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AdminAccountDetail, AdminAddressedAccount, AdminSelf } from "@/lib/admin/view-models";
import type {
  AdminEmailChangeCancelOutcome,
  AdminEmailChangeRequestOutcome,
  AdminEmailChangeState,
  AdminPendingEmailChange,
} from "@/lib/admin/account-email-change";
import type { CodeProof, ReauthRequestResult } from "@/lib/auth/reauth-action-state";
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

/** The signed-in administrator: another account than the one opened, at another address. */
const SELF: AdminSelf = { userId: "admin-id", email: "admin@example.test" };

const NEW = "ny.adress@example.test";
const SPENT = "Koden du skrev in är förbrukad, så du behöver en ny kod när du försöker igen.";

/** A change as the backend's 202 or its read answers it; the instants are Swedish time 2026-10-08 14:00 and 10-09 14:00. */
const CHANGE: AdminPendingEmailChange = {
  state: "pending",
  completableFrom: "2026-10-08T12:00:00Z",
  expiresAt: "2026-10-09T12:00:00Z",
};

const ALL_LIVE: ReadonlySet<AdminLiveAction> = new Set([
  "changeEmail",
  "cancelEmailChange",
  "suspend",
  "reinstate",
  "scheduleDeletion",
]);

type RequestCommand = (
  account: AdminAddressedAccount,
  newEmail: string,
  proof: CodeProof,
) => Promise<AdminEmailChangeRequestOutcome>;

const requestCodeMock = vi.fn<() => Promise<ReauthRequestResult>>();
const requestMock = vi.fn<RequestCommand>();
const cancelMock = vi.fn<(account: AdminAddressedAccount) => Promise<AdminEmailChangeCancelOutcome>>();

function commands(
  run: AdminAccountCommands["run"] = async () => null,
  live: ReadonlySet<AdminLiveAction> = ALL_LIVE,
): AdminAccountCommands {
  return {
    live,
    run,
    deletionEarliestIfScheduledNow: "2026-11-03",
    emailChange: { requestCode: requestCodeMock, request: requestMock, cancel: cancelMock, returnPath: "/admin/anvandare" },
  };
}

function loaded(account: AdminAccountDetail | null): AdminAccountDetails {
  return account === null ? { kind: "loading" } : { kind: "loaded", data: account };
}

function renderPanel(
  account: AdminAccountDetail | null,
  onCommand: (account: AdminAddressedAccount, command: AdminAccountCommand) => Promise<AdminCommandRefusal> = async () => null,
  live: ReadonlySet<AdminLiveAction> = ALL_LIVE,
  emailChange: AdminEmailChangeState = { kind: "none" },
) {
  const onClose = vi.fn();
  render(
    <AdminAccountPanel
      account={account}
      details={loaded(account)}
      onClose={onClose}
      commands={commands(onCommand, live)}
      self={SELF}
      emailChange={emailChange}
    />,
  );
  return { onClose };
}

/**
 * The panel as its caller holds it: the caller keeps the pending change a command answered, and marks the account
 * gone when a command says so, as the account page does.
 */
function Harness({
  account = ACTIVE,
  initialChange = { kind: "none" },
  onClose = () => {},
}: {
  readonly account?: AdminAddressedAccount;
  readonly initialChange?: AdminEmailChangeState;
  readonly onClose?: () => void;
}) {
  const [emailChange, setEmailChange] = useState<AdminEmailChangeState>(initialChange);
  const [details, setDetails] = useState<AdminAccountDetails>({ kind: "loaded", data: account });
  const live: AdminAccountCommands = {
    live: ALL_LIVE,
    run: async () => null,
    deletionEarliestIfScheduledNow: "2026-11-03",
    emailChange: {
      requestCode: requestCodeMock,
      request: async (target, newEmail, proof) => {
        const outcome = await requestMock(target, newEmail, proof);
        if (outcome.ok) setEmailChange({ kind: "pending", change: outcome.value });
        else if (outcome.kind === "outcomeUnknown") setEmailChange({ kind: "unknown" });
        if (outcome.after === "gone") setDetails({ kind: "gone" });
        return outcome;
      },
      cancel: async (target) => {
        const outcome = await cancelMock(target);
        if (outcome.kind === "cancelled") setEmailChange({ kind: "none" });
        return outcome;
      },
      returnPath: "/admin/anvandare",
    },
  };
  return (
    <AdminAccountPanel
      account={account}
      details={details}
      onClose={onClose}
      commands={live}
      self={SELF}
      emailChange={emailChange}
    />
  );
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

type User = ReturnType<typeof userEvent.setup>;

/** Opens the edit form, types the new address and presses Fortsätt; returns the step-up dialog. */
async function continueWith(user: User, typed = NEW) {
  await user.click(screen.getByRole("button", { name: "Ändra e-postadress" }));
  await user.type(screen.getByLabelText("Ny e-postadress"), typed);
  await user.click(screen.getByRole("button", { name: "Fortsätt" }));
  return screen.findByRole("dialog", { name: "Ändra e-postadress" });
}

/** Goes through the step-up and presses its primary, which requests the change. */
async function requestChange(user: User) {
  const dialog = await continueWith(user);
  await user.click(within(dialog).getByRole("button", { name: "Skicka kod" }));
  await user.type(await within(dialog).findByLabelText("Sexsiffrig kod"), "123456");
  await user.click(within(dialog).getByRole("button", { name: "Bekräfta koden" }));
}

beforeEach(() => {
  requestCodeMock.mockReset();
  requestCodeMock.mockResolvedValue({ ok: true, challengeId: "step-up-challenge" });
  requestMock.mockReset();
  requestMock.mockResolvedValue({ ok: true, value: CHANGE });
  cancelMock.mockReset();
  cancelMock.mockResolvedValue({ kind: "cancelled" });
});

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
    expect(within(dialog).queryByText("Adressbyte")).toBeNull();
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
      <AdminAccountPanel account={ACTIVE} details={{ kind: "loading" }} onClose={() => {}} commands={commands()} self={SELF} />,
    );

    const dialog = screen.getByRole("dialog", { name: "konto.a@example.test" });
    expect(within(dialog).getByRole("status")).toHaveTextContent("Hämtar kontots uppgifter…");
    expect(within(dialog).queryByRole("region", { name: "Åtgärder" })).toBeNull();

    rerender(
      <AdminAccountPanel account={ACTIVE} details={loaded(ACTIVE)} onClose={() => {}} commands={commands()} self={SELF} />,
    );
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
    function Opener({ expose }: { readonly expose: (remove: () => void) => void }) {
      const [account, setAccount] = useState<AdminAccountDetail | null>(null);
      const [opener, setOpener] = useState(true);
      const fallback = useRef<HTMLDivElement>(null);
      useEffect(() => expose(() => setOpener(false)), [expose]);
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
    render(<Opener expose={(remove) => (removeOpener = remove)} />);

    await userEvent.click(screen.getByRole("button", { name: "Öppna" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Stäng" })).toHaveFocus());
    act(() => removeOpener());
    await userEvent.keyboard("{Escape}");

    await waitFor(() => expect(screen.getByTestId("fallback")).toHaveFocus());
  });

  it("offers every action as Kommer snart where no command is built", () => {
    render(<AdminAccountPanel account={ACTIVE} details={loaded(ACTIVE)} onClose={() => {}} self={SELF} />);

    expect(actionNames().every((name) => name.endsWith("Kommer snart"))).toBe(true);
  });

  it("offers an action as Kommer snart when it is named live without the command that runs it (ADR 0150 D4)", () => {
    // The account page's own set (#1975): the address change and its cancel, and no command for #1976–#1977.
    render(
      <AdminAccountPanel
        account={ACTIVE}
        details={loaded(ACTIVE)}
        onClose={() => {}}
        self={SELF}
        commands={{ ...commands(), run: undefined, deletionEarliestIfScheduledNow: undefined }}
      />,
    );

    expect(actionNames()).toEqual([
      "Agera som användaren Kommer snart",
      "Ändra e-postadress",
      "Skicka inloggningslänk Kommer snart",
      "Suspendera konto Kommer snart",
      "Radera konto Kommer snart",
      "Radera permanent Kommer snart",
    ]);
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
        <AdminAccountPanel account={ACTIVE} details={loaded(ACTIVE)} onClose={onClose} commands={commands()} self={SELF} />
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

describe("AdminAccountPanel — an administrator account's address (#1975, design-reviewer item 1)", () => {
  it("offers no address change for an administrator account, and says so where it would stand", () => {
    renderPanel({ ...ACTIVE, role: "admin" });

    expect(actionNames()).not.toContain("Ändra e-postadress");
    const region = screen.getByRole("region", { name: "Åtgärder" });
    expect(within(region).getByText("Adressen på ett administratörskonto byts inte här.")).toHaveClass(
      "jp-adminpanel__note",
    );
    expect(within(region).queryByText(/Mina sidor/)).toBeNull();
    // Not the disabled form D2 keeps for what is not built: this is refused by design, so no button stands there.
    expect(within(region).queryByRole("button", { name: /e-postadress/ })).toBeNull();
  });

  it("points the administrator's own account to Mina sidor, telling it by its id", () => {
    renderPanel({ ...ACTIVE, id: SELF.userId, email: SELF.email, role: "admin" });

    expect(
      within(screen.getByRole("region", { name: "Åtgärder" })).getByText(
        "Adressen på ett administratörskonto byts inte här. Byt din på Mina sidor.",
      ),
    ).toBeInTheDocument();
  });

  it("never takes another account at the administrator's address for the administrator's own", () => {
    // An address is no identity (#1975 C-1): only the id says whose account it is.
    renderPanel({ ...ACTIVE, email: SELF.email.toUpperCase() });

    expect(actionNames()).toContain("Ändra e-postadress");
    expect(screen.queryByText(/Mina sidor/)).toBeNull();
  });

  it("takes the administrator's own id for their own account, whatever role and address the row shows", () => {
    // The backend refuses the administrator's own account by its id as well (`AdministratorTarget`).
    renderPanel({ ...ACTIVE, id: SELF.userId.toUpperCase() });

    expect(actionNames()).not.toContain("Ändra e-postadress");
    expect(screen.getByText(/Byt din på Mina sidor\./)).toBeInTheDocument();
  });
});

describe("AdminAccountPanel — a pending address change (#1975, design-reviewer items 2 and 6)", () => {
  it("states a pending change as a fact, with both instants, and offers its cancel in place of a second request", () => {
    renderPanel(ACTIVE, undefined, ALL_LIVE, { kind: "pending", change: CHANGE });

    const dialog = screen.getByRole("dialog", { name: "konto.a@example.test" });
    expect(within(dialog).getByText("Adressbyte").nextElementSibling).toHaveTextContent(
      "Väntar på kontoägaren. Koden kan användas från 2026-10-08 14:00 till 2026-10-09 14:00.",
    );
    expect(actionNames()).toContain("Avbryt adressbytet");
    expect(actionNames()).not.toContain("Ändra e-postadress");
    const cancel = screen.getByRole("button", { name: "Avbryt adressbytet" });
    expect(cancel).toHaveClass("jp-btn--secondary");
    expect(cancel).not.toHaveClass("jp-btn--danger");
  });

  it("says a change whose code was entered wrongly too often cannot be used, and when it lapses", () => {
    renderPanel(ACTIVE, undefined, ALL_LIVE, { kind: "pending", change: { ...CHANGE, state: "codeBurned" } });

    expect(screen.getByText("Adressbyte").nextElementSibling).toHaveTextContent(
      "Koden har skrivits fel för många gånger och kan inte användas. Bytet upphör 2026-10-09 14:00.",
    );
    expect(actionNames()).toContain("Avbryt adressbytet");
  });

  it("says it cannot tell whether a change is pending when the read failed, and offers the request", () => {
    renderPanel(ACTIVE, undefined, ALL_LIVE, { kind: "unknown" });

    const row = screen.getByText("Adressbyte").nextElementSibling;
    expect(row?.querySelector(".sr-only")).toHaveTextContent("Uppgift saknas");
    expect(actionNames()).toContain("Ändra e-postadress");
  });

  it("cancels at a press without asking first, names the cancel while it runs, then confirms with a receipt", async () => {
    let settle: (outcome: AdminEmailChangeCancelOutcome) => void = () => {};
    cancelMock.mockImplementation(() => new Promise((resolve) => (settle = resolve)));
    render(<Harness initialChange={{ kind: "pending", change: CHANGE }} />);

    await userEvent.click(screen.getByRole("button", { name: "Avbryt adressbytet" }));
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(screen.getByRole("button", { name: "Avbryter…" })).toBeDisabled();
    expect(cancelMock).toHaveBeenCalledWith(ACTIVE);

    await act(async () => settle({ kind: "cancelled" }));
    await waitFor(() => expect(screen.getByRole("heading", { name: "konto.a@example.test" })).toHaveFocus());
    expect(getAdminToastSnapshot()?.message).toBe(
      "Adressbytet är avbrutet. Koden till den nya adressen gäller inte längre.",
    );
    expect(screen.queryByText("Adressbyte")).toBeNull();
    expect(actionNames()).toContain("Ändra e-postadress");
  });

  it.each([
    [{ kind: "nothingPending" }, "Det finns inget adressbyte att avbryta längre."],
    [{ kind: "unknown" }, "Vi kan inte se om adressbytet avbröts. Öppna kontot igen för att se om det väntar."],
    [{ kind: "refused", reason: "rateLimited", retryAfterSeconds: 6 }, "För många förfrågningar. Försök igen om 6 sekunder."],
    [{ kind: "refused", reason: "unauthorized" }, "Du är inte inloggad längre. Logga in och försök igen."],
    [{ kind: "refused", reason: "forbidden" }, "Din session saknar Admin-rollen."],
  ] as const)("claims only what happened when a cancel ends otherwise (%o), as a status that takes focus", async (outcome, copy) => {
    cancelMock.mockResolvedValue(outcome);
    render(<Harness initialChange={{ kind: "pending", change: CHANGE }} />);

    await userEvent.click(screen.getByRole("button", { name: "Avbryt adressbytet" }));

    const status = await screen.findByText(copy);
    expect(status).toHaveAttribute("role", "status");
    expect(status).toHaveClass("jp-adminpanel__status");
    await waitFor(() => expect(status).toHaveFocus());
    expect(getAdminToastSnapshot()).toBeNull();
  });
});

describe("AdminAccountPanel — requesting an address change (#1975, design-reviewer items 3–5 and 7)", () => {
  it("asks for the address under a hint that says when it changes, with Fortsätt as the only primary", async () => {
    renderPanel(ACTIVE);

    await userEvent.click(screen.getByRole("button", { name: "Ändra e-postadress" }));

    const field = screen.getByLabelText("Ny e-postadress");
    await waitFor(() => expect(field).toHaveFocus());
    expect(field).toHaveAccessibleDescription("Adressen byts när kontoägaren har använt koden som skickas dit.");
    expect(screen.getByRole("button", { name: "Fortsätt" })).toHaveClass("jp-btn--primary");
    expect(screen.queryByRole("button", { name: "Skicka bekräftelse" })).toBeNull();
  });

  it("names both recipients before anything is sent, and sends the step-up code to the administrator, never the account", async () => {
    const user = userEvent.setup();
    renderPanel(ACTIVE);

    const dialog = await continueWith(user);

    expect(dialog).toHaveAccessibleDescription(
      `En kod skickas till ${NEW} och ett meddelande till konto.a@example.test. ` +
        `För att bekräfta att det är du skickar vi en sexsiffrig kod till ${SELF.email}.`,
    );
    expect(dialog).not.toHaveTextContent("sexsiffrig kod till konto.a@example.test");
    expect(dialog).toHaveClass("jp-adminstepup");
    // The stylesheet lifts the overlay as the sibling portalled right before the content.
    expect(dialog.previousElementSibling).toHaveAttribute("data-slot", "dialog-overlay");
  });

  it("returns focus to Fortsätt when the step-up is cancelled, and keeps the address typed", async () => {
    const user = userEvent.setup();
    renderPanel(ACTIVE);

    const dialog = await continueWith(user);
    await user.click(within(dialog).getByRole("button", { name: "Avbryt" }));

    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Ändra e-postadress" })).toBeNull());
    await waitFor(() => expect(screen.getByRole("button", { name: "Fortsätt" })).toHaveFocus());
    expect(screen.getByLabelText("Ny e-postadress")).toHaveValue(NEW);
    expect(requestMock).not.toHaveBeenCalled();
  });

  it("closes one layer per Escape: the step-up, then the form, then the panel", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    render(<Harness onClose={onClose} />);

    await continueWith(user);
    await user.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Ändra e-postadress" })).toBeNull());
    expect(screen.getByLabelText("Ny e-postadress")).toBeInTheDocument();

    await user.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByLabelText("Ny e-postadress")).toBeNull());
    expect(onClose).not.toHaveBeenCalled();

    await user.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalled();
  });

  it("requests the change with the administrator's code, then shows the pending change, its cancel and a receipt", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await requestChange(user);

    await waitFor(() => expect(screen.getByRole("heading", { name: "konto.a@example.test" })).toHaveFocus());
    expect(requestMock).toHaveBeenCalledWith(ACTIVE, NEW, { challengeId: "step-up-challenge", code: "123456" });
    expect(screen.queryByLabelText("Ny e-postadress")).toBeNull();
    expect(getAdminToastSnapshot()?.message).toBe(
      `En kod har skickats till ${NEW}. Adressen byts när kontoägaren har använt koden.`,
    );
    expect(screen.getByText("Adressbyte").nextElementSibling).toHaveTextContent(
      "Väntar på kontoägaren. Koden kan användas från 2026-10-08 14:00 till 2026-10-09 14:00.",
    );
    expect(actionNames()).toContain("Avbryt adressbytet");
    expect(actionNames()).not.toContain("Ändra e-postadress");
  });

  it("puts a taken address on the field, keeps it, and moves focus there", async () => {
    const taken = `E-postadressen används redan av ett annat konto. Skriv in en annan adress. ${SPENT}`;
    requestMock.mockResolvedValue({ ok: false, kind: "operationRefused", error: taken, channel: "field" });
    const user = userEvent.setup();
    render(<Harness />);

    await requestChange(user);

    const field = screen.getByLabelText("Ny e-postadress");
    expect(await screen.findByRole("alert")).toHaveTextContent(taken);
    expect(field).toHaveAttribute("aria-invalid", "true");
    expect(field).toHaveValue(NEW);
    await waitFor(() => expect(field).toHaveFocus());
    expect(getAdminToastSnapshot()).toBeNull();
  });

  it.each([
    ["a budget or a cooldown", { ok: false, kind: "operationRefused", error: `Det går inte att begära ett adressbyte just nu. Försök igen senare. ${SPENT}`, channel: "status" }],
    ["mail that cannot be sent", { ok: false, kind: "refused", error: `E-postutskick är inte aktiverat just nu. ${SPENT}` }],
    ["an address another account's change holds", { ok: false, kind: "operationRefused", error: `Ett annat konto väntar redan på att få den adressen. ${SPENT}`, channel: "status", after: "refetch" }],
  ] as const)("keeps %s in the form as a status, the address kept", async (_label, outcome) => {
    requestMock.mockResolvedValue(outcome);
    const user = userEvent.setup();
    render(<Harness />);

    await requestChange(user);

    const status = await screen.findByText(outcome.error);
    expect(status).toHaveAttribute("role", "status");
    await waitFor(() => expect(status).toHaveFocus());
    expect(screen.getByLabelText("Ny e-postadress")).toHaveValue(NEW);
    expect(screen.getByLabelText("Ny e-postadress")).not.toHaveAttribute("aria-invalid");
  });

  it("says no mail can be sent when the step-up itself is refused, before any code exists", async () => {
    requestCodeMock.mockResolvedValue({ ok: false, kind: "refused" });
    const user = userEvent.setup();
    render(<Harness />);

    const dialog = await continueWith(user);
    await user.click(within(dialog).getByRole("button", { name: "Skicka kod" }));

    const status = await screen.findByText(
      "E-postutskick är inte aktiverat just nu, så ingen kod kan skickas. Kontots adress är oförändrad. Försök igen senare.",
    );
    expect(status).toHaveAttribute("role", "status");
    await waitFor(() => expect(status).toHaveFocus());
  });

  it("claims nothing when the outcome is unknown: it leaves the form, and the pending change is unknown", async () => {
    const unknown = `Vi kan inte se om koden skickades. Öppna kontot igen för att se om ett adressbyte väntar. ${SPENT}`;
    requestMock.mockResolvedValue({ ok: false, kind: "outcomeUnknown", error: unknown });
    const user = userEvent.setup();
    render(<Harness />);

    await requestChange(user);

    const status = await screen.findByText(unknown);
    expect(status).toHaveAttribute("role", "status");
    await waitFor(() => expect(status).toHaveFocus());
    expect(screen.queryByLabelText("Ny e-postadress")).toBeNull();
    expect(screen.getByText("Adressbyte").nextElementSibling?.querySelector(".sr-only")).toHaveTextContent(
      "Uppgift saknas",
    );
    expect(getAdminToastSnapshot()).toBeNull();
  });

  it("leaves the form for a refusal about the account itself, beside the account's facts", async () => {
    const changed = `Kontot är ett administratörskonto, så adressen kan inte bytas här. ${SPENT}`;
    requestMock.mockResolvedValue({
      ok: false,
      kind: "operationRefused",
      error: changed,
      channel: "status",
      after: "changed",
    });
    const user = userEvent.setup();
    render(<Harness />);

    await requestChange(user);

    const status = await screen.findByText(changed);
    await waitFor(() => expect(status).toHaveFocus());
    expect(screen.queryByLabelText("Ny e-postadress")).toBeNull();
    expect(screen.getByRole("region", { name: "Åtgärder" })).toContainElement(status);
  });

  it("says an account that is gone is gone, and moves focus to its title", async () => {
    requestMock.mockResolvedValue({
      ok: false,
      kind: "operationRefused",
      error: `Kontot finns inte längre. ${SPENT}`,
      channel: "status",
      after: "gone",
    });
    const user = userEvent.setup();
    render(<Harness />);

    await requestChange(user);

    expect(await screen.findByRole("alert")).toHaveTextContent("Kontot finns inte längre.");
    await waitFor(() => expect(screen.getByRole("heading", { name: "konto.a@example.test" })).toHaveFocus());
    expect(screen.queryByRole("region", { name: "Åtgärder" })).toBeNull();
  });
});
