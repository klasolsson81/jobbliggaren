"use client";

// "use client": the panel holds its account's mode, the running command and its refusal.
import { useEffect, useRef, useState, useTransition } from "react";
import { Dialog } from "radix-ui";
import { useFormatter, useTranslations } from "next-intl";
import {
  BadgeCheck,
  Ban,
  Link2,
  Pencil,
  RotateCcw,
  Trash2,
  UserCog,
  UserRound,
  X,
  type LucideIcon,
} from "lucide-react";
import type {
  AdminAccountAction,
  AdminAccountDetail,
  AdminAccountStatus as Status,
} from "@/lib/admin/view-models";
import { formatDate, formatDateTime } from "@/lib/i18n/format";
import { useReturnFocus } from "@/lib/hooks/use-return-focus";
import { holdAdminToasts, showAdminToast } from "@/lib/admin/toast-store";
import { AdminAccountStatus, AdminRolePill } from "./admin-account-status";
import { ADMIN_NEW_EMAIL_FIELD_ID, AdminAccountEditForm } from "./admin-account-edit-form";
import { AdminBusyLabel } from "./admin-busy-label";
import { AdminConfirmDialog } from "./admin-confirm-dialog";
import { isInAdminToast } from "./admin-toast-host";

/** The actions whose flows exist in the MVP (#1975–#1977); every other action is "Kommer snart" here. */
export type AdminLiveAction = Extract<
  AdminAccountAction,
  "changeEmail" | "suspend" | "reinstate" | "scheduleDeletion"
>;

export type AdminAccountCommand =
  | { readonly kind: "changeEmail"; readonly newEmail: string }
  | { readonly kind: "suspend" }
  | { readonly kind: "reinstate" }
  | { readonly kind: "scheduleDeletion" };

/** Null when the command went through; otherwise the refusal, shown where the command was asked. */
export type AdminCommandRefusal = string | null;

type Confirming = "suspend" | "scheduleDeletion";

interface AdminAccountPanelProps {
  readonly account: AdminAccountDetail | null;
  readonly onClose: () => void;
  /** The actions that are built (ADR 0150 D4). */
  readonly live: ReadonlySet<AdminLiveAction>;
  readonly onCommand: (
    account: AdminAccountDetail,
    command: AdminAccountCommand,
  ) => Promise<AdminCommandRefusal>;
  /** ISO instant: the earliest permanent deletion a deletion scheduled now would get. */
  readonly deletionEarliestIfScheduledNow: string;
}

const ICONS: Readonly<Record<AdminAccountAction, LucideIcon>> = {
  impersonate: UserCog,
  changeEmail: Pencil,
  sendLoginLink: Link2,
  markVerified: BadgeCheck,
  reinstate: RotateCcw,
  restore: RotateCcw,
  suspend: Ban,
  scheduleDeletion: Trash2,
  deletePermanently: Trash2,
};

/** The handoff's order, narrowed by the status the account is in. */
function actionsFor(status: Status) {
  const general: AdminAccountAction[] = ["impersonate"];
  if (status !== "pendingDeletion") general.push("changeEmail");
  if (status === "active" || status === "unverified") general.push("sendLoginLink");
  if (status === "unverified") general.push("markVerified");
  if (status === "suspended") general.push("reinstate");
  if (status === "pendingDeletion") general.push("restore");
  const destructive: AdminAccountAction[] = [];
  if (status === "active") destructive.push("suspend");
  if (status !== "pendingDeletion") destructive.push("scheduleDeletion");
  destructive.push("deletePermanently");
  return { general, destructive };
}

function isLive(action: AdminAccountAction, live: ReadonlySet<AdminLiveAction>): action is AdminLiveAction {
  return (live as ReadonlySet<AdminAccountAction>).has(action);
}

/**
 * The account panel (ADR 0150, handoff 10–12): a modal side panel with the account's facts and its
 * actions. Focus starts on the close button and returns to the row that opened the panel. An action
 * that is not built is shown in place, `aria-disabled` and saying "Kommer snart" (ADR 0150 D2).
 * Escape is layered: in edit mode it leaves the edit, otherwise it closes the panel.
 */
export function AdminAccountPanel({
  account,
  onClose,
  live,
  onCommand,
  deletionEarliestIfScheduledNow,
}: AdminAccountPanelProps) {
  const open = account !== null;
  const { onCloseAutoFocus } = useReturnFocus(open);

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
            account={account}
            live={live}
            onCommand={onCommand}
            deletionEarliestIfScheduledNow={deletionEarliestIfScheduledNow}
            onCloseAutoFocus={onCloseAutoFocus}
          />
        )}
      </Dialog.Portal>
    </Dialog.Root>
  );
}

function PanelContent({
  account,
  live,
  onCommand,
  deletionEarliestIfScheduledNow,
  onCloseAutoFocus,
}: {
  readonly account: AdminAccountDetail;
  readonly live: ReadonlySet<AdminLiveAction>;
  readonly onCommand: AdminAccountPanelProps["onCommand"];
  readonly deletionEarliestIfScheduledNow: string;
  readonly onCloseAutoFocus: (event: Event) => void;
}) {
  const t = useTranslations("admin.users");
  const soon = useTranslations("admin.unavailable")("comingSoon");
  const unknown = useTranslations("admin.unavailable")("unknownValue");
  const format = useFormatter();

  const [mode, setMode] = useState<"view" | "edit">("view");
  const [confirming, setConfirming] = useState<Confirming | null>(null);
  const [refusal, setRefusal] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();
  const [running, setRunning] = useState<AdminLiveAction | null>(null);
  const titleRef = useRef<HTMLHeadingElement>(null);
  const refusalRef = useRef<HTMLParagraphElement>(null);
  const actionRefs = useRef<Partial<Record<AdminLiveAction, HTMLButtonElement | null>>>({});
  const confirmOpener = useRef<Confirming | null>(null);
  const focusTitleAfterConfirm = useRef(false);
  const leftEdit = useRef(false);

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

  useEffect(() => {
    if (refusal !== null) refusalRef.current?.focus();
  }, [refusal]);

  const deletionDate = formatDate(format, deletionEarliestIfScheduledNow) ?? unknown;

  function receipt(command: AdminAccountCommand): string {
    switch (command.kind) {
      case "changeEmail":
        return t("toast.emailChangeRequested", { email: command.newEmail });
      case "suspend":
        return t("toast.suspended", { email: account.email });
      case "reinstate":
        return t("toast.reinstated", { email: account.email });
      case "scheduleDeletion":
        return t("toast.deletionScheduled", { email: account.email, date: deletionDate });
    }
  }

  async function run(command: AdminAccountCommand): Promise<AdminCommandRefusal> {
    const outcome = await onCommand(account, command);
    if (outcome === null) showAdminToast(receipt(command));
    return outcome;
  }

  // A command that throws ends at the nearest error boundary rather than leaving the panel disabled.
  function runDirect(command: { readonly kind: "reinstate" }) {
    setRefusal(null);
    setRunning(command.kind);
    startTransition(async () => {
      const outcome = await run(command);
      startTransition(() => {
        setRunning(null);
        if (outcome === null) titleRef.current?.focus();
        else setRefusal(outcome);
      });
    });
  }

  function activate(action: AdminLiveAction) {
    setRefusal(null);
    switch (action) {
      case "changeEmail":
        setMode("edit");
        return;
      case "suspend":
      case "scheduleDeletion":
        confirmOpener.current = action;
        setConfirming(action);
        return;
      case "reinstate":
        runDirect({ kind: "reinstate" });
        return;
    }
  }

  function actionButton(action: AdminAccountAction, destructive: boolean) {
    const Icon = ICONS[action];
    if (!isLive(action, live)) {
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
    return (
      <li key={action}>
        <button
          type="button"
          ref={(element) => {
            actionRefs.current[action] = element;
          }}
          className={`jp-btn ${destructive ? "jp-btn--danger" : "jp-btn--secondary"} jp-adminpanel__action`}
          disabled={pending}
          onClick={() => activate(action)}
        >
          <Icon size={18} aria-hidden="true" />
          <AdminBusyLabel busy={running === action} label={t(`actions.${action}`)} busyLabel={t(`busy.${action}`)} />
        </button>
      </li>
    );
  }

  const { general, destructive } = actionsFor(account.status);
  const count = (value: number | null) => (value === null ? unknown : format.number(value));

  return (
    <Dialog.Content
      className="jp-adminpanel"
      aria-describedby={undefined}
      onCloseAutoFocus={onCloseAutoFocus}
      onEscapeKeyDown={(event) => {
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
              {account.email}
            </h2>
          </Dialog.Title>
          <div className="jp-adminpanel__badges">
            <AdminRolePill role={account.role} />
            <AdminAccountStatus status={account.status} deletionEarliest={account.deletionEarliest} />
          </div>
        </div>
        <Dialog.Close className="jp-icon-btn" aria-label={t("panel.close")}>
          <X size={20} aria-hidden="true" />
        </Dialog.Close>
      </div>

      <div className="jp-adminpanel__body">
        {mode === "edit" ? (
          <AdminAccountEditForm
            account={account}
            onSubmit={async (newEmail) => {
              const outcome = await run({ kind: "changeEmail", newEmail });
              if (outcome === null) {
                leftEdit.current = false;
                setMode("view");
                titleRef.current?.focus();
              }
              return outcome;
            }}
            onCancel={() => {
              leftEdit.current = true;
              setMode("view");
            }}
          />
        ) : (
          <>
            <dl className="jp-admindl jp-adminpanel__facts">
              <dt>{t("table.registered")}</dt>
              <dd>{formatDateTime(format, account.registeredAt) ?? unknown}</dd>
              <dt>{t("table.lastLogin")}</dt>
              <dd>{unknown}</dd>
              <dt>{t("table.lastActive")}</dt>
              <dd>{unknown}</dd>
              <dt>{t("table.applications")}</dt>
              <dd>{count(account.applicationCount)}</dd>
              <dt>{t("panel.savedSearches")}</dt>
              <dd>{count(account.savedSearchCount)}</dd>
              <dt>{t("panel.resumes")}</dt>
              <dd>{count(account.resumeCount)}</dd>
            </dl>
            <section className="jp-adminpanel__actions" aria-label={t("panel.actions")}>
              <ul className="jp-adminpanel__list">{general.map((action) => actionButton(action, false))}</ul>
              <hr className="jp-adminpanel__rule" />
              <ul className="jp-adminpanel__list">{destructive.map((action) => actionButton(action, true))}</ul>
              {refusal === null ? null : (
                <p ref={refusalRef} tabIndex={-1} className="jp-adminpanel__refusal" role="alert">
                  {refusal}
                </p>
              )}
            </section>
          </>
        )}
      </div>

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
          const outcome = await run({ kind: confirming });
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
    </Dialog.Content>
  );
}
