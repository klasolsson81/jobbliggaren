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
  AdminAccountRow,
  AdminAddressedAccount,
} from "@/lib/admin/view-models";
import { formatDate, formatDateTime } from "@/lib/i18n/format";
import { useReturnFocus } from "@/lib/hooks/use-return-focus";
import { holdAdminToasts, showAdminToast } from "@/lib/admin/toast-store";
import { AdminAccountStatus, AdminRolePill } from "./admin-account-status";
import { ADMIN_NEW_EMAIL_FIELD_ID, AdminAccountEditForm } from "./admin-account-edit-form";
import { AdminBusyLabel } from "./admin-busy-label";
import { AdminConfirmDialog } from "./admin-confirm-dialog";
import { AdminRegionLine } from "./admin-region-line";
import { AdminUnknown } from "./admin-unknown";
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

/** The built actions and how to run them (ADR 0150 D4). Without them every action is "Kommer snart". */
export interface AdminAccountCommands {
  readonly live: ReadonlySet<AdminLiveAction>;
  readonly run: (account: AdminAddressedAccount, command: AdminAccountCommand) => Promise<AdminCommandRefusal>;
  /** `YYYY-MM-DD`: the earliest permanent deletion a deletion scheduled now would get. */
  readonly deletionEarliestIfScheduledNow: string;
}

/** The panel's facts as they arrive, or why they did not: the caller words the failure. */
export type AdminAccountDetails =
  | { readonly kind: "loading" }
  | { readonly kind: "failed"; readonly message: string }
  | { readonly kind: "loaded"; readonly data: AdminAccountDetail };

type Confirming = "suspend" | "scheduleDeletion";

interface AdminAccountPanelProps {
  /** The row that opened the panel, or null while it is closed. The head shows it until the facts arrive. */
  readonly account: AdminAccountRow | null;
  readonly details: AdminAccountDetails;
  readonly onClose: () => void;
  readonly commands?: AdminAccountCommands;
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

const NO_LIVE_ACTIONS: ReadonlySet<AdminLiveAction> = new Set();

/** The handoff's order, narrowed by the state the account is in. */
function actionsFor({ status, emailConfirmed }: Pick<AdminAccountRow, "status" | "emailConfirmed">) {
  const general: AdminAccountAction[] = ["impersonate"];
  if (status !== "pendingDeletion") general.push("changeEmail");
  if (status === "active") general.push("sendLoginLink");
  if (status === "active" && !emailConfirmed) general.push("markVerified");
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

function isAddressed(account: AdminAccountDetail): account is AdminAddressedAccount {
  return account.email !== null;
}

/**
 * The account panel (ADR 0150, handoff 10–12): a modal side panel with the account's facts and its
 * actions. Focus starts on the close button and returns to the row that opened the panel. An action
 * that is not built is shown in place, `aria-disabled` and saying "Kommer snart" (ADR 0150 D2); an
 * account without a profile has no actions at all (ADR 0151).
 * Escape is layered: in edit mode it leaves the edit, otherwise it closes the panel.
 */
export function AdminAccountPanel({ account, details, onClose, commands }: AdminAccountPanelProps) {
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
            row={account}
            details={details}
            commands={commands}
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
  onCloseAutoFocus,
}: {
  readonly row: AdminAccountRow;
  readonly details: AdminAccountDetails;
  readonly commands: AdminAccountCommands | undefined;
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

  const deletionDate =
    commands === undefined ? unknown : (formatDate(format, commands.deletionEarliestIfScheduledNow) ?? unknown);

  function receipt(target: AdminAddressedAccount, command: AdminAccountCommand): string {
    switch (command.kind) {
      case "changeEmail":
        return t("toast.emailChangeRequested", { email: command.newEmail });
      case "suspend":
        return t("toast.suspended", { email: target.email });
      case "reinstate":
        return t("toast.reinstated", { email: target.email });
      case "scheduleDeletion":
        return t("toast.deletionScheduled", { email: target.email, date: deletionDate });
    }
  }

  async function run(target: AdminAddressedAccount, command: AdminAccountCommand): Promise<AdminCommandRefusal> {
    if (commands === undefined) return null;
    const outcome = await commands.run(target, command);
    if (outcome === null) showAdminToast(receipt(target, command));
    return outcome;
  }

  // A command that throws ends at the nearest error boundary rather than leaving the panel disabled.
  function runDirect(target: AdminAddressedAccount, command: { readonly kind: "reinstate" }) {
    setRefusal(null);
    setRunning(command.kind);
    startTransition(async () => {
      const outcome = await run(target, command);
      startTransition(() => {
        setRunning(null);
        if (outcome === null) titleRef.current?.focus();
        else setRefusal(outcome);
      });
    });
  }

  function activate(target: AdminAddressedAccount, action: AdminLiveAction) {
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
        runDirect(target, { kind: "reinstate" });
        return;
    }
  }

  function actionButton(target: AdminAddressedAccount, action: AdminAccountAction, destructive: boolean) {
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
          onClick={() => activate(target, action)}
        >
          <Icon size={18} aria-hidden="true" />
          <AdminBusyLabel busy={running === action} label={t(`actions.${action}`)} busyLabel={t(`busy.${action}`)} />
        </button>
      </li>
    );
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
        {detail.status === "pendingDeletion" ? (
          <>
            <dt>{t("panel.deletion")}</dt>
            <dd>
              {detail.deletionEarliest === null ? (
                <AdminUnknown />
              ) : (
                t("panel.deletionEarliest", { date: detail.deletionEarliest })
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
        {count(t("table.applications"), detail.applicationCount)}
        {count(t("panel.savedSearches"), detail.savedSearchCount)}
        {count(t("panel.resumes"), detail.resumeCount)}
      </dl>
    );
  }

  function actions(target: AdminAddressedAccount) {
    const { general, destructive } = actionsFor(target);
    return (
      <section className="jp-adminpanel__actions" aria-label={t("panel.actions")}>
        <ul className="jp-adminpanel__list">{general.map((action) => actionButton(target, action, false))}</ul>
        <hr className="jp-adminpanel__rule" />
        <ul className="jp-adminpanel__list">{destructive.map((action) => actionButton(target, action, true))}</ul>
        {refusal === null ? null : (
          <p ref={refusalRef} tabIndex={-1} className="jp-adminpanel__refusal" role="alert">
            {refusal}
          </p>
        )}
      </section>
    );
  }

  function body() {
    if (loaded === null) {
      return (
        <AdminRegionLine
          kind={details.kind === "failed" ? "failed" : "loading"}
          failed={details.kind === "failed" ? details.message : undefined}
          loading={t("panel.loading")}
        />
      );
    }
    if (account !== null && mode === "edit") {
      return (
        <AdminAccountEditForm
          account={account}
          onSubmit={async (newEmail) => {
            const outcome = await run(account, { kind: "changeEmail", newEmail });
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
      );
    }
    return (
      <>
        {facts(loaded)}
        {loaded.status === "profileMissing" ? (
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
          <div className="jp-adminpanel__badges">
            <AdminRolePill role={head.role} />
            <AdminAccountStatus status={head.status} />
          </div>
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
