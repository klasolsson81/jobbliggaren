"use client";

// "use client": the panel holds its account's mode, the running command and what came of it.
import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState, useTransition } from "react";
import Link from "next/link";
import { Dialog } from "radix-ui";
import { useFormatter, useTranslations } from "next-intl";
import {
  BadgeCheck,
  Ban,
  Link2,
  MailX,
  Pencil,
  RotateCcw,
  Trash2,
  UserCog,
  UserRound,
  X,
  type LucideIcon,
} from "lucide-react";
import type {
  AdminEmailChangeCancelOutcome,
  AdminEmailChangeReread,
  AdminEmailChangeRequestOutcome,
  AdminEmailChangeState,
  AdminPendingEmailChange,
} from "@/lib/admin/account-email-change";
import type {
  AdminAccountAction,
  AdminAccountDetail,
  AdminAccountRow,
  AdminAddressedAccount,
  AdminSelf,
} from "@/lib/admin/view-models";
import type { CodeProof, ReauthRequestResult } from "@/lib/auth/reauth-action-state";
import { formatDate, formatDateTime } from "@/lib/i18n/format";
import { useReturnFocus } from "@/lib/hooks/use-return-focus";
import { holdAdminToasts, showAdminToast } from "@/lib/admin/toast-store";
import { STANDALONE_LINK } from "@/components/auth/mail-link";
import { AdminAccountStatus, AdminRolePill, unbroken } from "./admin-account-status";
import { ADMIN_NEW_EMAIL_FIELD_ID, AdminAccountEditForm, type AdminEditExit } from "./admin-account-edit-form";
import { AdminBusyLabel } from "./admin-busy-label";
import { AdminConfirmDialog } from "./admin-confirm-dialog";
import { AdminRegionLine } from "./admin-region-line";
import { AdminUnknown } from "./admin-unknown";
import { isInAdminToast } from "./admin-toast-host";
import { ReAuthCodeDialog, type ReauthDialogHandle, type ReauthHandOff } from "@/components/forms/reauth-code-dialog";
import type { AdminAccessOperation, AdminAccessOutcome, AdminAccessReceipt } from "@/lib/admin/account-access";
import type { AdminDeletionOutcome, AdminDeletionReceipt } from "@/lib/admin/account-deletion";

/** The actions whose flows exist in the MVP (#1975–#1977); every other action is "Kommer snart" here. */
export type AdminLiveAction = Extract<
  AdminAccountAction,
  "changeEmail" | "cancelEmailChange" | "suspend" | "reinstate" | "scheduleDeletion"
>;

/** The commands #1976 and #1977 run; the address change has commands of its own. */
export type AdminAccountCommand =
  | { readonly kind: "suspend" }
  | { readonly kind: "reinstate" }
  | { readonly kind: "scheduleDeletion" };

/** Null when the command went through; otherwise the refusal, shown where the command was asked. */
export type AdminCommandRefusal = string | null;

/** The address change and its cancel (#1975, ADR 0153), run by the caller, so the panel reaches no backend. */
export interface AdminEmailChangeCommands {
  /** Asks for the administrator's own step-up code, to their own address. */
  readonly requestCode: () => Promise<ReauthRequestResult>;
  /** Verifies that code and requests the change, in one Server Action. */
  readonly request: (
    account: AdminAddressedAccount,
    newEmail: string,
    proof: CodeProof,
  ) => Promise<AdminEmailChangeRequestOutcome>;
  readonly cancel: (account: AdminAddressedAccount) => Promise<AdminEmailChangeCancelOutcome>;
  /** The page a lapsed session returns to after logging in again. */
  readonly returnPath: string;
}

/**
 * The built actions and how to run them (ADR 0150 D4). Without them every action is "Kommer snart", and so is an
 * action named live without the command that runs it.
 */
export interface AdminAccountCommands {
  readonly live: ReadonlySet<AdminLiveAction>;
  /** Suspend, reinstate and schedule deletion (#1976, #1977). */
  readonly run?: (account: AdminAddressedAccount, command: AdminAccountCommand) => Promise<AdminCommandRefusal>;
  /**
   * `YYYY-MM-DD`: the earliest permanent deletion a deletion scheduled now would get. Absent where scheduling
   * deletion is not built, so no date is made up for it.
   */
  readonly deletionEarliestIfScheduledNow?: string;
  readonly emailChange?: AdminEmailChangeCommands;
  readonly access?: {
    readonly requestCode: () => Promise<ReauthRequestResult>;
    readonly run: (account: AdminAddressedAccount, operation: AdminAccessOperation, proof: CodeProof) => Promise<AdminAccessOutcome>;
    readonly returnPath: string;
  };
  readonly deletion?: {
    readonly requestCode: () => Promise<ReauthRequestResult>;
    readonly run: (account: AdminAddressedAccount, proof: CodeProof) => Promise<AdminDeletionOutcome>;
    readonly returnPath: string;
  };
}

/**
 * The panel's facts as they arrive, or why they did not. The caller words a failure and says what the admin
 * can do about it; an account that no longer exists is its own state, so the head stops describing it.
 */
export type AdminAccountDetails =
  | { readonly kind: "loading" }
  | { readonly kind: "failed"; readonly message: string; readonly recovery: "retry" | "signIn" | "none" }
  | { readonly kind: "gone" }
  | { readonly kind: "loaded"; readonly data: AdminAccountDetail };

type Confirming = "suspend" | "scheduleDeletion";

/** What a command left under the actions: a refusal of what was asked, or a status that is no one's fault. */
interface Notice {
  readonly text: string;
  readonly role: "alert" | "status";
  readonly reread?: boolean;
}

type FocusTarget = "title" | "notice";

type RereadFocus = "title" | "retry" | "emailChangeValue" | "emailChangeAction";

interface AdminAccountPanelProps {
  /** The row that opened the panel, or null while it is closed. The head shows it until the facts arrive. */
  readonly account: AdminAccountRow | null;
  readonly details: AdminAccountDetails;
  readonly onClose: () => void;
  readonly commands?: AdminAccountCommands;
  /**
   * The signed-in administrator. Their own account is told by its id, never by its address, and their step-up code
   * goes to their own address; without them the address change is not offered.
   */
  readonly self?: AdminSelf;
  /** The open account's pending address change, as the caller last read it or a command left it (#1975). */
  readonly emailChange?: AdminEmailChangeState;
  /** Reads the details again, for a failure whose recovery is a retry. */
  readonly onRetry?: () => void;
  /** Reads the pending address change again, for a read that failed. */
  readonly onRetryEmailChange?: () => Promise<AdminEmailChangeReread>;
  /** Where focus goes on close when the row that opened the panel is gone. */
  readonly fallbackFocus?: () => HTMLElement | null;
}

const ICONS: Readonly<Record<AdminAccountAction, LucideIcon>> = {
  impersonate: UserCog,
  changeEmail: Pencil,
  cancelEmailChange: MailX,
  sendLoginLink: Link2,
  markVerified: BadgeCheck,
  reinstate: RotateCcw,
  restore: RotateCcw,
  suspend: Ban,
  scheduleDeletion: Trash2,
  deletePermanently: Trash2,
};

const NO_LIVE_ACTIONS: ReadonlySet<AdminLiveAction> = new Set();

const NO_EMAIL_CHANGE: AdminEmailChangeState = { kind: "none" };

/** The actions that run at a press and name themselves while they run; the others open a form or a question. */
type DirectAction = Extract<AdminLiveAction, "reinstate" | "cancelEmailChange">;

function isDirect(action: AdminLiveAction): action is DirectAction {
  return action === "reinstate" || action === "cancelEmailChange";
}

/**
 * The note that stands where an administrator account's address change would, and the note and retry that stand
 * where the address change or its cancel would while the pending change cannot be read.
 */
type ActionSlot = AdminAccountAction | "addressNote" | "emailChangeUnknown";

/** The handoff's order, narrowed by the state the account is in. */
function actionsFor(
  { status, emailConfirmed, isSuspended }: Pick<AdminAccountRow, "status" | "emailConfirmed" | "isSuspended">,
  emailChange: AdminEmailChangeState,
  administrator: boolean,
) {
  const general: ActionSlot[] = ["impersonate"];
  // A pending change is cancelled, never requested again: a second request would displace the code its owner holds.
  if (emailChange.kind === "pending") general.push("cancelEmailChange");
  else if (emailChange.kind === "unknown") general.push("emailChangeUnknown");
  else if (status === "active") general.push(administrator ? "addressNote" : "changeEmail");
  if (status === "active") general.push("sendLoginLink");
  if (status === "active" && !emailConfirmed) general.push("markVerified");
  const suspended = isSuspended ?? status === "suspended";
  if (suspended) general.push("reinstate");
  if (status === "pendingDeletion") general.push("restore");
  const destructive: AdminAccountAction[] = [];
  if (!suspended && (status === "active" || status === "pendingDeletion")) destructive.push("suspend");
  if (status !== "pendingDeletion") destructive.push("scheduleDeletion");
  destructive.push("deletePermanently");
  return { general, destructive };
}

function isAddressed(account: AdminAccountDetail): account is AdminAddressedAccount {
  return account.email !== null;
}

const sameId = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();

/**
 * The account panel (ADR 0150, handoff 10–12): a modal side panel with the account's facts and its
 * actions. Focus starts on the close button and returns to the row that opened the panel. An action
 * that is not built is shown in place, `aria-disabled` and saying "Kommer snart" (ADR 0150 D2); an
 * account without a profile has no actions at all (ADR 0151).
 * Escape is layered: in edit mode it leaves the edit, otherwise it closes the panel.
 */
export function AdminAccountPanel({
  account,
  details,
  onClose,
  commands,
  self,
  emailChange = NO_EMAIL_CHANGE,
  onRetry,
  onRetryEmailChange,
  fallbackFocus,
}: AdminAccountPanelProps) {
  const open = account !== null;
  const { onCloseAutoFocus: returnToOpener } = useReturnFocus(open);
  const onCloseAutoFocus = useCallback(
    (event: Event) => {
      returnToOpener(event);
      const active = document.activeElement;
      if (active === null || active === document.body) fallbackFocus?.()?.focus();
    },
    [returnToOpener, fallbackFocus],
  );

  return (
    <Dialog.Root
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="jp-adminpanel-scrim" />
        {account === null ? null : (
          <PanelContent
            key={account.id}
            row={account}
            details={details}
            commands={commands}
            self={self}
            emailChange={emailChange}
            onRetry={onRetry}
            onRetryEmailChange={onRetryEmailChange}
            onCloseAutoFocus={onCloseAutoFocus}
          />
        )}
      </Dialog.Portal>
    </Dialog.Root>
  );
}

function PanelContent({
  row,
  details,
  commands,
  self,
  emailChange,
  onRetry,
  onRetryEmailChange,
  onCloseAutoFocus,
}: {
  readonly row: AdminAccountRow;
  readonly details: AdminAccountDetails;
  readonly commands: AdminAccountCommands | undefined;
  readonly self: AdminSelf | undefined;
  readonly emailChange: AdminEmailChangeState;
  readonly onRetry: (() => void) | undefined;
  readonly onRetryEmailChange: (() => Promise<AdminEmailChangeReread>) | undefined;
  readonly onCloseAutoFocus: (event: Event) => void;
}) {
  const t = useTranslations("admin.users");
  const soon = useTranslations("admin.unavailable")("comingSoon");
  const unknown = useTranslations("admin.unavailable")("unknownValue");
  const format = useFormatter();

  const loaded = details.kind === "loaded" ? details.data : null;
  const head = loaded ?? row;
  const account = loaded !== null && isAddressed(loaded) ? loaded : null;
  const live = commands?.live ?? NO_LIVE_ACTIONS;
  const emailChangeCommands = commands?.emailChange;
  const accessCommands = commands?.access;
  const deletionCommands = commands?.deletion;
  // The administrator's own account, by its id: an address is no identity (security-auditor, #1975 C-1).
  const ownAccount = self !== undefined && sameId(head.id, self.userId);
  const administrator = head.role === "admin" || ownAccount;

  const [mode, setMode] = useState<"view" | "edit">("view");
  const [confirming, setConfirming] = useState<Confirming | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [pending, startTransition] = useTransition();
  const [running, setRunning] = useState<AdminLiveAction | null>(null);
  const titleRef = useRef<HTMLHeadingElement>(null);
  const noticeRef = useRef<HTMLParagraphElement>(null);
  const actionRefs = useRef<Partial<Record<AdminLiveAction, HTMLButtonElement | null>>>({});
  const reauthDialogRefs = useRef<Partial<Record<AdminAccountCommand["kind"], ReauthDialogHandle | null>>>({});
  const reauthOpener = useRef<AdminAccountCommand["kind"] | null>(null);
  const confirmOpener = useRef<Confirming | null>(null);
  const focusTitleAfterConfirm = useRef(false);
  const leftEdit = useRef(false);
  const pendingFocus = useRef<FocusTarget | null>(null);
  const exitFocus = useRef<FocusTarget | null>(null);
  const [retrying, setRetrying] = useState(false);
  const [retryAttempt, setRetryAttempt] = useState(0);
  const retryRef = useRef<HTMLButtonElement>(null);
  const emailChangeValueRef = useRef<HTMLElement>(null);
  const rereadFocus = useRef<RereadFocus | null>(null);
  const unknownNoteId = useId();

  // A receipt published while the panel holds focus waits for the panel to close (WCAG 2.2.1).
  useEffect(() => holdAdminToasts(), []);

  // Entering edit mode puts the caret in the new address; leaving it returns to the button that opened it.
  useEffect(() => {
    if (mode === "edit") document.getElementById(ADMIN_NEW_EMAIL_FIELD_ID)?.focus();
    else if (leftEdit.current) {
      leftEdit.current = false;
      actionRefs.current.changeEmail?.focus();
    }
  }, [mode]);

  // Focus follows what a command left on screen, once it is there.
  useEffect(() => {
    const target = pendingFocus.current;
    if (target === null) return;
    const element = (target === "title" ? titleRef : noticeRef).current;
    if (element === null) return;
    pendingFocus.current = null;
    element.focus();
  });

  function rereadElement(target: RereadFocus): HTMLElement | null {
    switch (target) {
      case "title":
        return titleRef.current;
      case "retry":
        return retryRef.current;
      case "emailChangeValue":
        return emailChangeValueRef.current;
      case "emailChangeAction":
        return actionRefs.current.changeEmail ?? titleRef.current;
    }
  }

  useLayoutEffect(() => {
    const target = rereadFocus.current;
    if (target === null) return;
    const element = rereadElement(target);
    if (element === null) return;
    rereadFocus.current = null;
    element.focus();
  });

  function focusOn(target: FocusTarget) {
    (target === "title" ? titleRef : noticeRef).current?.focus();
  }

  function rereadEmailChange(reread: () => Promise<AdminEmailChangeReread>) {
    if (retrying) return;
    setRetrying(true);
    void reread().then((answer) => {
      setRetrying(false);
      switch (answer.kind) {
        case "gone":
          rereadFocus.current = "title";
          return;
        case "unknown":
          setRetryAttempt((attempt) => attempt + 1);
          rereadFocus.current = "retry";
          return;
        case "pending":
          setNotice(null);
          rereadFocus.current = "emailChangeValue";
          return;
        case "none":
          setNotice(null);
          rereadFocus.current = "emailChangeAction";
          return;
      }
    });
  }

  function isLive(action: AdminAccountAction): action is AdminLiveAction {
    if (!(live as ReadonlySet<AdminAccountAction>).has(action)) return false;
    switch (action) {
      case "changeEmail":
        // The step-up code goes to the administrator's own address, so the panel must know it.
        return emailChangeCommands !== undefined && self !== undefined;
      case "cancelEmailChange":
        return emailChangeCommands !== undefined;
      case "suspend":
      case "reinstate":
        return (accessCommands !== undefined && self !== undefined) || commands?.run !== undefined;
      case "scheduleDeletion":
        return (deletionCommands !== undefined && self !== undefined && account?.deletionPreview != null)
          || commands?.run !== undefined;
      default:
        return commands?.run !== undefined;
    }
  }

  const deletionDate =
    commands?.deletionEarliestIfScheduledNow === undefined
      ? unknown
      : (formatDate(format, commands.deletionEarliestIfScheduledNow) ?? unknown);

  function receipt(target: AdminAddressedAccount, command: AdminAccountCommand): string {
    switch (command.kind) {
      case "suspend":
        return t("toast.suspended", { email: target.email });
      case "reinstate":
        return t("toast.reinstated", { email: target.email });
      case "scheduleDeletion":
        return t("toast.deletionScheduled", { email: target.email, date: deletionDate });
    }
  }

  async function run(target: AdminAddressedAccount, command: AdminAccountCommand): Promise<AdminCommandRefusal> {
    const runCommand = commands?.run;
    // Reached only through a live action, and an action is live only with the command that runs it.
    if (runCommand === undefined) throw new Error(`No command runs ${command.kind}.`);
    const outcome = await runCommand(target, command);
    if (outcome === null) showAdminToast(receipt(target, command));
    return outcome;
  }

  // A command that throws ends at the nearest error boundary rather than leaving the panel disabled.
  function runDirect(target: AdminAddressedAccount, command: { readonly kind: "reinstate" }) {
    setNotice(null);
    setRunning(command.kind);
    startTransition(async () => {
      const outcome = await run(target, command);
      startTransition(() => {
        setRunning(null);
        if (outcome === null) titleRef.current?.focus();
        else {
          setNotice({ text: outcome, role: "alert" });
          pendingFocus.current = "notice";
        }
      });
    });
  }

  function cancelNotice(outcome: Exclude<AdminEmailChangeCancelOutcome, { readonly kind: "cancelled" }>): string {
    switch (outcome.kind) {
      case "nothingPending":
        return t("emailChange.nothingToCancel");
      case "unknown":
        return t("emailChange.cancelUnknown");
      case "refused":
        switch (outcome.reason) {
          case "rateLimited":
            return t("errors.rateLimited", { seconds: outcome.retryAfterSeconds });
          case "unauthorized":
            return t("errors.unauthorized");
          case "forbidden":
            return t("errors.forbidden");
        }
    }
  }

  // No confirmation: a cancel removes nothing the account holds and returns it to its state before the request.
  function cancelEmailChange(target: AdminAddressedAccount) {
    const cancel = emailChangeCommands?.cancel;
    if (cancel === undefined) return;
    setNotice(null);
    setRunning("cancelEmailChange");
    startTransition(async () => {
      const outcome = await cancel(target);
      startTransition(() => {
        setRunning(null);
        if (outcome.kind === "cancelled") {
          showAdminToast(t("toast.emailChangeCancelled"));
          pendingFocus.current = "title";
          return;
        }
        setNotice({ text: cancelNotice(outcome), role: "status" });
        pendingFocus.current = "notice";
      });
    });
  }

  function activate(target: AdminAddressedAccount, action: AdminLiveAction) {
    setNotice(null);
    switch (action) {
      case "changeEmail":
        setMode("edit");
        return;
      case "cancelEmailChange":
        cancelEmailChange(target);
        return;
      case "suspend":
      case "scheduleDeletion":
        confirmOpener.current = action;
        setConfirming(action);
        return;
      case "reinstate":
        runDirect(target, { kind: "reinstate" });
        return;
    }
  }

  // A request that ended where the panel shows it: the form is left, and the dialog's close moves focus.
  function leaveEdit(exit: AdminEditExit) {
    leftEdit.current = false;
    setMode("view");
    switch (exit.kind) {
      case "requested":
        showAdminToast(
          t.rich("toast.emailChangeRequested", {
            email: exit.newEmail,
            from: formatDateTime(format, exit.completableFrom) ?? unknown,
            nowrap: unbroken,
          }),
        );
        exitFocus.current = "title";
        return;
      case "notice":
        setNotice({ text: exit.message, role: "status" });
        exitFocus.current = "notice";
        return;
      case "gone":
        exitFocus.current = "title";
        return;
    }
  }

  function focusAfterExit() {
    const target = exitFocus.current;
    exitFocus.current = null;
    if (target !== null) focusOn(target);
  }

  function onReauthOpenChange(action: AdminAccountCommand["kind"], next: boolean) {
    if (next) reauthOpener.current = action;
    else if (reauthOpener.current === action) reauthOpener.current = null;
  }

  function actionButton(target: AdminAddressedAccount, action: AdminAccountAction, destructive: boolean) {
    const Icon = ICONS[action];
    if (!isLive(action)) {
      // The accessible name reads "{action} Kommer snart"; no handler, no hover, no opacity (ADR 0150 D2).
      return (
        <li key={action}>
          <button
            type="button"
            aria-disabled="true"
            className="jp-btn jp-btn--secondary jp-adminpanel__action jp-adminpanel__action--soon"
          >
            <Icon size={18} aria-hidden="true" />
            <span>{t(`actions.${action}`)}</span>{" "}
            <span className="jp-adminpanel__soon">{soon}</span>
          </button>
        </li>
      );
    }
    if (action === "scheduleDeletion" && deletionCommands !== undefined && self !== undefined && target.deletionPreview != null) {
      const preview = target.deletionPreview;
      const description = t("deletion.confirm", {
        days: (Date.parse(preview.eligibleAt) - Date.parse(preview.deletedAt)) / 86_400_000,
        eligible: formatDateTime(format, preview.eligibleAt) ?? unknown,
        run: formatDateTime(format, preview.scheduledRunAt) ?? unknown,
      });
      const handOff = (outcome: ReauthHandOff<AdminDeletionReceipt>) => {
        if (outcome.kind === "verified") {
          const text = t("deletion.receipt", {
            email: target.email,
            deleted: formatDateTime(format, outcome.value.deletedAt) ?? unknown,
          });
          setNotice({ text, role: "status" });
        } else {
          setNotice({ text: outcome.error ?? t("deletion.deliveryUnavailable"),
            role: outcome.kind === "outcomeUnknown" ? "status" : "alert",
            reread: outcome.kind === "outcomeUnknown" });
        }
        exitFocus.current = "notice";
      };
      return (
        <li key={action}>
          <ReAuthCodeDialog<AdminDeletionReceipt>
            dialogRef={(handle) => { reauthDialogRefs.current[action] = handle; }}
            onOpenChange={(next) => onReauthOpenChange(action, next)}
            trigger={
              <button type="button" className="jp-btn jp-btn--danger jp-adminpanel__action"
                ref={(element) => { actionRefs.current[action] = element; }} onClick={() => setNotice(null)}>
                <Icon size={18} aria-hidden="true" />{t("actions.scheduleDeletion")}
              </button>
            }
            className="jp-adminstepup"
            title={t("confirm.scheduleDeletion.title", { email: target.email })}
            description={description}
            currentEmail={self.email}
            codeRecipientLabels={{
              request: t("deletion.sendCode", { email: self.email }),
              pending: t("deletion.sendingCode", { email: self.email }),
              field: t("deletion.code", { email: self.email }),
            }}
            confirmLabel={t("confirm.scheduleDeletion.confirm")}
            pendingLabel={t("busy.scheduleDeletion")}
            cancelLabel={t("confirm.cancel")}
            variant="destructive"
            returnPath={deletionCommands.returnPath}
            requestCode={deletionCommands.requestCode}
            action={(proof) => deletionCommands.run(target, proof)}
            onHandOff={handOff}
            focusAfterHandOff={focusAfterExit}
          />
        </li>
      );
    }
    if ((action === "suspend" || action === "reinstate") && accessCommands !== undefined && self !== undefined) {
      const description = t(`confirm.${action}.body`)
        + (action === "suspend" && emailChange.kind === "pending" ? ` ${t("access.cancelsPending")}` : "")
        + (action === "reinstate" && target.status === "pendingDeletion" ? ` ${t("access.deletionContinues")}` : "");
      const handOff = (outcome: ReauthHandOff<AdminAccessReceipt>) => {
        if (outcome.kind === "verified") {
          showAdminToast(receipt(target, { kind: action })
            + (action === "reinstate" && outcome.value.pendingDeletion ? ` ${t("access.deletionContinues")}` : ""));
          exitFocus.current = "title";
        } else {
          setNotice({ text: outcome.error ?? t("access.deliveryUnavailable"),
            role: outcome.kind === "outcomeUnknown" ? "status" : "alert" });
          exitFocus.current = "notice";
        }
      };
      return (
        <li key={action}>
          <ReAuthCodeDialog<AdminAccessReceipt>
            dialogRef={(handle) => { reauthDialogRefs.current[action] = handle; }}
            onOpenChange={(next) => onReauthOpenChange(action, next)}
            trigger={
              <button type="button" className={`jp-btn ${destructive ? "jp-btn--danger" : "jp-btn--secondary"} jp-adminpanel__action`}
                ref={(element) => { actionRefs.current[action] = element; }} onClick={() => setNotice(null)}>
                <Icon size={18} aria-hidden="true" />{t(`actions.${action}`)}
              </button>
            }
            className="jp-adminstepup"
            title={t(`confirm.${action}.title`, { email: target.email })}
            description={description}
            currentEmail={self.email}
            confirmLabel={t(`actions.${action}`)}
            pendingLabel={t(`busy.${action}`)}
            cancelLabel={t("confirm.cancel")}
            variant={action === "suspend" ? "destructive" : "default"}
            returnPath={accessCommands.returnPath}
            requestCode={accessCommands.requestCode}
            action={(proof) => accessCommands.run(target, action, proof)}
            onHandOff={handOff}
            focusAfterHandOff={focusAfterExit}
          />
        </li>
      );
    }
    return (
      <li key={action}>
        <button
          type="button"
          ref={(element) => {
            actionRefs.current[action] = element;
          }}
          className={`jp-btn ${destructive ? "jp-btn--danger" : "jp-btn--secondary"} jp-adminpanel__action`}
          disabled={pending}
          onClick={() => activate(target, action)}
        >
          <Icon size={18} aria-hidden="true" />
          {isDirect(action) ? (
            <AdminBusyLabel busy={running === action} label={t(`actions.${action}`)} busyLabel={t(`busy.${action}`)} />
          ) : (
            <span>{t(`actions.${action}`)}</span>
          )}
        </button>
      </li>
    );
  }

  // Where an administrator account's address change would stand: it is changed on Mina sidor, never here.
  function addressNote() {
    return (
      <li key="addressNote">
        <p className="jp-adminpanel__note">
          {t("panel.adminAddress")}
          {ownAccount ? <> {t("panel.adminAddressOwn")}</> : null}
        </p>
      </li>
    );
  }

  function emailChangeUnknown() {
    return (
      <li key="emailChangeUnknown" className="jp-adminpanel__failure">
        <p id={unknownNoteId} className="jp-adminpanel__note">
          {t("panel.emailChangeUnknown")}
        </p>
        {onRetryEmailChange === undefined ? null : (
          <button
            key={retryAttempt}
            ref={retryRef}
            type="button"
            className="jp-btn jp-btn--secondary jp-btn--sm"
            aria-describedby={unknownNoteId}
            aria-disabled={retrying || undefined}
            onClick={() => rereadEmailChange(onRetryEmailChange)}
          >
            <AdminBusyLabel busy={retrying} label={t("errors.retry")} busyLabel={t("busy.retry")} />
          </button>
        )}
      </li>
    );
  }

  function pendingLine(change: AdminPendingEmailChange) {
    const until = formatDateTime(format, change.expiresAt) ?? unknown;
    if (change.state === "codeBurned") return t.rich("panel.emailChangeBurned", { until, nowrap: unbroken });
    const from = formatDateTime(format, change.completableFrom) ?? unknown;
    return t.rich("panel.emailChangePending", { from, until, nowrap: unbroken });
  }

  function facts(detail: AdminAccountDetail) {
    const count = (label: string, value: number | null) =>
      value === null ? null : (
        <>
          <dt>{label}</dt>
          <dd>{format.number(value)}</dd>
        </>
      );
    return (
      <dl className="jp-admindl jp-adminpanel__facts">
        {detail.status === "pendingDeletion" && detail.isSuspended ? (
          <><dt>{t("panel.access")}</dt><dd><AdminAccountStatus status="suspended" /></dd></>
        ) : null}
        {detail.status === "pendingDeletion" ? (
          <>
            <dt>{t("panel.deletion")}</dt>
            <dd>
              {detail.deletion != null ? (
                t.rich("deletion.pending", {
                  eligible: formatDateTime(format, detail.deletion.eligibleAt) ?? unknown,
                  run: formatDateTime(format, detail.deletion.scheduledRunAt) ?? unknown,
                  nowrap: unbroken,
                })
              ) : detail.deletionEarliest === null ? (
                <AdminUnknown />
              ) : (
                t.rich("panel.deletionEarliest", { date: detail.deletionEarliest, nowrap: unbroken })
              )}
            </dd>
          </>
        ) : null}
        <dt>{t("table.registered")}</dt>
        <dd>{formatDateTime(format, detail.registeredAt) ?? <AdminUnknown />}</dd>
        <dt>{t("table.lastLogin")}</dt>
        <dd>
          <AdminUnknown />
        </dd>
        <dt>{t("table.lastActive")}</dt>
        <dd>
          <AdminUnknown />
        </dd>
        <dt>{t("panel.email")}</dt>
        <dd>{detail.emailConfirmed ? t("panel.emailConfirmed") : t("panel.emailUnconfirmed")}</dd>
        {/* A server's answer only, never the form's: a reload, a second tab and a second admin agree (#1975). */}
        {emailChange.kind === "none" ? null : (
          <>
            <dt>{t("panel.emailChange")}</dt>
            <dd ref={emailChangeValueRef} tabIndex={-1}>
              {emailChange.kind === "pending" ? pendingLine(emailChange.change) : <AdminUnknown />}
            </dd>
          </>
        )}
        {count(t("table.applications"), detail.applicationCount)}
        {count(t("panel.savedSearches"), detail.savedSearchCount)}
        {count(t("panel.resumes"), detail.resumeCount)}
      </dl>
    );
  }

  function actions(target: AdminAddressedAccount) {
    const { general, destructive } = actionsFor(target, emailChange, administrator);
    return (
      <section className="jp-adminpanel__actions" aria-label={t("panel.actions")}>
        <ul className="jp-adminpanel__list">
          {general.map((slot) => {
            if (slot === "addressNote") return addressNote();
            if (slot === "emailChangeUnknown") return emailChangeUnknown();
            return actionButton(target, slot, false);
          })}
        </ul>
        <hr className="jp-adminpanel__rule" />
        <ul className="jp-adminpanel__list">{destructive.map((action) => actionButton(target, action, true))}</ul>
        {notice === null ? null : (
          <p
            ref={noticeRef}
            tabIndex={-1}
            className={notice.role === "alert" ? "jp-adminpanel__refusal" : "jp-adminpanel__status"}
            role={notice.role}
          >
            {notice.text}
          </p>
        )}
        {notice?.reread && onRetry !== undefined ? (
          <button type="button" className="jp-btn jp-btn--secondary jp-btn--sm" onClick={() => {
            setNotice(null);
            pendingFocus.current = "title";
            onRetry();
          }}>{t("deletion.reread")}</button>
        ) : null}
      </section>
    );
  }

  function body() {
    if (details.kind === "loading") return <AdminRegionLine kind="loading" loading={t("panel.loading")} />;
    if (details.kind === "gone") return <AdminRegionLine kind="failed" failed={t("errors.gone")} />;
    if (details.kind === "failed") {
      return (
        <div className="jp-adminpanel__failure">
          <AdminRegionLine kind="failed" failed={details.message} />
          {details.recovery === "retry" && onRetry !== undefined ? (
            <button type="button" className="jp-btn jp-btn--secondary jp-btn--sm" onClick={onRetry}>
              {t("errors.retry")}
            </button>
          ) : null}
          {details.recovery === "signIn" ? (
            <Link href="/logga-in" className={STANDALONE_LINK}>
              {t("errors.signIn")}
            </Link>
          ) : null}
        </div>
      );
    }
    if (account !== null && mode === "edit" && self !== undefined && emailChangeCommands !== undefined) {
      return (
        <AdminAccountEditForm
          account={account}
          selfEmail={self.email}
          requestCode={emailChangeCommands.requestCode}
          request={(newEmail, proof) => emailChangeCommands.request(account, newEmail, proof)}
          returnPath={emailChangeCommands.returnPath}
          onExit={leaveEdit}
          focusAfterExit={focusAfterExit}
          onCancel={() => {
            leftEdit.current = true;
            setMode("view");
          }}
        />
      );
    }
    return (
      <>
        {facts(details.data)}
        {details.data.status === "profileMissing" ? (
          <p className="jp-adminpanel__note">{t("panel.profileMissing")}</p>
        ) : account === null ? (
          <p className="jp-adminpanel__note">{t("panel.noAddress")}</p>
        ) : (
          actions(account)
        )}
      </>
    );
  }

  return (
    <Dialog.Content
      className="jp-adminpanel"
      aria-describedby={undefined}
      onCloseAutoFocus={onCloseAutoFocus}
      onEscapeKeyDown={(event) => {
        if (event.defaultPrevented) return;
        const reauthAction = reauthOpener.current;
        if (reauthAction !== null) {
          const reauthDialog = reauthDialogRefs.current[reauthAction];
          if (reauthDialog != null) {
            event.preventDefault();
            reauthDialog.close();
            return;
          }
          reauthOpener.current = null;
        }
        // Right after a confirmation opens, Escape can still reach the panel: it cancels the
        // confirmation, never the panel.
        if (confirming !== null) {
          event.preventDefault();
          setConfirming(null);
          return;
        }
        if (mode !== "edit") return;
        event.preventDefault();
        leftEdit.current = true;
        setMode("view");
      }}
      onInteractOutside={(event) => {
        if (mode === "edit" || isInAdminToast(event.target)) event.preventDefault();
      }}
    >
      <div className="jp-adminpanel__head">
        <span className="jp-adminpanel__icon" aria-hidden="true">
          <UserRound size={22} />
        </span>
        <div className="jp-adminpanel__who">
          <Dialog.Title asChild>
            <h2 ref={titleRef} tabIndex={-1} className="jp-adminpanel__title">
              {head.email ?? <AdminUnknown />}
            </h2>
          </Dialog.Title>
          {details.kind === "gone" ? null : (
            <div className="jp-adminpanel__badges">
              <AdminRolePill role={head.role} />
              <AdminAccountStatus status={head.status} />
            </div>
          )}
        </div>
        <Dialog.Close className="jp-icon-btn" aria-label={t("panel.close")}>
          <X size={20} aria-hidden="true" />
        </Dialog.Close>
      </div>

      <div className="jp-adminpanel__body">{body()}</div>

      {account === null ? null : (
        <AdminConfirmDialog
          key={confirming ?? "none"}
          open={confirming !== null}
          title={
            confirming === "scheduleDeletion"
              ? t("confirm.scheduleDeletion.title", { email: account.email })
              : t("confirm.suspend.title", { email: account.email })
          }
          body={
            confirming === "scheduleDeletion"
              ? t("confirm.scheduleDeletion.body", { date: deletionDate })
              : t("confirm.suspend.body")
          }
          confirmLabel={
            confirming === "scheduleDeletion"
              ? t("confirm.scheduleDeletion.confirm")
              : t("confirm.suspend.confirm")
          }
          busyLabel={confirming === "scheduleDeletion" ? t("busy.scheduleDeletion") : t("busy.suspend")}
          onConfirm={async () => {
            if (confirming === null) return null;
            const outcome = await run(account, { kind: confirming });
            if (outcome === null) {
              focusTitleAfterConfirm.current = true;
              setConfirming(null);
            }
            return outcome;
          }}
          onCancel={() => setConfirming(null)}
          onCloseAutoFocus={(event) => {
            // A completed command lands on the account's title; a cancelled one returns to its button.
            event.preventDefault();
            const opener = confirmOpener.current;
            confirmOpener.current = null;
            if (focusTitleAfterConfirm.current) {
              focusTitleAfterConfirm.current = false;
              titleRef.current?.focus();
            } else if (opener !== null) {
              actionRefs.current[opener]?.focus();
            }
          }}
        />
      )}
    </Dialog.Content>
  );
}
